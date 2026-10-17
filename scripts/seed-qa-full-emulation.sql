-- Full QA emulation: supplier -> import -> batch -> stock, plus a wallet with real orders behind it.
--
-- The App Store reviewer signs in as the QA account, so this is not a fixture to be cleaned up after a
-- test - it is the storefront the review sees. That changes what "seeded" has to mean. A row of
-- `Available` vouchers is enough to make a package buyable and nothing else: the wallet renders from
-- orders, the History timeline renders from order lines and renewal snapshots, and the renewal entry
-- point only appears for a voucher the customer actually holds. So this builds the whole chain, the
-- way the operator flow builds it, and leaves the QA pool in both states a real one has.
--
-- What it creates:
--
--   suppliers          one QA supplier, the party the whole chain is attributed to
--   voucher_imports    one import per network, `Completed`
--   purchase_batches   one batch per (import, fuel type), carrying the supplier and the cost - the
--                      step that makes a batch activatable at all
--   fuel_vouchers      `vouchers_per_package` of `Available` stock per QA package: buyable from the
--                      shop, no order, which is what a stock pool is
--   orders + line items + fulfillments
--                      one fulfilled purchase per (network, fuel type) holding a couple of vouchers,
--                      so the wallet has receipts and there are vouchers to renew
--
-- The split between stock and wallet matters and is not cosmetic. A voucher cannot be both: a held
-- voucher is `Assigned` inside an order, and the fulfilment claim moves it out of the pool. Seeding
-- "an order per voucher" would consume all 320 into the wallet and leave the shop empty, which is the
-- opposite of what a review needs - the reviewer has to be able to buy, and separately to find
-- something already theirs to renew.
--
-- Terms, on both halves. Stock runs customer +180d, so it never drifts near expiry on its own. Wallet
-- vouchers run customer +7d against a supplier term of +180d, which is inside the 14-day renewal
-- window, so the near-expiry banner and the renewal entry point are both reachable - and it is the
-- Extend branch, because the customer term is well short of the supplier's.
--
-- Constraint worth knowing, because the DB enforces it and the nightly jobs depend on it:
--   * `ck_voucher_held_has_order` - an `Assigned`/`Used`/`Blocked` voucher must carry `order_id`.
--     `Available` must not, which is why the stock half leaves it null.
--   * `FulfillmentService` reconciles fulfillments against line items every cycle and releases any
--     voucher it counts as excess back to `Available`. A line whose `quantity` exceeds its
--     fulfillment count therefore loses vouchers within about a minute, which reads as silent data
--     loss. Every line here is written with the quantity it actually fulfills.
--   * the wallet nests vouchers under orders by reading `fulfillments`, not `fuel_vouchers.order_id`,
--     and drops an order with no live voucher - so a held voucher with no fulfillment row never
--     renders at all.
--
-- Idempotent throughout: re-running tops stock back up to `vouchers_per_package` and leaves everything
-- else alone.
--
-- Usage:
--   docker exec -i fuelflow-postgres psql -U fuelflow -d fuelflow -v ON_ERROR_STOP=1 \
--     -f - < scripts/seed-qa-full-emulation.sql
--
--   -v station_id=okko      one network only
--   -v vouchers_per_package 25
--   -v wallet_per_fuel_type 5    vouchers held per fuel type in the wallet
--
-- Every row is `data_kind = 'qa'`, which is the only thing keeping it away from a paying customer:
-- both the storefront filter and the fulfilment claim resolve the caller's pool, and neither this
-- script nor the storefront creates a test-flagged row the other pool can see.

\set ON_ERROR_STOP on

\if :{?station_id}
\set seed_all_stations false
\else
\set seed_all_stations true
\set station_id ''
\endif

\if :{?vouchers_per_package}
\else
\set vouchers_per_package 10
\endif

\if :{?wallet_per_fuel_type}
\else
\set wallet_per_fuel_type 3
\endif

\if :{?qa_user_phone}
\else
\set qa_user_phone '+380110010203'
\endif

