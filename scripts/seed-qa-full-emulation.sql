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
--   voucher_imports    one import per brand, `Completed`
--   purchase_batches   one batch per (import, fuel type), carrying the supplier and the cost - the
--                      step that makes a batch activatable at all
--   fuel_nominals      the QA size list: one `data_kind = 'qa'` row per live nominal
--   fuel_vouchers      `vouchers_per_package` of `Available` stock per QA package: buyable from the
--                      shop, no order, which is what a stock pool is
--   orders + line items + fulfillments
--                      one fulfilled purchase per (brand, fuel type) holding a couple of vouchers,
--                      so the wallet has receipts and there are vouchers to renew
--
-- REWRITTEN 2026-10-19 against the fuel-centric schema. The previous version created a
-- `fuel_offers` twin and a `fuel_offer_volumes` twin under it; both tables were dropped
-- (`fuel_offer_volumes` in `20261018224126`, `fuel_offers` in `20261019095748`, the migration that
-- moved the price onto the fuel). It also read the network from `stations`, which
-- `20261018140426_StationsBecomeBrands` turned into `brands` - a station is a brand now, and a node
-- of a brand is a station. A brand carries a SLUG ('okko'), and the slug is what `fuel_vouchers.brand`
-- carries and what the stock gate matches on, so the slug is what this script speaks in.
--
-- The split between stock and wallet matters and is not cosmetic. A voucher cannot be both: a held
-- voucher is `Assigned` inside an order, and the fulfilment claim moves it out of the pool. Seeding
-- "an order per voucher" would consume all 320 into the wallet and leave the shop empty, which is
-- the opposite of what a review needs - the reviewer has to be able to buy, and separately to find
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
--   * `ck_voucher_customer_term_within_supplier_term` - the customer term may not outlast the
--     supplier's, which is why the stock half runs +180d/+180d and the wallet half +180d/+7d.
--   * `ck_fuel_vouchers_acquisition_source` - the column is NOT NULL and constrained to
--     ('direct', 'partner'), so every row here says which one it is.
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
--   -v brand=okko             one brand only
--   -v vouchers_per_package 25
--   -v wallet_per_fuel_type 5    vouchers held per fuel type in the wallet
--
-- Every row is `data_kind = 'qa'`, which is the only thing keeping it away from a paying customer:
-- both the storefront filter and the fulfilment claim resolve the caller's pool, and neither this
-- script nor the storefront creates a test-flagged row the other pool can see.

\set ON_ERROR_STOP on

\if :{?brand}
\set seed_all_brands false
\else
\set seed_all_brands true
\set brand ''
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

-- The QA size list. Every live nominal gets a `data_kind = 'qa'` twin, because the pool lives on
-- the nominal: the storefront resolves a package by joining `fuel_types` to `fuel_nominals` and
-- filtering the nominal by the caller's pool
-- (`PackageStockAvailability.FindPackageAsync`: `nominal.DataKind == pool`). Without the twin a QA
-- session sees no catalogue at all.
--
-- The key is (fuel_type_id, data_kind, litres), which is the natural key a re-run collides on.
INSERT INTO fuel_nominals
    (fuel_type_id, data_kind, liters, created_at_utc, updated_at_utc)
SELECT
    live.fuel_type_id,
    'qa',
    live.liters,
    now() - interval '2 days',
    now() - interval '2 days'
FROM fuel_nominals live
JOIN fuel_types fuel ON fuel.id = live.fuel_type_id
JOIN brands brand ON brand.id = fuel.brand_id
WHERE live.data_kind = 'real'
  AND NOT fuel.is_deleted
  AND (:'seed_all_brands'::boolean OR lower(brand.slug) = lower(:'brand'))
ON CONFLICT (fuel_type_id, data_kind, liters) DO NOTHING;

-- One import per brand, named so it cannot be mistaken for a supplier PDF.
INSERT INTO voucher_imports
    (id, file_name, page_count, started_at_utc, completed_at_utc, status,
     imported_count, duplicate_count, failed_count, verification_failed_count, verified_with_warnings_count)
SELECT
    gen_random_uuid(),
    'QA-EMULATED-' || brand.slug || '.pdf',
    1,
    now() - interval '2 days',
    now() - interval '2 days',
    'Completed',
    (SELECT count(*) FROM fuel_nominals n
      JOIN fuel_types f ON f.id = n.fuel_type_id
      WHERE f.brand_id = brand.id AND n.data_kind = 'real'),
    0, 0, 0, 0
