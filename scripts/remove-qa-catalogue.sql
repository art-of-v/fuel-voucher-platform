-- Removes the QA pool: the `data_kind = 'qa'` mirror of the catalogue that
-- scripts/seed-qa-catalogue.sql and scripts/seed-qa-full-emulation.sql created, together with the
-- stock behind it and the emulation chain that holds it (orders, batches, imports, supplier).
--
-- Nothing here can reach a real customer, and nothing here can reach a real voucher: every DELETE is
-- keyed on the QA pool, either on `data_kind = 'qa'` or on the emulated chain's own markers
-- (`file_name LIKE 'QA-EMULATED-%'`, `name = 'QA Supplier (emulated)'`). The one place a QA order is
-- identified rather than its contents is spelled out below.
--
-- REWRITTEN 2026-10-19 against the fuel-centric schema. The previous version deleted
-- `fuel_offers` / `fuel_offer_volumes` - the QA twins themselves - and both tables are gone
-- (`fuel_offer_volumes` in `20261018224126`, `fuel_offers` in `20261019095748`). The twin now IS a
-- `fuel_nominals` row with `data_kind = 'qa'`, and the price history that hung off the offer hangs
-- off the fuel instead.
--
-- WHAT IT DELETES, and why each part has to go with the catalogue:
--   * `fuel_nominals` (the `data_kind = 'qa'` rows) - the QA twins themselves. The pool lives on
--     the nominal, so a QA size with no twin is a size nobody should be offered.
--   * `fuel_price_history` - the QA price history. It arrives with 20261017142210
--     (AddFuelOfferPriceHistory) and is renamed on 2026-10-19 (20261019132728), the migration that
--     took the offer out of the name; it carries `data_kind` of its own, and a price row is a record
--     of something sold, so it has to go when the pool it belongs to is decommissioned.
--   * `fuel_vouchers` - the QA stock and the QA wallet vouchers. They hang off the catalogue only
--     by (brand, fuel_type_id, liters) and carry `data_kind` themselves, so they would survive as
--     orphans of a catalogue that no longer exists.
--   * the wallet half - orders the reviewer bought with, plus their lines, fulfillments and
--     payments. A held QA voucher cannot be deleted without its order, and an order whose only
--     vouchers are QA ones is test data for the same reason.
--   * `purchase_batches` / `voucher_imports` / `suppliers` - the emulated supplier -> import ->
--     batch chain. These are what make the QA vouchers legible in the operator screens; with the
--     catalogue gone they are leftovers that only say "somebody seeded something once".
--   * the audit and custody rows that restrict against the vouchers: `voucher_events`,
--     `voucher_movements` (three FKs - `voucher_id`, `from_voucher_id`, `to_voucher_id`),
--     `voucher_movements` and the renewal lines on `order_line_items`.
--
-- `voucher_renewal_items` is deliberately absent: `20261017073518_DropVoucherRenewalItems` removed the
-- table, and `20261017065731` moved its RESTRICT onto `order_line_items.order_id`, which is deleted
-- below. (scripts/reset-test-data.sql still names the dropped table - that script is stale, this one
-- is not.) The price history is guarded rather than assumed: `fuel_price_history` arrives with
-- 20261017142210 (AddFuelOfferPriceHistory) and is renamed on 2026-10-19 (20261019132728), and a
-- database that has not taken it yet must still run this script rather than die on a missing
-- relation.
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
--   # one brand only (same filter the seed scripts take):
--   ... -v brand=okko -f - < scripts/remove-qa-catalogue.sql
--
-- Re-seeding afterwards is what puts the QA catalogue back:
-- scripts/seed-qa-full-emulation.sql rebuilds supplier -> import -> batch -> stock -> wallet, and
-- scripts/seed-qa-catalogue.sql is the lighter version (catalogue plus stock, no wallet).

\set ON_ERROR_STOP on

\if :{?dry_run}
\else
\set dry_run false
\endif

-- `brand` is optional, for the same reason it is optional in the seeds: with none given the
-- whole QA pool goes, which is what a decommissioning wants. It is the brand's SLUG ('okko'), which
-- is what `fuel_vouchers.brand` carries; the parameter used to be called `station_id` and carried
-- the same value, because a station was a brand.
\if :{?brand}
\set qa_all_brands false
\else
\set qa_all_brands true
\set brand ''
\endif