BEGIN;

-- One supplier for the whole QA pool. Named so it is obvious in the admin screens and in any audit
-- row which chain a voucher came from - an unattributable test batch is the thing that makes a
-- production cleanup guesswork later.
INSERT INTO suppliers (id, name, is_active, notes, created_at_utc, updated_at_utc)
SELECT gen_random_uuid(), 'QA Supplier (emulated)', true,
       'Test-only. Created by scripts/seed-qa-full-emulation.sql. Safe to delete with its batches.',
       now(), now()
WHERE NOT EXISTS (SELECT 1 FROM suppliers WHERE name = 'QA Supplier (emulated)');

-- The QA account the reviewer signs in as. Held vouchers must belong to somebody, and the fulfilment
-- claim only serves test stock to a QA account, so this has to be that one. Checked here rather than
-- left to a foreign-key error further down, because the failure without it is a confusing one: the
-- whole chain inserts happily and the reviewer finds an empty wallet.
SELECT count(*) AS qa_user_count
FROM users
WHERE phone_number = :'qa_user_phone' AND NOT is_deleted \gset

\if :qa_user_count
\else
\echo 'No active user with the QA phone - the QA account must exist before seeding.'
\quit
\endif

CREATE TEMP TABLE qa_user AS
SELECT id FROM users WHERE phone_number = :'qa_user_phone' AND NOT is_deleted LIMIT 1;

-- One import per network, named so it cannot be mistaken for a supplier PDF.
INSERT INTO voucher_imports
    (id, file_name, page_count, started_at_utc, completed_at_utc, status,
     imported_count, duplicate_count, failed_count, verification_failed_count, verified_with_warnings_count)
SELECT
    gen_random_uuid(),
    'QA-EMULATED-' || s.id || '.pdf',
    1,
    now() - interval '2 days',
    now() - interval '2 days',
    'Completed',
    (SELECT count(*) FROM fuel_offers p
      WHERE p.station_id = s.id AND p.data_kind = 'real' AND NOT p.is_deleted),
    0, 0, 0, 0
FROM stations s
WHERE EXISTS (SELECT 1 FROM fuel_offers p WHERE p.station_id = s.id AND p.data_kind = 'real' AND NOT p.is_deleted)
  AND (:'seed_all_stations'::boolean OR s.id = :'station_id')
  AND NOT EXISTS (SELECT 1 FROM voucher_imports i WHERE i.file_name = 'QA-EMULATED-' || s.id || '.pdf');

-- The QA mirror of the live catalogue. Every QA volume belongs to the network's import, and every
-- network's stock is one batch per fuel type carrying the supplier and the cost - the step that makes
-- a batch activatable, and the reason a bare voucher insert leaves the operator screens unreadable.
--
-- REWRITTEN 2026-10-16 against `fuel_offers` + `fuel_offer_volumes`. This used to write
-- `fuel_packages`, which the split turned into two tables that nothing reads - so it had been
-- succeeding while seeding nothing. See scripts/seed-qa-catalogue.sql for the full account, including
-- why the `(QA)` name suffix cannot survive the split (the storefront takes the name from
-- `fuel_types`, which has no pool column) and why the pools stay apart without it.
INSERT INTO fuel_offers
    (id, station_id, fuel_type_id,
     supplier_price_per_liter, margin_uah_per_liter, margin_percent,
     pump_price_per_liter, min_discount_per_liter, price_updated_at,
     created_at_utc, updated_at_utc, is_deleted, deleted_at_utc, data_kind)
SELECT
    live.id || '-qa', live.station_id, live.fuel_type_id,
    live.supplier_price_per_liter, live.margin_uah_per_liter, live.margin_percent,
    live.pump_price_per_liter, live.min_discount_per_liter,
    live.price_updated_at,
    now() - interval '2 days', now() - interval '2 days', false, NULL, 'qa'
FROM fuel_offers live
WHERE live.data_kind = 'real'
  AND NOT live.is_deleted
  AND (:'seed_all_stations'::boolean OR live.station_id = :'station_id')
