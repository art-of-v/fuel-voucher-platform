-- Removes the QA pool: the `data_kind = 'qa'` mirror of the catalogue that
-- scripts/seed-qa-catalogue.sql and scripts/seed-qa-full-emulation.sql created, together with the
-- stock behind it and the emulation chain that holds it (orders, batches, imports, supplier).
--
-- Nothing here can reach a real customer, and nothing here can reach a real voucher: every DELETE is
-- keyed on the QA pool, either on `data_kind = 'qa'` or on the emulated chain's own markers
-- (`file_name LIKE 'QA-EMULATED-%'`, `name = 'QA Supplier (emulated)'`). The one place a QA order is
-- identified rather than its contents is spelled out below.
--
-- WHAT IT DELETES, and why each part has to go with the offers:
--   * `fuel_offers` / `fuel_offer_volumes` / `fuel_offer_prices` - the QA twins themselves. Both FKs
--     onto `fuel_offers` are RESTRICT, so the volumes and the price history have to go before the
--     offers; nothing else in the schema points at an offer.
--   * `fuel_vouchers` - the QA stock and the QA wallet vouchers. They hang off the offer only by
--     (brand, fuel_type_id, liters) and carry `data_kind` themselves, so they would survive as
--     orphans of a catalogue that no longer exists.
--   * the wallet half - orders the reviewer bought with, plus their lines, fulfillments and
--     payments. A held QA voucher cannot be deleted without its order, and an order whose only
--     vouchers are QA ones is test data for the same reason.
--   * `purchase_batches` / `voucher_imports` / `suppliers` - the emulated supplier -> import ->
--     batch chain. These are what make the QA vouchers legible in the operator screens; with the
--     catalogue gone they are leftovers that only say "somebody seeded something once".
--   * the audit and custody rows that restrict against the vouchers: `voucher_events`,
--     `voucher_movements` (three FKs - `voucher_id`, `from_voucher_id`, `to_voucher_id`),
--     `voucher_exchanges` and `operator_voucher_renewals`.
--
-- `voucher_renewal_items` is deliberately absent: `20261017073518_DropVoucherRenewalItems` removed the
-- table, and `20261017065731` moved its RESTRICT onto `order_line_items.order_id`, which is deleted
-- below. (scripts/reset-test-data.sql still names the dropped table - that script is stale, this one
-- is not.) The price history is guarded rather than assumed: `fuel_offer_prices` arrives with
-- 20261017142210 (AddFuelOfferPriceHistory), and a database that has not taken it yet must still run
-- this script rather than die on a missing relation.
--
-- An order is removed only when it has at least one QA voucher AND no voucher outside the QA pool. An
-- order that mixes the two is left alone and reported instead: deleting it would take a real voucher
-- with it, which is the one mistake this script must not make.
--
-- Delete order is forced by RESTRICT FKs, not stylistic. Everything below runs in one transaction,
-- so a failure anywhere leaves the database exactly as it was.
--
-- Usage (from the app server, where the postgres container is):
--
--   docker exec -i <pg-container> psql -U <user> -d <db> -v ON_ERROR_STOP=1 \
--     -f - < scripts/remove-qa-catalogue.sql
--
--   # on the current deployment:
--   docker exec -i fuelflow-postgres psql -U fuelflow -d fuelflow -v ON_ERROR_STOP=1 \
--     -f - < scripts/remove-qa-catalogue.sql
--
--   # print what would go, change nothing:
--   ... -v dry_run=true -f - < scripts/remove-qa-catalogue.sql
--
--   # one station only (same filter the seed scripts take):
--   ... -v station_id=okko -f - < scripts/remove-qa-catalogue.sql
--
-- Re-seeding afterwards is what puts the QA catalogue back:
-- scripts/seed-qa-full-emulation.sql rebuilds supplier -> import -> batch -> stock -> wallet, and
-- scripts/seed-qa-catalogue.sql is the lighter version (catalogue plus stock, no wallet).

\set ON_ERROR_STOP on

\if :{?dry_run}
\else
\set dry_run false
\endif

-- `station_id` is optional, for the same reason it is optional in the seeds: with none given the
-- whole QA pool goes, which is what a decommissioning wants.
\if :{?station_id}
\set qa_all_stations false
\else
\set qa_all_stations true
\set station_id ''
\endif

BEGIN;

-- ---------------------------------------------------------------------------------------------
-- What is in scope. Every temp table below is built once and reused by the DELETEs, so the report
-- and the deletion can never disagree about which rows were chosen.
-- ---------------------------------------------------------------------------------------------

-- The QA offers. `data_kind` is the pool marker and is the only thing trusted here: the id suffix
-- (`-qa`) is cosmetic, and an offer imported or repriced by an operator still carries the kind.
CREATE TEMP TABLE qa_offers ON COMMIT DROP AS
SELECT o.id, o.station_id, o.fuel_type_id
FROM fuel_offers o
WHERE o.data_kind = 'qa'
  AND (:'qa_all_stations'::boolean OR o.station_id = :'station_id');