BEGIN;

-- ---------------------------------------------------------------------------------------------
-- What is in scope. Every temp table below is built once and reused by the DELETEs, so the report
-- and the deletion can never disagree about which rows were chosen.
-- ---------------------------------------------------------------------------------------------

-- The QA catalogue: the `data_kind = 'qa'` nominals, with the brand they belong to. `data_kind` is
-- the pool marker and is the only thing trusted here.
CREATE TEMP TABLE qa_nominals ON COMMIT DROP AS
SELECT n.fuel_type_id, n.liters, lower(b.slug) AS brand
FROM fuel_nominals n
JOIN fuel_types f ON f.id = n.fuel_type_id
JOIN brands b ON b.id = f.brand_id
WHERE n.data_kind = 'qa'
  AND (:'qa_all_brands'::boolean OR lower(b.slug) = lower(:'brand'));

-- The emulated chain. Keyed on the seeds' own markers, so an operator who renamed a file or a
-- supplier by hand leaves a row behind rather than having it swept up.
CREATE TEMP TABLE qa_imports ON COMMIT DROP AS
SELECT i.id, i.file_name
FROM voucher_imports i
WHERE i.file_name LIKE 'QA-EMULATED-%'
  AND (:'qa_all_brands'::boolean
       OR i.file_name = 'QA-EMULATED-' || :'brand' || '.pdf');

CREATE TEMP TABLE qa_batches ON COMMIT DROP AS
SELECT b.id
FROM purchase_batches b
JOIN qa_imports i ON i.id = b.import_job_id;