ON CONFLICT (station_id, fuel_type_id, data_kind) DO NOTHING;

INSERT INTO fuel_offer_volumes
    (id, offer_id, liters, price_override_uah,
     created_at_utc, updated_at_utc, is_deleted, deleted_at_utc)
SELECT
    'qa-' || live.id, live_offer.id || '-qa', live.liters,
    -- Whatever exception the live nominal carries, and nothing more: the QA offer mirrors the live
    -- offer's rate, so a null override reproduces the live price exactly.
    live.price_override_uah,
    now() - interval '2 days', now() - interval '2 days', false, NULL
FROM fuel_offer_volumes live
JOIN fuel_offers live_offer ON live_offer.id = live.offer_id
WHERE live_offer.data_kind = 'real'
  AND NOT live_offer.is_deleted
  AND NOT live.is_deleted
  AND (:'seed_all_stations'::boolean OR live_offer.station_id = :'station_id')
ON CONFLICT (offer_id, liters) DO NOTHING;

-- Everything below this point reasons about a "package" - a station, a fuel, a nominal, a price. That
-- shape no longer exists as a row; it is an offer plus one of its volumes. Rather than rewrite every
-- statement that way, the flat shape is reconstructed here once and the rest of the script is
-- unchanged - which is also what keeps this diff reviewable against the version it replaces.
--
-- Scoped to the QA pool on purpose: after the split, "a fuel_packages row whose id starts with
-- qa-pkg-" was the only thing separating the mirror from the live catalogue, and every statement below
-- relied on that filter. Carrying the pool in the view means forgetting it is no longer possible.
CREATE TEMP VIEW qa_pkg AS
SELECT
    volume.id                AS id,
    offer.station_id         AS station_id,
    offer.fuel_type_id       AS fuel_type_id,
    volume.liters            AS liters,
    -- The effective price of the nominal: the override when one is set, else the offer's rate
    -- x litres. The offer stopped storing a final price on 2026-10-17 and the volume stopped storing
    -- a total the same day, so both numbers below are computed here the way FuelPricing.PackagePrice does.
    coalesce(volume.price_override_uah,
             round(least(coalesce(offer.supplier_price_per_liter, 0) + coalesce(offer.margin_uah_per_liter, 0),
                            coalesce(offer.pump_price_per_liter, 1000000000) - coalesce(offer.min_discount_per_liter, 0))
                   * volume.liters, 2)) AS price,
    offer.supplier_price_per_liter,
    offer.pump_price_per_liter,
    offer.min_discount_per_liter,
    -- The customer price per litre, derived the way FuelPricing.FinalPerLiterOrNull does: the QA
    -- mirror carries the offer's knobs, and since 2026-10-17 the offer carries no stored final price to
    -- copy. least over a value no real pump reaches stands in for a missing ceiling, which is what the
    -- formula's pump-null branch means.
    CASE WHEN offer.supplier_price_per_liter IS NULL OR offer.margin_uah_per_liter IS NULL THEN NULL
         ELSE least(offer.supplier_price_per_liter + offer.margin_uah_per_liter,
                    coalesce(offer.pump_price_per_liter, 1000000000) - coalesce(offer.min_discount_per_liter, 0))
    END AS final_price_per_liter
FROM fuel_offer_volumes volume
JOIN fuel_offers offer ON offer.id = volume.offer_id
WHERE offer.data_kind = 'qa'
  AND NOT offer.is_deleted
  AND NOT volume.is_deleted
  AND (:'seed_all_stations'::boolean OR offer.station_id = :'station_id');

-- One batch per (import, fuel type) - and that is exactly the shape the unique index
-- `IX_purchase_batches_import_job_id_fuel_type_id` demands. It is keyed on the fuel type alone, not
-- the brand and not the nominal, so a catalogue that lists 10 L, 20 L and 50 L of the same fuel
-- shares one batch, and the query has to collapse to it rather than emit one row per package.
INSERT INTO purchase_batches
    (id, import_job_id, fuel_type_id, brand, supplier_id, created_at_utc, updated_at_utc)