-- Their volumes. The volume row has no `data_kind` - the pool lives on the offer above it - so it is
-- reached through the offer, which is also what makes the RESTRICT on `offer_id` decidable here
-- rather than at the end.
CREATE TEMP TABLE qa_volumes ON COMMIT DROP AS
SELECT v.id, v.offer_id, v.liters
FROM fuel_offer_volumes v
JOIN qa_offers o ON o.id = v.offer_id;

-- The emulated chain. Keyed on the seeds' own markers, so an operator who renamed a file or a
-- supplier by hand leaves a row behind rather than having it swept up.
CREATE TEMP TABLE qa_imports ON COMMIT DROP AS
SELECT i.id, i.file_name
FROM voucher_imports i
WHERE i.file_name LIKE 'QA-EMULATED-%'
  AND (:'qa_all_stations'::boolean
       OR i.file_name = 'QA-EMULATED-' || :'station_id' || '.pdf');

CREATE TEMP TABLE qa_batches ON COMMIT DROP AS
SELECT b.id
FROM purchase_batches b
JOIN qa_imports i ON i.id = b.import_job_id;

-- The QA vouchers. `brand` is the station id, lower-cased - it is the key
-- `PackageStockAvailability` and the fulfilment claim match on, so it is the only column that can
-- attribute a voucher to a station. The fuel type is the second handle: a voucher whose `brand` was
-- filled with something else (a hand-edited fixture, a supplier name) is still found through the
-- offer's fuel type, which is the other half of the availability key.
CREATE TEMP TABLE qa_vouchers ON COMMIT DROP AS
SELECT v.id
FROM fuel_vouchers v
WHERE v.data_kind = 'qa'
  AND (
      :'qa_all_stations'::boolean
      OR lower(v.brand) = lower(:'station_id')
      OR v.fuel_type_id IN (SELECT fuel_type_id FROM qa_offers)
  );

-- The QA orders. An order is in scope when it touches the QA pool - it holds a QA voucher, or it has
-- a line renewing one (`order_line_items.source_voucher_id` is RESTRICT onto `fuel_vouchers`, so a
-- line pointing at a QA voucher blocks its deletion and the order has to go with it) - and when
-- every voucher it holds is QA. An order that mixes the two is recorded in `qa_mixed_orders` and
-- reported at the end rather than deleted.
CREATE TEMP TABLE qa_orders ON COMMIT DROP AS
SELECT o.id, o.user_id, o.status
FROM orders o
WHERE NOT EXISTS (
          SELECT 1 FROM fuel_vouchers v
          WHERE v.order_id = o.id AND v.id NOT IN (SELECT id FROM qa_vouchers))
  AND (
      EXISTS (
          SELECT 1 FROM fuel_vouchers v
          WHERE v.order_id = o.id AND v.id IN (SELECT id FROM qa_vouchers))
      OR EXISTS (
          SELECT 1 FROM order_line_items li
          WHERE li.order_id = o.id AND li.source_voucher_id IN (SELECT id FROM qa_vouchers))
  );

-- Taken before the DELETEs, because afterwards the QA rows that proved the mixture are gone and the
-- question could no longer be asked.
CREATE TEMP TABLE qa_mixed_orders ON COMMIT DROP AS
SELECT o.id, o.status
FROM orders o
WHERE EXISTS (
          SELECT 1 FROM fuel_vouchers v
          WHERE v.order_id = o.id AND v.id IN (SELECT id FROM qa_vouchers))
  AND EXISTS (
          SELECT 1 FROM fuel_vouchers v
          WHERE v.order_id = o.id AND v.id NOT IN (SELECT id FROM qa_vouchers));

-- ---------------------------------------------------------------------------------------------
-- Report. Before the DELETEs, so a wrong `-v station_id` is visible in the scrollback.
-- ---------------------------------------------------------------------------------------------

\echo '--- QA offers to remove ---'
SELECT o.station_id, o.fuel_type_id,
       (SELECT count(*) FROM qa_volumes v WHERE v.offer_id = o.id) AS volumes,
       (SELECT count(*) FROM fuel_vouchers v
        WHERE v.data_kind = 'qa' AND v.fuel_type_id = o.fuel_type_id
          AND (:'qa_all_stations'::boolean OR lower(v.brand) = lower(o.station_id))) AS qa_vouchers
FROM qa_offers o ORDER BY station_id, fuel_type_id;

\echo '--- QA volumes to remove ---'
SELECT count(*) AS volumes FROM qa_volumes;

\echo '--- QA vouchers to remove, by status ---'
SELECT v.status, count(*)
FROM fuel_vouchers v JOIN qa_vouchers q ON q.id = v.id
GROUP BY v.status ORDER BY v.status;

