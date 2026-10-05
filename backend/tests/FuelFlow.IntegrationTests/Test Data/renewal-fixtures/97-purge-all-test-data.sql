-- =============================================================================================
-- Purge ALL test data — everything except the one real customer
--
-- This is the nuclear version of 98-purge-test-data.sql. That one clears orders and vouchers; this
-- one also clears the sessions, notifications, device registrations, OTP codes, webhook replays,
-- supplier imports and test companies that every past campaign left behind.
--
-- "Real" is resolved by phone suffix (%1771) and never by id, because ids change when the test
-- account is recreated. Everything that hangs off a removed user goes with it.
--
-- Dry run by default:
--
--   docker exec -i fuelflow-postgres psql -U fuelflow -d fuelflow -v ON_ERROR_STOP=1 < 97-purge-all-test-data.sql
--   docker exec -i fuelflow-postgres psql -U fuelflow -d fuelflow -v ON_ERROR_STOP=1 -v apply=1 < 97-purge-all-test-data.sql
--
-- KEPT, deliberately:
--   roles, stations, fuel_types, qr_parameters   — reference data the app needs to boot
--   app_settings                                  — configuration (the renewal and term ladders)
--   fuel_packages                                 — the live supplier catalog
--   the real customer's vouchers, orders, fulfilments, refunds and their company
--
-- NOT TOUCHED, deliberately:
--   Hangfire storage (job, jobparameter, state, server, set, hash, counter, aggregatedcounter).
--   Those tables are full — roughly 38k rows of dead one-shot jobs — but they are the scheduler's own
--   bookkeeping, and hand-deleting rows from them can leave a job claimed but never executed. Use
--   Hangfire's own pruning, or stop the worker first, if they ever need trimming.
--
-- Delete order is forced by ON DELETE RESTRICT from four tables onto fuel_vouchers
-- (fulfillments.voucher_id, voucher_renewal_items.source_voucher_id, voucher_exchanges.old_voucher_id,
-- operator_voucher_renewals.voucher_id) plus fuel_vouchers.order_id onto orders. Unhook, then delete
-- vouchers, then orders. Never UPDATE fuel_vouchers SET order_id = NULL to "unlink": CHECK
-- ck_voucher_held_has_order is evaluated per row on UPDATE and aborts the statement.
-- =============================================================================================

\set ON_ERROR_STOP on
\if :{?apply}
\else
  \set apply 0
\endif

\echo ''
\echo '################ DRY RUN — nothing is being changed ################'

BEGIN;

-- The one account that is not test data.
CREATE TEMP TABLE _real_user AS
SELECT id, phone_number FROM users WHERE phone_number LIKE '%1771';

DO $$
BEGIN
  IF (SELECT count(*) FROM _real_user) <> 1 THEN
    RAISE EXCEPTION 'expected exactly one real customer on phone ending 1771, found %',
      (SELECT count(*) FROM _real_user);
  END IF;
END $$;

-- Everything else is a test account.
CREATE TEMP TABLE _test_user AS
SELECT id, phone_number FROM users WHERE id NOT IN (SELECT id FROM _real_user);

-- Companies are test unless the real customer owns or works for them.
CREATE TEMP TABLE _test_company AS
SELECT id, name FROM legal_entities
WHERE user_id NOT IN (SELECT id FROM _real_user)
   AND id NOT IN (
       SELECT le.id FROM legal_entities le
       JOIN company_members m ON m.legal_entity_id = le.id
       WHERE m.worker_user_id IN (SELECT id FROM _real_user)
   );

CREATE TEMP TABLE _test_order AS
SELECT id FROM orders
WHERE user_id NOT IN (SELECT id FROM _real_user)
  -- NULL-safe on purpose. `legal_entity_id NOT IN (...)` is NULL, not TRUE, for a personal order, so
  -- the plain form silently spares every personal order — which is exactly the set a campaign creates.
  AND (legal_entity_id IS NULL OR legal_entity_id NOT IN (SELECT id FROM _test_company));

CREATE TEMP TABLE _test_voucher AS
SELECT id FROM fuel_vouchers
WHERE assigned_to_user_id IS DISTINCT FROM (SELECT id FROM _real_user);

\echo ''
\echo '=== accounts ==='
SELECT 'KEEP  ' || phone_number || '  (' || role_id || ')' FROM users WHERE id IN (SELECT id FROM _real_user)
UNION ALL
SELECT 'drop  ' || phone_number FROM users WHERE id IN (SELECT id FROM _test_user)
ORDER BY 1;

\echo ''
\echo '=== companies ==='
SELECT CASE WHEN id IN (SELECT id FROM _test_company) THEN 'drop  ' ELSE 'KEEP  ' END || name
FROM legal_entities ORDER BY 1;