SELECT DISTINCT ON (imp.id, pkg.fuel_type_id)
    gen_random_uuid(),
    imp.id,
    pkg.fuel_type_id,
    pkg.station_id,
    sup.id,
    now() - interval '2 days',
    now() - interval '2 days'
FROM qa_pkg pkg
JOIN voucher_imports imp ON imp.file_name = 'QA-EMULATED-' || pkg.station_id || '.pdf'
CROSS JOIN (SELECT id FROM suppliers WHERE name = 'QA Supplier (emulated)' LIMIT 1) sup
ORDER BY imp.id, pkg.fuel_type_id
ON CONFLICT (import_job_id, fuel_type_id) DO NOTHING;

-- Stock. `Available` carries no order - that is what the status means, and the held-voucher CHECK
-- would reject the alternative. Inserted as the shortfall against the target depth, so re-running
-- tops a drained pool back up instead of multiplying it.
INSERT INTO fuel_vouchers
(id, brand, fuel_type_id, liters, cost_per_liter, supplier_id, import_job_id,
 supplier_expiration_date, customer_expiration_date, voucher_number, qr_payload,
     status, data_kind, is_deleted, created_at_utc, updated_at_utc)
SELECT
    gen_random_uuid(),
    lower(pkg.station_id),
    pkg.fuel_type_id,
    pkg.liters,
    pkg.supplier_price_per_liter,
    sup.id,
    imp.id,
    current_date + 180,
    current_date + 180,
    'QA-STK-' || substr(md5(pkg.id), 1, 8) || '-' || lpad(n::text, 2, '0'),
    'QA-QR-' || substr(md5(pkg.id), 1, 8) || '-' || lpad(n::text, 2, '0'),
    'Available', 'qa', false, now() - interval '2 days', now() - interval '2 days'
FROM qa_pkg pkg
JOIN voucher_imports imp ON imp.file_name = 'QA-EMULATED-' || pkg.station_id || '.pdf'
CROSS JOIN (SELECT id FROM suppliers WHERE name = 'QA Supplier (emulated)' LIMIT 1) sup
CROSS JOIN LATERAL (
    SELECT generate_series(
        1,
        GREATEST(0, :vouchers_per_package::int - (
            SELECT count(*) FROM fuel_vouchers v
            WHERE v.data_kind = 'qa'
              AND v.fuel_type_id = pkg.fuel_type_id
              AND v.liters = pkg.liters
              AND lower(v.brand) = lower(pkg.station_id)
              AND v.status = 'Available'
        ))
    ) AS n
) AS shortfall
ON CONFLICT DO NOTHING;

-- The wallet half. One fulfilled purchase per QA package, holding `wallet_per_fuel_type` vouchers
-- with a customer term inside the renewal window - so the reviewer opens a wallet that is not empty,
-- sees receipts, and has something to renew.
--
-- Every id below is derived from the QA package's own id, so the whole chain is re-runnable on its
-- primary keys rather than on existence subqueries. That matters because these rows are written in
-- dependency order - order, then line, then vouchers, then fulfillments - and an existence guard on
-- the last step cannot see what the first two have not written yet.

INSERT INTO orders
    (id, user_id, price, status, monobank_status, kind, created_at_utc, updated_at_utc,
     fulfilled_at_utc, is_deleted)
SELECT
    md5('qa-order-' || pkg.id)::uuid,
    u.id,
    pkg.final_price_per_liter * pkg.liters * :'wallet_per_fuel_type'::int,
    'Fulfilled', 'Success', 'Purchase',
    now() - interval '3 days', now() - interval '3 days', now() - interval '3 days', false
FROM qa_pkg pkg
CROSS JOIN qa_user u
ON CONFLICT (id) DO NOTHING;

-- `quantity` equals the vouchers this order actually holds. FulfillmentService reconciles fulfillments
-- against line items every cycle and releases the excess back to `Available`, so a line that
-- overstates its count loses vouchers within about a minute - and reads as silent data loss.
INSERT INTO order_line_items
    (id, order_id, fuel_type_id, liters, quantity, unit_price, line_total, brand, term_code)