FROM brands brand
WHERE EXISTS (SELECT 1 FROM fuel_nominals n
                JOIN fuel_types f ON f.id = n.fuel_type_id
                WHERE f.brand_id = brand.id AND n.data_kind = 'real')
  AND (:'seed_all_brands'::boolean OR lower(brand.slug) = lower(:'brand'))
  AND NOT EXISTS (SELECT 1 FROM voucher_imports i WHERE i.file_name = 'QA-EMULATED-' || brand.slug || '.pdf');

-- Everything below this point reasons about a "package" - a brand, a fuel, a size, a price. That
-- shape is no longer a row of its own: it is a fuel, a nominal, and a price derived from the fuel's
-- own knobs. Rather than rewrite every statement that way, the flat shape is reconstructed here once
-- and the rest of the script is unchanged - which is also what keeps this diff reviewable against
-- the version it replaces.
--
-- Scoped to the QA pool on purpose: the pool is what separates the mirror from the live catalogue,
-- and every statement below relies on that filter. Carrying the pool in the view means forgetting it
-- is no longer possible.
CREATE TEMP VIEW qa_pkg AS
SELECT
    nominal.fuel_type_id                            AS fuel_type_id,
    lower(brand.slug)                               AS brand,
    nominal.liters                                  AS liters,
    -- The effective price of the package: the fuel's rate x litres, capped by the ceiling. There is
    -- no per-nominal override left to prefer, and no stored total to read, so both numbers below are
    -- computed here the way FuelPricing.PackagePrice does.
    round(least(coalesce(fuel.supplier_price_per_liter, 0) + coalesce(fuel.margin_uah_per_liter, 0),
                   coalesce(fuel.pump_price_per_liter, 1000000000) - coalesce(fuel.min_discount_per_liter, 0))
              * nominal.liters, 2)                  AS price,
    fuel.supplier_price_per_liter,
    fuel.pump_price_per_liter,
    fuel.min_discount_per_liter,
    -- The customer price per litre, derived the way FuelPricing.FinalPerLiterOrNull does: null
    -- unless the fuel has both a cost and a margin, because treating a missing margin as zero would
    -- publish a free fuel. least over a value no real pump reaches stands in for a missing ceiling,
    -- which is what the formula's pump-null branch means.
    CASE WHEN fuel.supplier_price_per_liter IS NULL OR fuel.margin_uah_per_liter IS NULL THEN NULL
         ELSE least(fuel.supplier_price_per_liter + fuel.margin_uah_per_liter,
                    coalesce(fuel.pump_price_per_liter, 1000000000) - coalesce(fuel.min_discount_per_liter, 0))
    END                                             AS final_price_per_liter
FROM fuel_nominals nominal
JOIN fuel_types fuel ON fuel.id = nominal.fuel_type_id
JOIN brands brand ON brand.id = fuel.brand_id
WHERE nominal.data_kind = 'qa'
  AND NOT fuel.is_deleted
  AND (:'seed_all_brands'::boolean OR lower(brand.slug) = lower(:'brand'));

-- One batch per (import, fuel type) - and that is exactly the shape the unique index
-- `ix_purchase_batches_import_job_id_fuel_type_id` demands. It is keyed on the fuel type alone, not
-- the brand and not the nominal, so a catalogue that lists 10 L, 20 L and 50 L of the same fuel
-- shares one batch, and the query has to collapse to it rather than emit one row per package.
INSERT INTO purchase_batches
    (id, import_job_id, fuel_type_id, brand, supplier_id, created_at_utc, updated_at_utc)
SELECT DISTINCT ON (imp.id, pkg.fuel_type_id)
    gen_random_uuid(),
    imp.id,
    pkg.fuel_type_id,
    pkg.brand,
    sup.id,
    now() - interval '2 days',
    now() - interval '2 days'
FROM qa_pkg pkg
JOIN voucher_imports imp ON imp.file_name = 'QA-EMULATED-' || pkg.brand || '.pdf'
CROSS JOIN (SELECT id FROM suppliers WHERE name = 'QA Supplier (emulated)' LIMIT 1) sup
ORDER BY imp.id, pkg.fuel_type_id
ON CONFLICT (import_job_id, fuel_type_id) DO NOTHING;

