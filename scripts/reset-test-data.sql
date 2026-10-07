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
-- number (numeric `external_id`/`voucher_number`, e.g. the real customer's 999996...) are never
-- matched: they belong to a real customer, not to a campaign.
--
-- Delete order matters, and it is not stylistic:
--   * `fuel_vouchers.order_id` restricts, so a held voucher blocks its order from being deleted.
--   * `fulfillments` now cascades from its order (FK added in AddFulfillmentOrderForeignKey), but
--     its `voucher_id` restricts, so vouchers have to go too.
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
WHERE v.external_id LIKE 'EMU%'
   OR v.external_id LIKE 'TEST%'
   OR v.voucher_number LIKE 'EMU%'
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

DELETE FROM voucher_exchanges
WHERE old_voucher_id IN (SELECT id FROM reset_vouchers)
   OR new_voucher_id IN (SELECT id FROM reset_vouchers);

DELETE FROM voucher_renewal_items
WHERE source_voucher_id IN (SELECT id FROM reset_vouchers)
   OR fulfilled_voucher_id IN (SELECT id FROM reset_vouchers)
   OR order_id IN (SELECT id FROM reset_orders);

DELETE FROM operator_voucher_renewals
WHERE voucher_id NOT IN (SELECT id FROM fuel_vouchers)
   OR (replacement_voucher_id IS NOT NULL
       AND replacement_voucher_id NOT IN (SELECT id FROM fuel_vouchers));

DELETE FROM fulfillments
WHERE order_id IN (SELECT id FROM reset_orders)
   OR voucher_id IN (SELECT id FROM reset_vouchers);

DELETE FROM order_line_items WHERE order_id IN (SELECT id FROM reset_orders);
DELETE FROM fuel_vouchers WHERE id IN (SELECT id FROM reset_vouchers);
DELETE FROM orders WHERE id IN (SELECT id FROM reset_orders);

-- Anything the campaign left pointing at a voucher that no longer exists.
DELETE FROM fulfillments WHERE voucher_id NOT IN (SELECT id FROM fuel_vouchers);

\echo '--- after ---'
SELECT 'campaign_vouchers_left' AS check, count(*) AS value
FROM fuel_vouchers
WHERE coalesce(external_id, '') LIKE 'EMU%' OR coalesce(external_id, '') LIKE 'TEST%'
   OR coalesce(voucher_number, '') LIKE 'EMU%' OR coalesce(voucher_number, '') LIKE 'TEST%'
   OR coalesce(qr_payload, '') LIKE 'EMU%' OR coalesce(qr_payload, '') LIKE 'TEST%';

SELECT 'test_phone_orders_left' AS check, count(*) AS value
FROM orders o JOIN users u ON u.id = o.user_id
WHERE u.phone_number IN ('+380677757766', '+380110010203');

SELECT 'real_vouchers_kept' AS check, count(*) AS value
FROM fuel_vouchers WHERE coalesce(voucher_number, '') LIKE '999996%';

COMMIT;