SELECT
    md5('qa-line-' || pkg.id)::uuid,
    md5('qa-order-' || pkg.id)::uuid,
    pkg.fuel_type_id,
    pkg.liters,
    :'wallet_per_fuel_type'::int,
    pkg.final_price_per_liter,
    pkg.final_price_per_liter * pkg.liters * :'wallet_per_fuel_type'::int,
    pkg.station_id,
    NULL
FROM qa_pkg pkg
ON CONFLICT (id) DO NOTHING;

INSERT INTO fuel_vouchers
(id, brand, fuel_type_id, liters, cost_per_liter, supplier_id, import_job_id,
 supplier_expiration_date, customer_expiration_date, voucher_number, qr_payload,
     status, data_kind, is_deleted, order_id, assigned_to_user_id, issued_at_utc,
     created_at_utc, updated_at_utc)
SELECT
    md5('qa-held-' || pkg.id || '-' || lpad(n::text, 2, '0'))::uuid,
    lower(pkg.station_id),
    pkg.fuel_type_id,
    pkg.liters,
    pkg.supplier_price_per_liter,
    sup.id,
    imp.id,
    current_date + 180,
    current_date + 7,
    'QA-HELD-' || substr(md5(pkg.id), 1, 8) || '-' || lpad(n::text, 2, '0'),
    'QA-QRH-' || substr(md5(pkg.id), 1, 8) || '-' || lpad(n::text, 2, '0'),
    'Assigned', 'qa', false,
    md5('qa-order-' || pkg.id)::uuid,
    u.id,
    now() - interval '3 days', now() - interval '3 days', now() - interval '3 days'
FROM qa_pkg pkg
JOIN voucher_imports imp ON imp.file_name = 'QA-EMULATED-' || pkg.station_id || '.pdf'
CROSS JOIN (SELECT id FROM suppliers WHERE name = 'QA Supplier (emulated)' LIMIT 1) sup
CROSS JOIN qa_user u
CROSS JOIN LATERAL (SELECT generate_series(1, :'wallet_per_fuel_type'::int) AS n) AS g
ON CONFLICT (id) DO NOTHING;

-- Without this row the wallet never renders the voucher: GetUserPurchases reads voucher ids from
-- `fulfillments`, not from `fuel_vouchers.order_id`, and drops an order with no live voucher.
INSERT INTO fulfillments (order_id, voucher_id, fulfilled_at_utc)
SELECT v.order_id, v.id, now() - interval '3 days'
FROM fuel_vouchers v
WHERE v.id IN (
          SELECT md5('qa-held-' || p.id || '-' || lpad(n::text, 2, '0'))::uuid
          FROM qa_pkg p
          CROSS JOIN LATERAL (SELECT generate_series(1, :'wallet_per_fuel_type'::int) AS n) g
      )
  AND NOT EXISTS (SELECT 1 FROM fulfillments f WHERE f.voucher_id = v.id);

\echo ''
\echo 'QA pool emulated. Verify:'
\echo '  select p.station_id, p.fuel_type_id, p.liters, p.price,'
\echo '    (select count(*) from fuel_vouchers v where v.data_kind = ''qa'' and v.fuel_type_id = p.fuel_type_id'
\echo '       and v.liters = p.liters and lower(v.brand) = lower(p.station_id) and v.status = ''Available'') as stock'
\echo '  from qa_pkg p order by 1,2,3;'
\echo '  select status, count(*) from fuel_vouchers where data_kind = ''qa'' group by 1;'
\echo '  select count(*) as wallet_orders from orders o join order_line_items li on li.order_id = o.id where li.id like ''qa-line-%'';'
\echo ''
\echo 'Anonymous readers must see no QA row:'
\echo '  curl -s https://api.palne.shop/api/packages | grep -c ''qa-'''   # expect 0

COMMIT;
