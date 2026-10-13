-- Seeds a QA-only mirror of the live fuel catalogue, so the App Store reviewer (and any QA session)
-- can browse and buy fuel under names that cannot be confused with the real thing.
--
-- What this creates, for every live package at one station:
--   * a `fuel_packages` row whose `fuel_name` is the live name plus a `(QA)` suffix, flagged
--     `is_test_data = true`, reusing the live `station_id` and `fuel_type_id`;
--   * one sellable `fuel_vouchers` row behind it, also `is_test_data = true`.
--
-- Why the names are mirrored here rather than listed: they stay 1:1 with whatever the catalogue
-- actually sells today. Add a brand in admin and re-run this, and its QA twin appears too. A
-- hand-maintained list would drift the first time someone renamed a brand, and the drift would be
-- invisible - a QA package nobody notices is missing.
--
-- Why no new `fuel_types` or `stations` rows: neither table has a test flag and neither endpoint
-- filters on one, so a `QA`-named brand or station would reach every anonymous visitor of the public
-- catalogue. The test flag lives on `fuel_packages`, and the anonymous storefront routes now resolve
-- the caller's pool before they filter. That is the whole isolation story, and it is why this script
-- creates two kinds of row and not a third.
--
-- The live nominal is mirrored as-is. Availability keys on (station, fuel type, nominal) and carries
-- no package id, so a QA package at the same nominal is backed by the same key - meaning a QA session
-- sees the live package lit as well as its `(QA)` twin. That is cosmetic, not a leak: the fulfilment
-- claim is gated on `is_test_data` (QaTestPoolSegregationIntegrationTests), so a QA order still only
-- ever consumes test stock and a real order only ever real stock. Give the QA rows a different
-- nominal if that confusion is worse than a per-row constant to maintain.
--
-- Idempotent: re-running tops the pool up rather than failing.
--
-- Usage:
--   docker exec -i fuelflow-postgres psql -U fuelflow -d fuelflow -v ON_ERROR_STOP=1 \
--     -v station_id=okko -v qa_liters=10 \
--     -f - < scripts/seed-qa-catalogue.sql
--
-- `station_id` is required and deliberately not defaulted: seeding the wrong station is exactly the
-- mistake this script exists to prevent. The live packages themselves already need `supplier_id`,
-- batch costs and activation before this produces anything sellable - see scripts/seed-test-vouchers.sql
-- and docs/testing/voucher-renewal-80.md for the operator side.

\set ON_ERROR_STOP on
\if :{?station_id}
\else
\echo 'station_id is required (see the header of this file)'
\quit
\endif

BEGIN;

-- The suffix is what makes a QA package recognisable at a glance in the app and in a screenshot.
\if :{?qa_suffix}
\else
\set qa_suffix '(QA)'
\endif

-- One QA package per live package at this station. The id is derived, so re-running collides on the
-- primary key and does nothing rather than creating a second twin.
INSERT INTO fuel_packages
    (id, station_id, fuel_type_id, fuel_name, liters, price, original_price,
     supplier_price_per_liter, margin_uah_per_liter, final_price_per_liter,
     is_test_data, is_deleted, created_at_utc, updated_at_utc)
SELECT
    'qa-pkg-' || live.id,
    live.station_id,
    live.fuel_type_id,
    live.fuel_name || :'qa_suffix',
    live.liters,
    live.price,
    live.original_price,
    live.supplier_price_per_liter,
    live.margin_uah_per_liter,
    live.final_price_per_liter,
    true,
    false,
    now(), now()
FROM fuel_packages live
WHERE live.station_id = :'station_id'
  AND NOT live.is_test_data
  AND NOT live.is_deleted
ON CONFLICT (id) DO NOTHING;

-- Stock behind each QA package.
--
-- On the `provider` column, because the name lies. A supplier and a station are different things
-- here: `fuel_vouchers.supplier_id` is a uuid FK holding who actually supplied the paper, and a
-- supplier is deliberately not tied to one brand - it may hold OKKO, WOG and KLO vouchers at once.
-- That column is what cost and the audit trail join on, and it is left NULL here because a test
-- fixture is not a purchase.
--
-- `provider` is the older varchar, and both the storefront key (StockKey) and the fulfilment claim
-- match it against `fuel_packages.station_id` - lower-cased, case-insensitive. So whatever is put
-- here has to be the station id for the package to find its own stock. A row keyed to a supplier
-- name or a network name would silently never match, leaving a visible package that cannot be
-- bought, which reads as an out-of-stock bug rather than as a mismatched key.
--
-- Renaming that column is its own piece of work: it is a match key on the money path, not a label.
--
-- The terms are what make renewal reachable at all: a reviewer who buys a QA voucher sees the
-- near-expiry banner and the renewal entry point only while the customer term sits inside the
-- 14-day trigger window, and a full-term voucher shows neither. Hence customer +7d against a
-- provider term of +180d - the same shape as the live stock.
INSERT INTO fuel_vouchers
    (id, provider, fuel_type_id, liters, cost_per_liter,
     provider_expiration_date, customer_expiration_date, voucher_number, qr_payload,
     status, is_test_data, is_deleted, created_at_utc, updated_at_utc)
SELECT
    gen_random_uuid(),
    lower(pkg.station_id),
    pkg.fuel_type_id,
    pkg.liters,
    pkg.supplier_price_per_liter,
    current_date + 180,
    current_date + 7,
    'QA-CAT-' || substr(md5(pkg.id), 1, 10),
    'QA-QR-' || substr(md5(pkg.id), 1, 10),
    'Available',
    true,
    false,
    now(), now()
FROM fuel_packages pkg
WHERE pkg.id LIKE 'qa-pkg-%'
  AND NOT EXISTS (
      SELECT 1 FROM fuel_vouchers v
      WHERE v.is_test_data
        AND v.fuel_type_id = pkg.fuel_type_id
        AND v.liters = pkg.liters
        AND lower(v.provider) = lower(pkg.station_id)
  )
ON CONFLICT DO NOTHING;

-- Nothing above is inserted twice: a re-run finds the twin package on the primary key and finds
-- stock already behind it, so this converges on one voucher per QA package instead of growing.

\echo ''
\echo 'QA catalogue seeded. Verify, as the QA account:'
\echo '  select fuel_name, liters, is_test_data from fuel_packages where id like ''qa-pkg-%'' order by fuel_name;'
\echo '  select provider, fuel_type_id, liters, customer_expiration_date, status'
\echo '    from fuel_vouchers where is_test_data order by customer_expiration_date;'
\echo ''
\echo 'Anonymous readers must see none of it - check from outside a signed-in session:'
\echo '  curl -s https://api.palne.shop/api/packages | grep -c ''(QA)'''   # expect 0

COMMIT;