-- Stock. `Available` carries no order - that is what the status means, and the held-voucher CHECK
-- would reject the alternative. Inserted as the shortfall against the target depth, so re-running
-- tops a drained pool back up instead of multiplying it.
INSERT INTO fuel_vouchers
(id, brand, fuel_type_id, liters, cost_per_liter, supplier_id, import_job_id,
 supplier_expiration_date, customer_expiration_date, voucher_number, qr_payload, qr_payload_sha256,
     status, data_kind, acquisition_source, is_deleted, created_at_utc, updated_at_utc)
SELECT
    gen_random_uuid(),
    pkg.brand,
    pkg.fuel_type_id,
    pkg.liters,
    pkg.supplier_price_per_liter,
    sup.id,
    imp.id,
    current_date + 180,
    current_date + 180,
    'QA-STK-' || substr(md5(pkg.fuel_type_id || ':' || pkg.liters::text), 1, 8) || '-' || lpad(n::text, 2, '0'),
    'QA-QR-' || substr(md5(pkg.fuel_type_id || ':' || pkg.liters::text), 1, 8) || '-' || lpad(n::text, 2, '0'),
    sha256(convert_to('QA-QR-' || substr(md5(pkg.fuel_type_id || ':' || pkg.liters::text), 1, 8) || '-' || lpad(n::text, 2, '0'), 'UTF8')),
    'Available', 'qa', 'direct', false, now() - interval '2 days', now() - interval '2 days'
FROM qa_pkg pkg
JOIN voucher_imports imp ON imp.file_name = 'QA-EMULATED-' || pkg.brand || '.pdf'
CROSS JOIN (SELECT id FROM suppliers WHERE name = 'QA Supplier (emulated)' LIMIT 1) sup
CROSS JOIN LATERAL (
    SELECT generate_series(
        1,
        GREATEST(0, :vouchers_per_package::int - (
            SELECT count(*) FROM fuel_vouchers v
            WHERE v.data_kind = 'qa'
              AND v.fuel_type_id = pkg.fuel_type_id
              AND v.liters = pkg.liters
              AND lower(v.brand) = lower(pkg.brand)
              AND v.status = 'Available'
        ))
    ) AS n
) AS shortfall
ON CONFLICT DO NOTHING;

-- The wallet half. One fulfilled purchase per QA package, holding `wallet_per_fuel_type` vouchers
-- with a customer term inside the renewal window - so the reviewer opens a wallet that is not empty,
-- sees receipts, and has something to renew.
--
-- Every id below is derived from the QA package's own key, so the whole chain is re-runnable on its
-- primary keys rather than on existence subqueries. That matters because these rows are written in
-- dependency order - order, then line, then vouchers, then fulfillments - and an existence guard on
-- the last step cannot see what the first two have not written yet.

INSERT INTO orders
    (id, user_id, price, status, monobank_status, kind, created_at_utc, updated_at_utc,
     fulfilled_at_utc, is_deleted)
SELECT
    md5('qa-order-' || pkg.fuel_type_id || ':' || pkg.liters::text)::uuid,
    u.id,
    pkg.final_price_per_liter * pkg.liters * :'wallet_per_fuel_type'::int,
    'Fulfilled', 'Success', 'Purchase',
    now() - interval '3 days', now() - interval '3 days', now() - interval '3 days', false
FROM qa_pkg pkg
CROSS JOIN qa_user u
WHERE pkg.final_price_per_liter IS NOT NULL
ON CONFLICT (id) DO NOTHING;

-- `quantity` equals the vouchers this order actually holds. FulfillmentService reconciles fulfillments
-- against line items every cycle and releases the excess back to `Available`, so a line that
-- overstates its count loses vouchers within about a minute - and reads as silent data loss.
INSERT INTO order_line_items
    (id, order_id, fuel_type_id, liters, quantity, unit_price, line_total, brand, term_code)
