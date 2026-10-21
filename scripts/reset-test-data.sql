-- Resets the test data a renewal/renewal-campaign run leaves behind, without touching anyone
-- else's fuel. Safe to run against the deployed database between test sessions: everything it
-- deletes is either a seeded `EMU*`/`TEST*` voucher or an order placed by a phone number listed
-- below.
--
-- Usage (from the app server, where the postgres container is):
--
--   docker exec -i <pg-container> psql -U <user> -d <db> -v ON_ERROR_STOP=1 \
--     -f - < scripts/reset-test-data.sql
--
-- On the current deployment that is:
--
--   docker exec -i fuelflow-postgres psql -U fuelflow -d fuelflow -v ON_ERROR_STOP=1 \
--     -f - < scripts/reset-test-data.sql
--
-- Change `test_phones` to the accounts your session uses. Vouchers that carry a paper voucher
-- number (numeric `voucher_number`, e.g. the real customer's 999996...) are never matched: they
-- belong to a real customer, not to a campaign.
--
-- Delete order matters, and it is not stylistic:
--   * `fuel_vouchers.order_id` restricts, so a held voucher blocks its order from being deleted.
--   * `payments.order_id` restricts and does NOT cascade, so a paid test order blocks its own
--     deletion until its payment rows are removed first. A refund is a payment (`kind = 'Refund'`),
--     not a row in a table of its own, and the capture it reverses restricts the same way.
--   * `fulfillments` now cascades from its order (FK added in AddFulfillmentOrderForeignKey), but
--     its `voucher_id` restricts, so vouchers have to go too.
--   * every row a renewal or an exchange writes -- `order_line_items.source_voucher_id` (the
--     renewal lines themselves, since `voucher_renewal_items` was dropped in `20261017073518` and
--     the operator's renewals became orders in `20261021143655`), and the ledger's
--     `voucher_movements.voucher_id` / `replaced_by_voucher_id` -- restricts against
--     `fuel_vouchers`, so a session that produced a renewal cannot be reset until those audit rows
--     are cleared first.
-- Running it inside one transaction means a failure anywhere leaves the database untouched.

\set ON_ERROR_STOP on
BEGIN;

CREATE TEMP TABLE reset_orders ON COMMIT DROP AS
SELECT o.id
FROM orders o
JOIN users u ON u.id = o.user_id
WHERE u.phone_number IN (
    -- 7766: the owner's own device account, used for the renewal campaign.
    '+380677757766',
    -- 0203: the QA account the first campaign ran on.
    '+380110010203'
);

CREATE TEMP TABLE reset_vouchers ON COMMIT DROP AS
SELECT v.id
FROM fuel_vouchers v
WHERE v.voucher_number LIKE 'EMU%'
   OR v.voucher_number LIKE 'TEST%'
   OR v.qr_payload LIKE 'EMU%'
   OR v.qr_payload LIKE 'TEST%'
   OR v.assigned_to_user_id IN (SELECT id FROM users WHERE phone_number IN ('+380677757766', '+380110010203'))
   OR v.order_id IN (SELECT id FROM reset_orders);

-- Report before deleting, so a wrong phone number is visible in the scrollback rather than silent.
\echo '--- orders to delete ---'
SELECT kind, status, count(*), sum(price) AS total
FROM orders o JOIN reset_orders r ON r.id = o.id
GROUP BY kind, status ORDER BY kind, status;

\echo '--- vouchers to delete ---'
SELECT coalesce(voucher_number, '(null)') AS voucher_number, status, count(*)
FROM fuel_vouchers v JOIN reset_vouchers r ON r.id = v.id
GROUP BY 1, 2 ORDER BY 1;

-- The exchange pairs live in the ledger now (#319), on the row of the retired paper, and they
-- restrict onto the vouchers they name - so the rows go before the vouchers do, and the batches
-- they were part of follow. A batch that still has members left is kept rather than half-deleted.
DELETE FROM voucher_movements
WHERE voucher_id IN (SELECT id FROM reset_vouchers)
   OR replaced_by_voucher_id IN (SELECT id FROM reset_vouchers);

DELETE FROM movement_batches b
WHERE NOT EXISTS (SELECT 1 FROM voucher_movements m WHERE m.batch_id = b.id);

-- `voucher_renewal_items` was dropped in `20261017073518_DropVoucherRenewalItems`, and the renewal
-- rows live in `order_line_items` now: `source_voucher_id` is the column that points at the voucher
-- a line renews, and it RESTRICTs, so the line has to go before the voucher does. The same is true
-- of `order_id`, and it is the reason a session that produced a renewal cannot be reset until
-- those audit rows are cleared first.
--
-- This statement is the old one, rewritten for the table that replaced it. It deletes only the
-- renewal lines - the order's own lines are deleted further down, with the order.
DELETE FROM order_line_items
WHERE source_voucher_id IN (SELECT id FROM reset_vouchers);

-- An operator renewal is an ORDER now (#319): the renewal's intent and outcome live on the order
-- line, and `source_voucher_id` is the column that points at the voucher a line renews - so the
-- statement above, which deletes the renewal lines, is what has to go before the voucher it points
-- at. Otherwise the DELETE below dies on the FK and the whole reset rolls back.

DELETE FROM fulfillments
WHERE order_id IN (SELECT id FROM reset_orders)
   OR voucher_id IN (SELECT id FROM reset_vouchers);

-- `payments.order_id` restricts and does not cascade, so a paid test order cannot be deleted until
-- its payment rows are gone. Both kinds have to go: a refund payment AND the capture it reverses,
-- because a refund is a payment rather than a row in a table of its own. Deleting them here is
-- correct for a test reset: the money movement is part of what the session produced.
--
-- Two statements, children first: a refund points at its capture and that FK restricts too. RESTRICT
-- is immediate rather than deferred, so one DELETE covering both kinds fails the moment it reaches a
-- capture whose refund has not gone yet.
DELETE FROM payments WHERE order_id IN (SELECT id FROM reset_orders) AND kind = 'Refund';

DELETE FROM payments WHERE order_id IN (SELECT id FROM reset_orders);

DELETE FROM order_line_items WHERE order_id IN (SELECT id FROM reset_orders);
DELETE FROM fuel_vouchers WHERE id IN (SELECT id FROM reset_vouchers);
DELETE FROM orders WHERE id IN (SELECT id FROM reset_orders);

-- Anything the campaign left pointing at a voucher that no longer exists.
DELETE FROM fulfillments WHERE voucher_id NOT IN (SELECT id FROM fuel_vouchers);

\echo '--- after ---'
SELECT 'campaign_vouchers_left' AS check, count(*) AS value
FROM fuel_vouchers
WHERE coalesce(voucher_number, '') LIKE 'EMU%' OR coalesce(voucher_number, '') LIKE 'TEST%'
   OR coalesce(qr_payload, '') LIKE 'EMU%' OR coalesce(qr_payload, '') LIKE 'TEST%';

SELECT 'test_phone_orders_left' AS check, count(*) AS value
FROM orders o JOIN users u ON u.id = o.user_id
WHERE u.phone_number IN ('+380677757766', '+380110010203');

SELECT 'real_vouchers_kept' AS check, count(*) AS value
FROM fuel_vouchers WHERE coalesce(voucher_number, '') LIKE '999996%';

COMMIT;