\echo '--- QA orders to remove ---'
SELECT o.status, count(*)
FROM orders o JOIN qa_orders q ON q.id = o.id
GROUP BY o.status ORDER BY o.status;

\echo '--- QA chain to remove ---'
SELECT 'voucher_imports' AS object, count(*) FROM qa_imports
UNION ALL SELECT 'purchase_batches', count(*) FROM qa_batches
UNION ALL SELECT 'supplier', count(*) FROM suppliers WHERE name = 'QA Supplier (emulated)';

\if :dry_run
\echo ''
\echo 'dry_run=true - nothing was deleted.'
ROLLBACK;
\quit
\endif

-- ---------------------------------------------------------------------------------------------
-- Delete. Children first, in FK order.
-- ---------------------------------------------------------------------------------------------

-- Custody and audit rows first: `voucher_events.voucher_id` is RESTRICT, and so is every one of the
-- three `voucher_movements` FKs. A movement is matched on both sides because a QA voucher can be
-- either the source or the destination of a movement whose other end is not in scope.
--
-- The two-sided form is gated on the columns existing. `from_voucher_id` / `to_voucher_id` arrived in
-- `20261017092930_VoucherMovementsLedger`, and a database whose backend predates that migration has
-- only `voucher_id` - naming the others there is an error, not a no-op, and it aborts the whole
-- transaction. `information_schema` is asked rather than assumed.
SELECT EXISTS (
    SELECT 1 FROM information_schema.columns
    WHERE table_name = 'voucher_movements' AND column_name = 'from_voucher_id'
) AS movements_have_two_sides \gset

DELETE FROM voucher_events
WHERE voucher_id IN (SELECT id FROM qa_vouchers);

\if :movements_have_two_sides
DELETE FROM voucher_movements
WHERE voucher_id IN (SELECT id FROM qa_vouchers)
   OR from_voucher_id IN (SELECT id FROM qa_vouchers)
   OR to_voucher_id IN (SELECT id FROM qa_vouchers);
\else
\echo 'voucher_movements has no from_voucher_id/to_voucher_id on this database - matching voucher_id only.'
DELETE FROM voucher_movements
WHERE voucher_id IN (SELECT id FROM qa_vouchers);
\endif

-- Exchanges before their batches: `voucher_exchanges.exchange_batch_id` is RESTRICT onto
-- `movement_batches`, so the rows go first and the batches follow. The batches are collected while
-- the exchanges still exist, and only ones left with no member are removed - a batch that still has
-- an exchange in it belongs to somebody else's chain, and an already-empty batch was not ours to
-- sweep up.
CREATE TEMP TABLE qa_movement_batches ON COMMIT DROP AS
SELECT DISTINCT exchange_batch_id AS id
FROM voucher_exchanges
WHERE old_voucher_id IN (SELECT id FROM qa_vouchers)
   OR new_voucher_id IN (SELECT id FROM qa_vouchers);

DELETE FROM voucher_exchanges
WHERE old_voucher_id IN (SELECT id FROM qa_vouchers)
   OR new_voucher_id IN (SELECT id FROM qa_vouchers);

DELETE FROM movement_batches b
WHERE b.id IN (SELECT id FROM qa_movement_batches)
  AND NOT EXISTS (SELECT 1 FROM voucher_exchanges e WHERE e.exchange_batch_id = b.id);

-- The operator renewal audit row restricts against the voucher on both `voucher_id` and
-- `replacement_voucher_id`, so it has to be gone before the vouchers it points at.
DELETE FROM operator_voucher_renewals
WHERE voucher_id IN (SELECT id FROM qa_vouchers)
   OR (replacement_voucher_id IS NOT NULL AND replacement_voucher_id IN (SELECT id FROM qa_vouchers));

DELETE FROM fulfillments
WHERE voucher_id IN (SELECT id FROM qa_vouchers)
   OR order_id IN (SELECT id FROM qa_orders);

-- `payments.order_id` is RESTRICT and a refund points at the capture it reverses, which restricts the
-- other way - so both kinds go, refunds first, in two statements. Deleting a QA payment row is part of
-- removing the session that produced it.
DELETE FROM payments WHERE order_id IN (SELECT id FROM qa_orders) AND kind = 'Refund';

DELETE FROM payments WHERE order_id IN (SELECT id FROM qa_orders);

-- `order_line_items.order_id` is RESTRICT (it inherited the renewal guard when
-- `voucher_renewal_items` went), and `order_events` cascades from the order, so the lines go first.
DELETE FROM order_line_items WHERE order_id IN (SELECT id FROM qa_orders);

-- `fuel_vouchers.order_id` is RESTRICT, which is why the orders wait for the vouchers below.
DELETE FROM fuel_vouchers WHERE id IN (SELECT id FROM qa_vouchers);