SELECT
    md5('qa-line-' || pkg.fuel_type_id || ':' || pkg.liters::text)::uuid,
    md5('qa-order-' || pkg.fuel_type_id || ':' || pkg.liters::text)::uuid,
    pkg.fuel_type_id,
    pkg.liters,
    :'wallet_per_fuel_type'::int,
    pkg.final_price_per_liter,
    pkg.final_price_per_liter * pkg.liters * :'wallet_per_fuel_type'::int,
    pkg.brand,
    NULL
FROM qa_pkg pkg
WHERE pkg.final_price_per_liter IS NOT NULL
ON CONFLICT (id) DO NOTHING;

INSERT INTO fuel_vouchers
(id, brand, fuel_type_id, liters, cost_per_liter, supplier_id, import_job_id,
 supplier_expiration_date, customer_expiration_date, voucher_number, qr_payload, qr_payload_sha256,
     status, data_kind, acquisition_source, is_deleted, order_id, assigned_to_user_id, issued_at_utc,
     created_at_utc, updated_at_utc)
SELECT
    md5('qa-held-' || pkg.fuel_type_id || ':' || pkg.liters::text || '-' || lpad(n::text, 2, '0'))::uuid,
    pkg.brand,
    pkg.fuel_type_id,
    pkg.liters,
    pkg.supplier_price_per_liter,
    sup.id,
    imp.id,
    current_date + 180,
    current_date + 7,
    'QA-HELD-' || substr(md5(pkg.fuel_type_id || ':' || pkg.liters::text), 1, 8) || '-' || lpad(n::text, 2, '0'),
    'QA-QRH-' || substr(md5(pkg.fuel_type_id || ':' || pkg.liters::text), 1, 8) || '-' || lpad(n::text, 2, '0'),
    sha256(convert_to('QA-QRH-' || substr(md5(pkg.fuel_type_id || ':' || pkg.liters::text), 1, 8) || '-' || lpad(n::text, 2, '0'), 'UTF8')),
    'Assigned', 'qa', 'direct', false,
    md5('qa-order-' || pkg.fuel_type_id || ':' || pkg.liters::text)::uuid,
    u.id,
    now() - interval '3 days', now() - interval '3 days', now() - interval '3 days'
FROM qa_pkg pkg
JOIN voucher_imports imp ON imp.file_name = 'QA-EMULATED-' || pkg.brand || '.pdf'
CROSS JOIN (SELECT id FROM suppliers WHERE name = 'QA Supplier (emulated)' LIMIT 1) sup
CROSS JOIN qa_user u
CROSS JOIN LATERAL (SELECT generate_series(1, :'wallet_per_fuel_type'::int) AS n) AS g
WHERE pkg.final_price_per_liter IS NOT NULL
ON CONFLICT (id) DO NOTHING;

-- Without this row the wallet never renders the voucher: GetUserPurchases reads voucher ids from
-- `fulfillments`, not from `fuel_vouchers.order_id`, and drops an order with no live voucher.
INSERT INTO fulfillments (order_id, voucher_id, fulfilled_at_utc)
SELECT v.order_id, v.id, now() - interval '3 days'
FROM fuel_vouchers v
WHERE v.id IN (
          SELECT md5('qa-held-' || p.fuel_type_id || ':' || p.liters::text || '-' || lpad(n::text, 2, '0'))::uuid
          FROM qa_pkg p
          CROSS JOIN LATERAL (SELECT generate_series(1, :'wallet_per_fuel_type'::int) AS n) g
      )
  AND NOT EXISTS (SELECT 1 FROM fulfillments f WHERE f.voucher_id = v.id);

\echo ''
\echo 'QA pool emulated. Verify:'
\echo '  select p.brand, p.fuel_type_id, p.liters, p.price,'
\echo '    (select count(*) from fuel_vouchers v where v.data_kind = ''qa'' and v.fuel_type_id = p.fuel_type_id'
\echo '       and v.liters = p.liters and lower(v.brand) = lower(p.brand) and v.status = ''Available'') as stock'
\echo '  from qa_pkg p order by 1,2,3;'
\echo '  select status, count(*) from fuel_vouchers where data_kind = ''qa'' group by 1;'
\echo '  select count(*) as wallet_orders from orders o join order_line_items li on li.order_id = o.id where li.id like ''qa-line-%'';'
\echo ''
\echo 'Anonymous readers must see no QA row:'
\echo '  curl -s https://api.palne.shop/api/packages | grep -c ''qa-'''   # expect 0

COMMIT;