\echo ''
\echo '=== row counts: what would go ==='
SELECT 'users' AS what, count(*) AS n FROM users WHERE id IN (SELECT id FROM _test_user)
UNION ALL SELECT 'companies', count(*) FROM legal_entities WHERE id IN (SELECT id FROM _test_company)
UNION ALL SELECT 'company members', count(*) FROM company_members WHERE worker_user_id IN (SELECT id FROM _test_user)
UNION ALL SELECT 'company invitations', count(*) FROM company_invitations
    WHERE legal_entity_id IN (SELECT id FROM _test_company) OR owner_user_id IN (SELECT id FROM _test_user)
UNION ALL SELECT 'vouchers', count(*) FROM fuel_vouchers WHERE id IN (SELECT id FROM _test_voucher)
UNION ALL SELECT 'orders', count(*) FROM orders WHERE id IN (SELECT id FROM _test_order)
UNION ALL SELECT 'fulfilments', count(*) FROM fulfillments WHERE order_id IN (SELECT id FROM _test_order)
UNION ALL SELECT 'order line items', count(*) FROM order_line_items WHERE order_id IN (SELECT id FROM _test_order)
UNION ALL SELECT 'refunds', count(*) FROM refunds WHERE order_id IN (SELECT id FROM _test_order)
UNION ALL SELECT 'renewal items', count(*) FROM voucher_renewal_items WHERE order_id IN (SELECT id FROM _test_order)
UNION ALL SELECT 'supplier exchanges', count(*) FROM voucher_exchanges
    WHERE old_voucher_id IN (SELECT id FROM _test_voucher)
UNION ALL SELECT 'operator renewals', count(*) FROM operator_voucher_renewals WHERE voucher_id IN (SELECT id FROM _test_voucher)
UNION ALL SELECT 'emulator packages', count(*) FROM fuel_packages WHERE id LIKE 'emu2-pkg-%'
UNION ALL SELECT 'devices', count(*) FROM devices WHERE user_id IN (SELECT id FROM _test_user)
UNION ALL SELECT 'push tokens', count(*) FROM push_tokens WHERE user_id IN (SELECT id FROM _test_user)
UNION ALL SELECT 'refresh tokens', count(*) FROM refresh_tokens
UNION ALL SELECT 'verification codes', count(*) FROM verification_codes
UNION ALL SELECT 'notifications', count(*) FROM notifications
UNION ALL SELECT 'provider event outbox', count(*) FROM provider_event_outbox
UNION ALL SELECT 'error logs', count(*) FROM error_logs
UNION ALL SELECT 'voucher imports', count(*) FROM voucher_imports
UNION ALL SELECT 'purchase batches', count(*) FROM purchase_batches
UNION ALL SELECT 'outbox events', count(*) FROM outbox_events
UNION ALL SELECT 'contracts', count(*) FROM contracts
UNION ALL SELECT 'user contracts', count(*) FROM user_contracts
ORDER BY 1;

\echo ''
\echo '=== sessions and tokens belong to nobody real ==='
SELECT 'refresh tokens by user' AS what, u.phone_number, count(*) AS n
FROM refresh_tokens t JOIN users u ON u.id = t.user_id GROUP BY 2
UNION ALL
SELECT 'devices by user', u.phone_number, count(*)
FROM devices d JOIN users u ON u.id = d.user_id GROUP BY 2
ORDER BY 1, 2;

