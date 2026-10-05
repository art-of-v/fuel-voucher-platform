-- =============================================================================================
-- Purge ALL test data
--
-- Everything except the one real customer. "Real" is resolved by phone suffix, never by id: the
-- suffix is %1771, and so is every company that customer works for.
--
-- Why this exists separately from 00-audit-expired.sql: that script only clears the expired backlog so
-- a campaign finds the right stock. This one empties the database of test history — orders,
-- fulfilments, renewals, exchanges, vouchers, refund rows — so a campaign starts from a known state
-- and its own residue is the only residue.
--
-- Dry run by default. Apply with -v apply=1.
--
--   docker exec -i fuelflow-postgres psql -U fuelflow -d fuelflow -v ON_ERROR_STOP=1 < 98-purge-test-data.sql
--   docker exec -i fuelflow-postgres psql -U fuelflow -d fuelflow -v ON_ERROR_STOP=1 -v apply=1 < 98-purge-test-data.sql
--
-- Kept on purpose: app_settings (the renewal and term ladders are configuration, not data) and
-- voucher_imports (the audit trail of what was once loaded). Both are reported so the operator can see
-- them rather than wonder.
--
-- Delete order matters, because fuel_vouchers is referenced with ON DELETE RESTRICT from four places:
--   fulfilments.voucher_id, voucher_renewal_items.source_voucher_id, voucher_exchanges.old_voucher_id,
--   operator_voucher_renewals.voucher_id  — unhook those before touching the voucher.
--   fuel_vouchers.order_id is RESTRICT too, and CHECK ck_voucher_held_has_order requires it on any
--   held voucher, so never UPDATE it to NULL to "unlink" a voucher: that trips the check. Delete the
--   voucher instead, then the order that was behind it.
-- =============================================================================================

\set ON_ERROR_STOP on
\if :{?apply}
\else
  \set apply 0
\endif

\echo ''
\echo '################ DRY RUN — nothing is being changed ################'

BEGIN;

CREATE TEMP TABLE _real_user AS
SELECT id, phone_number FROM users WHERE phone_number LIKE '%1771';

CREATE TEMP TABLE _real_company AS
SELECT DISTINCT le.id
FROM legal_entities le
WHERE EXISTS (
    SELECT 1 FROM company_members m
    WHERE m.legal_entity_id = le.id
      AND m.worker_user_id IN (SELECT id FROM _real_user)
);

-- What survives: the real customer's own orders and vouchers, plus their company's.
CREATE TEMP TABLE _keep_order AS
SELECT id, 'real customer order' AS reason FROM orders
WHERE user_id IN (SELECT id FROM _real_user)
UNION ALL
SELECT id, 'real company order' FROM orders
WHERE legal_entity_id IN (SELECT id FROM _real_company);

CREATE TEMP TABLE _keep_voucher AS
SELECT id, 'real customer voucher' AS reason FROM fuel_vouchers
WHERE assigned_to_user_id IN (SELECT id FROM _real_user)
   OR legal_entity_id IN (SELECT id FROM _real_company);

\echo ''
\echo '=== kept ==='
SELECT 'vouchers' AS what, count(*) FROM _keep_voucher
UNION ALL SELECT 'orders', count(*) FROM _keep_order;

SELECT DISTINCT k.reason, u.phone_number
FROM _keep_voucher k JOIN fuel_vouchers v ON v.id = k.id
JOIN users u ON u.id = v.assigned_to_user_id;

\echo ''
\echo '=== what would be deleted ==='
SELECT 'vouchers' AS what, count(*) AS n FROM fuel_vouchers v
  WHERE NOT EXISTS (SELECT 1 FROM _keep_voucher k WHERE k.id = v.id)
UNION ALL SELECT 'orders', count(*) FROM orders o
  WHERE NOT EXISTS (SELECT 1 FROM _keep_order k WHERE k.id = o.id)
UNION ALL SELECT 'fulfilments', count(*) FROM fulfillments f
  WHERE NOT EXISTS (SELECT 1 FROM _keep_order k WHERE k.id = f.order_id)
UNION ALL SELECT 'order line items', count(*) FROM order_line_items li
  WHERE NOT EXISTS (SELECT 1 FROM _keep_order k WHERE k.id = li.order_id)
UNION ALL SELECT 'refunds', count(*) FROM refunds r
  WHERE NOT EXISTS (SELECT 1 FROM _keep_order k WHERE k.id = r.order_id)
UNION ALL SELECT 'renewal items', count(*) FROM voucher_renewal_items i
  WHERE NOT EXISTS (SELECT 1 FROM _keep_order k WHERE k.id = i.order_id)
UNION ALL SELECT 'supplier exchanges', count(*) FROM voucher_exchanges x
  WHERE NOT EXISTS (SELECT 1 FROM _keep_voucher k WHERE k.id = x.old_voucher_id)
