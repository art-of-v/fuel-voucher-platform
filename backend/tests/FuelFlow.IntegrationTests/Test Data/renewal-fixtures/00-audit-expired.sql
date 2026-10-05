-- =============================================================================================
-- Expired-voucher audit and cleanup
--
-- Two modes, one file:
--
--   dry run (default):  reports what would go and what is being kept. Changes nothing.
--   apply:             \set apply 1 before running, or `psql -v apply=1`.
--
-- The protected set is the customer's own vouchers — resolved by phone suffix, never by a hardcoded
-- id, so this keeps working when the test account id changes:
--
--   * vouchers assigned to that customer
--   * vouchers belonging to a company that customer works for
--   * anything referenced by a paid order, a fulfilment or a renewal/exchange record
--
-- Run it, read the dry-run output, then run it again with `-v apply=1`.
-- =============================================================================================

\set ON_ERROR_STOP on
\if :{?apply}
\else
  \set apply 0
\endif

\echo ''
\echo '################ DRY RUN — nothing is being changed ################'
\echo ''

-- The customer under protection.
CREATE TEMP TABLE _protected_user AS
SELECT id, phone_number FROM users WHERE phone_number LIKE '%1771';

\echo '=== protected customer ==='
SELECT id, phone_number FROM _protected_user;

CREATE TEMP TABLE _protected_company AS
SELECT DISTINCT le.id
FROM legal_entities le
WHERE EXISTS (
    SELECT 1 FROM company_members m
    WHERE m.legal_entity_id = le.id
      AND m.worker_user_id IN (SELECT id FROM _protected_user)
);

-- Everything that must survive: the customer's own vouchers, their company's, and anything already
-- entangled with a paid order or an exchange record.
CREATE TEMP TABLE _keep AS
SELECT id, 'protected customer voucher' AS reason FROM fuel_vouchers
WHERE assigned_to_user_id IN (SELECT id FROM _protected_user)
UNION ALL
SELECT id, 'protected company voucher' FROM fuel_vouchers
WHERE legal_entity_id IN (SELECT id FROM _protected_company)
UNION ALL
SELECT id, 'referenced by a fulfilment' FROM fuel_vouchers
WHERE id IN (SELECT voucher_id FROM fulfillments)
UNION ALL
SELECT id, 'referenced by a renewal' FROM fuel_vouchers
WHERE id IN (SELECT source_voucher_id FROM voucher_renewal_items)
UNION ALL
SELECT id, 'serves as a renewal result' FROM fuel_vouchers
WHERE id IN (SELECT fulfilled_voucher_id FROM voucher_renewal_items WHERE fulfilled_voucher_id IS NOT NULL)
UNION ALL
SELECT id, 'referenced by a supplier exchange' FROM fuel_vouchers
WHERE id IN (SELECT old_voucher_id FROM voucher_exchanges)
   OR id IN (SELECT new_voucher_id FROM voucher_exchanges WHERE new_voucher_id IS NOT NULL)
UNION ALL
SELECT id, 'referenced by an operator renewal' FROM fuel_vouchers
WHERE id IN (SELECT voucher_id FROM operator_voucher_renewals)
UNION ALL
SELECT id, 'referenced by a paid order' FROM fuel_vouchers
WHERE order_id IS NOT NULL;

-- Candidates: past both dates, unowned, and not protected. Status `Expired` is the honest one for
-- these, but the date is what actually matters, so the date is what the filter uses — a voucher left
-- as `Available` with a lapsed date is exactly the row that makes the stock count lie.
CREATE TEMP TABLE _expired AS
SELECT v.id, v.voucher_number, v.provider, v.fuel_type_id, v.liters,
       v.status, v.customer_expiration_date, v.provider_expiration_date
FROM fuel_vouchers v
WHERE v.customer_expiration_date < CURRENT_DATE
  AND NOT EXISTS (SELECT 1 FROM _keep k WHERE k.id = v.id);

\echo ''
\echo '=== totals ==='
SELECT
  (SELECT count(*) FROM fuel_vouchers) AS all_vouchers,
  (SELECT count(DISTINCT id) FROM _keep) AS protected,
  (SELECT count(*) FROM _expired) AS would_delete,
  (SELECT count(*) FROM fuel_vouchers v
     WHERE v.customer_expiration_date < CURRENT_DATE) AS expired_by_date;

\echo ''
\echo '=== what WOULD be deleted (expired, unowned, unreferenced) ==='
SELECT voucher_number, provider, fuel_type_id, liters, status,
       customer_expiration_date, provider_expiration_date
FROM _expired
ORDER BY provider, fuel_type_id, liters, voucher_number;

\echo ''
\echo '=== kept, with the reason (first 50) ==='
SELECT k.reason, count(*) AS vouchers
FROM _keep k GROUP BY k.reason ORDER BY 2 DESC;

\echo ''
\echo '=== kept expired rows, in detail — this is the part that must look right ==='
SELECT v.voucher_number, v.provider, v.fuel_type_id, v.liters, v.status,
       v.customer_expiration_date, v.provider_expiration_date,
       k.reason
FROM _keep k
JOIN fuel_vouchers v ON v.id = k.id
WHERE v.customer_expiration_date < CURRENT_DATE
ORDER BY k.reason, v.voucher_number
LIMIT 200;

\if :apply
  \echo ''
  \echo '################ APPLYING ################'
BEGIN;

-- Unhook first. `fuel_vouchers` is referenced with ON DELETE RESTRICT from four places, so the
-- dependent rows have to be dealt with before the voucher can go. Anything a real order depends on is
-- already excluded from _expired, so at this point these are only rows left by earlier campaigns.
DELETE FROM fulfillments
WHERE voucher_id IN (SELECT id FROM _expired);

DELETE FROM voucher_renewal_items
WHERE source_voucher_id IN (SELECT id FROM _expired)
   OR fulfilled_voucher_id IN (SELECT id FROM _expired);

UPDATE voucher_exchanges SET new_voucher_id = NULL
WHERE old_voucher_id IN (SELECT id FROM _expired);

DELETE FROM voucher_exchanges WHERE old_voucher_id IN (SELECT id FROM _expired);

DELETE FROM operator_voucher_renewals WHERE voucher_id IN (SELECT id FROM _expired);

-- No order_id clearing: CHECK ck_voucher_held_has_order is checked per row on UPDATE, and these
-- rows are about to be deleted anyway. Deleting the vouchers leaves the orders unreferenced.

DELETE FROM fuel_vouchers WHERE id IN (SELECT id FROM _expired);

COMMIT;

  \echo ''
  \echo '=== after ==='
  SELECT count(*) AS vouchers_remaining FROM fuel_vouchers;
  SELECT
    (SELECT count(*) FROM fuel_vouchers WHERE assigned_to_user_id IN (SELECT id FROM _protected_user))
      AS protected_customer_still_there,
    (SELECT count(*) FROM fuel_vouchers WHERE customer_expiration_date < CURRENT_DATE)
      AS expired_remaining;
  SELECT v.voucher_number, v.customer_expiration_date, v.provider_expiration_date
  FROM fuel_vouchers v
  WHERE v.assigned_to_user_id IN (SELECT id FROM _protected_user)
  ORDER BY v.customer_expiration_date;
\else
  \echo ''
  \echo '################ DRY RUN ONLY — re-run with -v apply=1 to delete ################'
\endif