\if :apply
  \echo ''
  \echo '################ APPLYING ################'

  -- 1. Voucher references (all RESTRICT onto fuel_vouchers).
  DELETE FROM operator_voucher_renewals o WHERE o.voucher_id IN (SELECT id FROM _test_voucher);

  UPDATE voucher_exchanges x SET new_voucher_id = NULL
  WHERE x.new_voucher_id IN (SELECT id FROM _test_voucher);

  DELETE FROM voucher_exchanges x WHERE x.old_voucher_id IN (SELECT id FROM _test_voucher);

  DELETE FROM voucher_renewal_items i WHERE i.order_id IN (SELECT id FROM _test_order);

  -- 2. Fulfilments before vouchers (voucher_id is RESTRICT).
  DELETE FROM fulfillments f WHERE f.order_id IN (SELECT id FROM _test_order)
      OR f.voucher_id IN (SELECT id FROM _test_voucher);

  -- 3. Vouchers before orders (fuel_vouchers.order_id is RESTRICT, and the held-voucher check makes
  --    clearing it illegal).
  DELETE FROM fuel_vouchers WHERE id IN (SELECT id FROM _test_voucher);

  DELETE FROM orders WHERE id IN (SELECT id FROM _test_order);

  -- 4. Test companies. company_invitations and company_members cascade from legal_entities, but the
  --    invitation FKs to owner_user_id / worker_user_id are RESTRICT, so drop those rows first.
  DELETE FROM company_invitations
  WHERE legal_entity_id IN (SELECT id FROM _test_company)
     OR owner_user_id IN (SELECT id FROM _test_user)
     OR worker_user_id IN (SELECT id FROM _test_user);

  DELETE FROM company_members WHERE worker_user_id IN (SELECT id FROM _test_user);

  DELETE FROM legal_entities WHERE id IN (SELECT id FROM _test_company);

  -- 5. Everything keyed to a test account. devices / push_tokens / refresh_tokens / notifications /
  --    contracts cascade from users, but deleting them explicitly keeps the report honest about what
  --    went and makes the script safe to run when a cascade has been dropped.
  DELETE FROM devices WHERE user_id IN (SELECT id FROM _test_user);
  DELETE FROM push_tokens WHERE user_id IN (SELECT id FROM _test_user);
  DELETE FROM refresh_tokens WHERE user_id IN (SELECT id FROM _test_user);
  DELETE FROM user_contracts WHERE user_id IN (SELECT id FROM _test_user);
  DELETE FROM contracts WHERE user_id IN (SELECT id FROM _test_user);
  DELETE FROM notifications WHERE user_id IN (SELECT id FROM _test_user);

  -- 6. Session and telemetry leftovers that carry no user reference worth trusting.
  DELETE FROM refresh_tokens WHERE user_id IS NULL;
  DELETE FROM verification_codes;
  DELETE FROM notifications WHERE user_id IS NULL;
  DELETE FROM error_logs;
  DELETE FROM provider_event_outbox;

  -- 7. Supplier import history and its batches: every campaign re-imported the same PDFs.
  DELETE FROM voucher_imports;
  DELETE FROM purchase_batches;

  -- 8. Outbox: the payload carries ids inside jsonb, so no FK protects it and a stale row outlives the
  --    thing it describes. Two shapes, both needing a check:
  --      * orderId — keep only while that order still exists.
  --      * voucherIds / voucherId — keep only while every voucher it names still exists. A
  --        VoucherActivated event for a deleted voucher is pure garbage, and it is invisible: nothing
  --        in the app reads it again.
  --    First pass matched on 'orderId' only and so spared 6 events pointing at 8 long-deleted vouchers.
  DELETE FROM outbox_events e
  WHERE NOT EXISTS (SELECT 1 FROM orders o WHERE o.id::text = e.payload::text)
    AND NOT EXISTS (
        SELECT 1
        FROM regexp_matches(e.payload::text,
                            '[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}', 'g') AS m
        JOIN fuel_vouchers v ON v.id::text = m[1]
    );

  -- 9. Test accounts last: everything above that referenced them is already gone.
  DELETE FROM users WHERE id IN (SELECT id FROM _test_user);

  -- 10. Fixture catalog rows.
  DELETE FROM fuel_packages WHERE id LIKE 'emu2-pkg-%';

COMMIT;

  \echo ''
  \echo '=== after ==='
  SELECT 'users' AS what, count(*) AS n FROM users
  UNION ALL SELECT 'companies', count(*) FROM legal_entities
  UNION ALL SELECT 'company members', count(*) FROM company_members
  UNION ALL SELECT 'company invitations', count(*) FROM company_invitations
  UNION ALL SELECT 'vouchers', count(*) FROM fuel_vouchers
  UNION ALL SELECT 'orders', count(*) FROM orders
  UNION ALL SELECT 'order line items', count(*) FROM order_line_items
  UNION ALL SELECT 'fulfilments', count(*) FROM fulfillments
  UNION ALL SELECT 'refunds', count(*) FROM refunds
  UNION ALL SELECT 'renewal items', count(*) FROM voucher_renewal_items
  UNION ALL SELECT 'supplier exchanges', count(*) FROM voucher_exchanges
  UNION ALL SELECT 'devices', count(*) FROM devices
  UNION ALL SELECT 'push tokens', count(*) FROM push_tokens
  UNION ALL SELECT 'refresh tokens', count(*) FROM refresh_tokens
  UNION ALL SELECT 'verification codes', count(*) FROM verification_codes
  UNION ALL SELECT 'notifications', count(*) FROM notifications
  UNION ALL SELECT 'provider event outbox', count(*) FROM provider_event_outbox
  UNION ALL SELECT 'error logs', count(*) FROM error_logs
  UNION ALL SELECT 'voucher imports', count(*) FROM voucher_imports
  UNION ALL SELECT 'outbox events', count(*) FROM outbox_events
  UNION ALL SELECT 'fuel packages (catalog)', count(*) FROM fuel_packages
  UNION ALL SELECT 'app settings (config)', count(*) FROM app_settings
  UNION ALL SELECT 'roles', count(*) FROM roles
  UNION ALL SELECT 'stations', count(*) FROM stations
  UNION ALL SELECT 'fuel types', count(*) FROM fuel_types
  ORDER BY 1;

  \echo ''
  \echo '--- the real customer must be intact ---'
  SELECT v.voucher_number, v.status, v.customer_expiration_date, v.provider_expiration_date
  FROM fuel_vouchers v JOIN users u ON u.id = v.assigned_to_user_id
  WHERE u.phone_number LIKE '%1771' ORDER BY v.customer_expiration_date;
\else
  \echo ''
  \echo '################ DRY RUN ONLY — re-run with -v apply=1 ################'
\endif