-- The QA vouchers. `brand` is the brand's slug, lower-cased - it is the key
-- `PackageStockAvailability` and the fulfilment claim match on, so it is the only column that can
-- attribute a voucher to a brand. The fuel type is the second handle: a voucher whose `brand` was
-- filled with something else (a hand-edited fixture, a supplier name) is still found through the
-- nominal's fuel type, which is the other half of the availability key.
CREATE TEMP TABLE qa_vouchers ON COMMIT DROP AS
SELECT v.id
FROM fuel_vouchers v
WHERE v.data_kind = 'qa'
  AND (
      :'qa_all_brands'::boolean
      OR lower(v.brand) = lower(:'brand')
      OR v.fuel_type_id IN (SELECT fuel_type_id FROM qa_nominals)
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
-- Report. Before the DELETEs, so a wrong `-v brand` is visible in the scrollback.
-- ---------------------------------------------------------------------------------------------

\echo '--- QA catalogue to remove ---'
SELECT n.brand, n.fuel_type_id, n.liters,
       (SELECT count(*) FROM qa_vouchers q
          JOIN fuel_vouchers v ON v.id = q.id
         WHERE v.fuel_type_id = n.fuel_type_id
           AND v.liters = n.liters
           AND lower(v.brand) = n.brand) AS qa_vouchers
FROM qa_nominals n ORDER BY brand, fuel_type_id, liters;

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

-- The exchange pairs live in the ledger now (#319), on the row of the retired paper. The movements
-- go before the batches they were part of, and the batches are collected while the movements still
-- exist - only ones left with no member are removed, a batch that still has a movement in it
-- belongs to somebody else's chain, and an already-empty batch was not ours to sweep up.
CREATE TEMP TABLE qa_movement_batches ON COMMIT DROP AS
SELECT DISTINCT batch_id AS id
FROM voucher_movements
WHERE voucher_id IN (SELECT id FROM qa_vouchers)
   OR replaced_by_voucher_id IN (SELECT id FROM qa_vouchers);

DELETE FROM movement_batches b
WHERE b.id IN (SELECT id FROM qa_movement_batches)
  AND NOT EXISTS (SELECT 1 FROM voucher_movements m WHERE m.batch_id = b.id);

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
-- `movement_batches` and the ledger's `voucher_movements` (through the batch) all restrict onto it. (`payments` has no `supplier_id`
-- - it carries `qr_parameters_id` and the payer - so it is not one of the blockers here.)
DELETE FROM suppliers
WHERE name = 'QA Supplier (emulated)'
  AND NOT EXISTS (SELECT 1 FROM purchase_batches b WHERE b.supplier_id = suppliers.id)
  AND NOT EXISTS (SELECT 1 FROM fuel_vouchers v WHERE v.supplier_id = suppliers.id)
  AND NOT EXISTS (SELECT 1 FROM movement_batches m WHERE m.supplier_id = suppliers.id)
  AND NOT EXISTS (SELECT 1 FROM voucher_movements m WHERE m.batch_id IN (SELECT id FROM movement_batches mb WHERE mb.supplier_id = suppliers.id));

-- The catalogue itself: the QA size list, and the QA price history.
--
-- `fuel_offer_prices` since 20261017142210 (AddFuelOfferPriceHistory), renamed on 2026-10-19
-- (20261019132728) to `fuel_price_history`: a price row is a record of something sold, so the fuel it
-- was sold under RESTRICTs - which is the point, and which means the history has to go first when a
-- whole pool is decommissioned.
--
-- BOTH names are checked, because a precondition that names a table that does not exist is a no-op:
-- on a database that has not taken the rename yet, the QA price history is in `fuel_offer_prices`,
-- and asking only about the new name would leave it behind while the rest of the pool went. The
-- rename is a rename and nothing more, so the old table and the new one hold the same rows - which
-- means asking about both is safe, and asking about only one is not.
SELECT EXISTS (
    SELECT 1 FROM information_schema.tables
    WHERE table_schema = 'public' AND table_name = 'fuel_price_history'
) AS has_price_history,
EXISTS (
    SELECT 1 FROM information_schema.tables
    WHERE table_schema = 'public' AND table_name = 'fuel_offer_prices'
) AS has_legacy_price_history \gset

\if :has_price_history
DELETE FROM fuel_price_history WHERE data_kind = 'qa';
\endif

\if :has_legacy_price_history
DELETE FROM fuel_offer_prices WHERE data_kind = 'qa';
\endif

-- The QA nominals. There is no offer row to delete any more: the twin IS a nominal, and the pool
-- lives on it.
DELETE FROM fuel_nominals
WHERE data_kind = 'qa'
  AND fuel_type_id IN (SELECT fuel_type_id FROM qa_nominals)
  AND liters IN (SELECT liters FROM qa_nominals WHERE qa_nominals.fuel_type_id = fuel_nominals.fuel_type_id);

-- ---------------------------------------------------------------------------------------------
-- After. Each check is a hard question about what the run was supposed to achieve, so a leftover
-- shows up here rather than as an App Review with an empty catalogue.
-- ---------------------------------------------------------------------------------------------

\echo '--- after ---'
-- The price history is checked under whichever name this database carries - the two checks are
-- separate statements rather than two branches of one, because a table that is not here must not
-- cost the reader the other five numbers.
\if :has_price_history
SELECT 'qa_price_history_left' AS check, count(*) AS value FROM fuel_price_history WHERE data_kind = 'qa';
\endif
\if :has_legacy_price_history
SELECT 'qa_offer_prices_left' AS check, count(*) AS value FROM fuel_offer_prices WHERE data_kind = 'qa';
\endif
SELECT 'qa_nominals_left' AS check, count(*) FROM fuel_nominals WHERE data_kind = 'qa'
UNION ALL SELECT 'qa_vouchers_left', count(*) FROM fuel_vouchers WHERE data_kind = 'qa'
UNION ALL SELECT 'qa_imports_left', count(*) FROM voucher_imports WHERE file_name LIKE 'QA-EMULATED-%'
UNION ALL SELECT 'qa_supplier_left', count(*) FROM suppliers WHERE name = 'QA Supplier (emulated)'
UNION ALL SELECT 'real_nominals_kept', count(*) FROM fuel_nominals WHERE data_kind = 'real';

\echo 'Orders that mix pools and were therefore left alone - inspect by hand, not an error:'
SELECT id, status FROM qa_mixed_orders ORDER BY id;

\echo 'Re-seed with scripts/seed-qa-full-emulation.sql to get the QA catalogue back.'

COMMIT;