DELETE FROM orders WHERE id IN (SELECT id FROM qa_orders);

-- Batches, then the imports they hang off (`purchase_batches.import_job_id` is RESTRICT). A batch
-- that still has vouchers behind it is kept - it is only leftover here if a real import used it.
DELETE FROM purchase_batches
WHERE id IN (SELECT id FROM qa_batches)
  AND NOT EXISTS (SELECT 1 FROM fuel_vouchers v WHERE v.import_job_id = purchase_batches.id);

-- `voucher_import_errors` cascades from the import, and `fuel_vouchers.import_job_id` is SET NULL, so
-- the import has no remaining child at this point.
DELETE FROM voucher_imports WHERE id IN (SELECT id FROM qa_imports);

-- The emulated supplier, only once nothing points at it: `purchase_batches`, `fuel_vouchers`,
-- `movement_batches` and `voucher_exchanges` all restrict onto it. (`payments` has no `supplier_id`
-- - it carries `qr_parameters_id` and the payer - so it is not one of the blockers here.)
DELETE FROM suppliers
WHERE name = 'QA Supplier (emulated)'
  AND NOT EXISTS (SELECT 1 FROM purchase_batches b WHERE b.supplier_id = suppliers.id)
  AND NOT EXISTS (SELECT 1 FROM fuel_vouchers v WHERE v.supplier_id = suppliers.id)
  AND NOT EXISTS (SELECT 1 FROM movement_batches m WHERE m.supplier_id = suppliers.id)
  AND NOT EXISTS (SELECT 1 FROM voucher_exchanges e WHERE e.supplier_id = suppliers.id);

-- The catalogue itself. Volumes first, then prices, then offers: `FK_fuel_offer_volumes_fuel_offers_offer_id`
-- and `FK_fuel_offer_prices_fuel_offers_offer_id` are both RESTRICT. Deleted rows included - the twins
-- were never archived, and an archived QA offer is still a QA offer.
--
-- `fuel_offer_prices` since 20261017142210 (AddFuelOfferPriceHistory): a price row is a record of
-- something sold, so the offer it was sold under RESTRICTs - which is the point, and which means the
-- history has to go first when a whole pool is decommissioned.
DELETE FROM fuel_offer_volumes WHERE id IN (SELECT id FROM qa_volumes);

SELECT EXISTS (
    SELECT 1 FROM information_schema.tables
    WHERE table_schema = 'public' AND table_name = 'fuel_offer_prices'
) AS has_price_history \gset

\if :has_price_history
DELETE FROM fuel_offer_prices WHERE offer_id IN (SELECT id FROM qa_offers);
\endif

DELETE FROM fuel_offers WHERE id IN (SELECT id FROM qa_offers);

-- ---------------------------------------------------------------------------------------------
-- After. Each check is a hard question about what the run was supposed to achieve, so a leftover
-- shows up here rather than as an App Review with an empty catalogue.
-- ---------------------------------------------------------------------------------------------

\echo '--- after ---'
\if :has_price_history
SELECT 'qa_offer_prices_left' AS check, count(*) AS value FROM fuel_offer_prices WHERE data_kind = 'qa'
UNION ALL
SELECT 'qa_vouchers_left', count(*) FROM fuel_vouchers WHERE data_kind = 'qa'
UNION ALL
SELECT 'qa_imports_left', count(*) FROM voucher_imports WHERE file_name LIKE 'QA-EMULATED-%'
UNION ALL
SELECT 'qa_supplier_left', count(*) FROM suppliers WHERE name = 'QA Supplier (emulated)'
UNION ALL
SELECT 'real_offers_kept', count(*) FROM fuel_offers WHERE data_kind = 'real' AND NOT is_deleted;
\else
\echo 'fuel_offer_prices is not present on this database - skipped that check.'
SELECT 'qa_offers_left' AS check, count(*) AS value FROM fuel_offers WHERE data_kind = 'qa'
UNION ALL SELECT 'qa_vouchers_left', count(*) FROM fuel_vouchers WHERE data_kind = 'qa'
UNION ALL SELECT 'qa_imports_left', count(*) FROM voucher_imports WHERE file_name LIKE 'QA-EMULATED-%'
UNION ALL SELECT 'qa_supplier_left', count(*) FROM suppliers WHERE name = 'QA Supplier (emulated)'
UNION ALL SELECT 'real_offers_kept', count(*) FROM fuel_offers WHERE data_kind = 'real' AND NOT is_deleted;
\endif

\echo 'Orders that mix pools and were therefore left alone - inspect by hand, not an error:'
SELECT id, status FROM qa_mixed_orders ORDER BY id;

\echo 'Re-seed with scripts/seed-qa-full-emulation.sql to get the QA catalogue back.'

COMMIT;