UNION ALL SELECT 'operator renewals', count(*) FROM operator_voucher_renewals o
  WHERE NOT EXISTS (SELECT 1 FROM _keep_voucher k WHERE k.id = o.voucher_id)
UNION ALL SELECT 'emulator packages', count(*) FROM fuel_packages WHERE id LIKE 'emu2-pkg-%';

\echo ''
\echo '=== vouchers by owner, to be deleted ==='
SELECT coalesce(u.phone_number, '(unowned / stock)') AS owner, v.status, count(*) AS n
FROM fuel_vouchers v
LEFT JOIN users u ON u.id = v.assigned_to_user_id
WHERE NOT EXISTS (SELECT 1 FROM _keep_voucher k WHERE k.id = v.id)
GROUP BY 1, 2 ORDER BY 1, 2;

\if :apply
  \echo ''
  \echo '################ APPLYING ################'

  -- 1. Unhook the voucher references (all RESTRICT).
  DELETE FROM operator_voucher_renewals o
  WHERE NOT EXISTS (SELECT 1 FROM _keep_voucher k WHERE k.id = o.voucher_id);

  UPDATE voucher_exchanges x SET new_voucher_id = NULL
  WHERE x.new_voucher_id IS NOT NULL
    AND NOT EXISTS (SELECT 1 FROM _keep_voucher k WHERE k.id = x.new_voucher_id);

  DELETE FROM voucher_exchanges x
  WHERE NOT EXISTS (SELECT 1 FROM _keep_voucher k WHERE k.id = x.old_voucher_id);

  DELETE FROM voucher_renewal_items i
  WHERE NOT EXISTS (SELECT 1 FROM _keep_order k WHERE k.id = i.order_id)
     OR NOT EXISTS (SELECT 1 FROM _keep_voucher k WHERE k.id = i.source_voucher_id);

  -- 2. Vouchers go before their orders: fuel_vouchers.order_id is RESTRICT and the held-voucher check
  --    makes clearing it illegal anyway.
  DELETE FROM fulfillments f
  WHERE NOT EXISTS (SELECT 1 FROM _keep_order k WHERE k.id = f.order_id)
     OR NOT EXISTS (SELECT 1 FROM _keep_voucher k WHERE k.id = f.voucher_id);

  DELETE FROM fuel_vouchers v
  WHERE NOT EXISTS (SELECT 1 FROM _keep_voucher k WHERE k.id = v.id);

  -- 3. Now the orders and everything that cascades from them.
  DELETE FROM orders o
  WHERE NOT EXISTS (SELECT 1 FROM _keep_order k WHERE k.id = o.id);

  -- 4. Fixture catalog rows.
  DELETE FROM fuel_packages WHERE id LIKE 'emu2-pkg-%';

  -- 5. Outbox and notifications carry order ids inside jsonb, so no FK protects them. Leaving an
  --    OrderFulfilled event for an order that no longer exists would make the backfill's dedup check
  --    skip a future order that reuses the id — improbable, but the rows are meaningless now anyway.
  DELETE FROM outbox_events
  WHERE payload::text ~ 'orderId' AND NOT EXISTS (
      SELECT 1 FROM orders o WHERE o.id::text = outbox_events.payload::text
  );

COMMIT;

  \echo ''
  \echo '=== after ==='
  SELECT 'vouchers' AS what, count(*) AS n FROM fuel_vouchers
  UNION ALL SELECT 'orders', count(*) FROM orders
  UNION ALL SELECT 'fulfilments', count(*) FROM fulfillments
  UNION ALL SELECT 'order line items', count(*) FROM order_line_items
  UNION ALL SELECT 'refunds', count(*) FROM refunds
  UNION ALL SELECT 'renewal items', count(*) FROM voucher_renewal_items
  UNION ALL SELECT 'supplier exchanges', count(*) FROM voucher_exchanges
  UNION ALL SELECT 'operator renewals', count(*) FROM operator_voucher_renewals
  UNION ALL SELECT 'fuel packages (catalog)', count(*) FROM fuel_packages
  UNION ALL SELECT 'users (kept)', count(*) FROM users;

  \echo ''
  \echo '--- the real customer must be intact ---'
  SELECT v.voucher_number, v.status, v.customer_expiration_date, v.provider_expiration_date
  FROM fuel_vouchers v JOIN users u ON u.id = v.assigned_to_user_id
  WHERE u.phone_number LIKE '%1771' ORDER BY v.customer_expiration_date;

  \echo ''
  \echo '--- configuration and audit trail deliberately kept ---'
  SELECT 'app_settings' AS what, count(*) FROM app_settings
  UNION ALL SELECT 'voucher_imports', count(*) FROM voucher_imports
  UNION ALL SELECT 'users', count(*) FROM users;
\else
  \echo ''
  \echo '################ DRY RUN ONLY — re-run with -v apply=1 ################'
\endif