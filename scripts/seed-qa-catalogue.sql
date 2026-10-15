-- Seeds a QA-only mirror of the live fuel catalogue, so the App Store reviewer (and any QA session)
-- can browse and buy fuel under names that cannot be confused with the real thing.
--
-- What this creates, for every live package at every station:
--   * a `fuel_packages` row whose `fuel_name` is the live name plus a `(QA)` suffix, carrying
--     `data_kind = 'qa'`, reusing the live `station_id` and `fuel_type_id`;
--   * `vouchers_per_package` sellable `fuel_vouchers` rows behind it, also `data_kind = 'qa'`.
--     The column was `is_test_data = true` until EnforceDataKind; the value set is pinned by a
--     CHECK constraint, so the literals below cannot drift into anything the database accepts.
--
-- Why the names are mirrored here rather than listed: they stay 1:1 with whatever the catalogue
-- actually sells today. Add a brand in admin and re-run this, and its QA twin appears too. A
-- hand-maintained list would drift the first time someone renamed a brand, and the drift would be
-- invisible - a QA package nobody notices is missing.
--
-- Why no new `fuel_types` or `stations` rows: neither table has a pool column and neither endpoint
-- filters on one, so a `QA`-named brand or station would reach every anonymous visitor of the public
-- catalogue. The pool lives on `fuel_packages`, and the anonymous storefront routes now resolve
-- the caller's pool before they filter. That is the whole isolation story, and it is why this script
-- creates two kinds of row and not a third.
--
-- The live nominal is mirrored as-is. Availability keys on (station, fuel type, nominal) and carries
-- no package id, so a QA package at the same nominal is backed by the same key - meaning a QA session
-- sees the live package lit as well as its `(QA)` twin. That is cosmetic, not a leak: the fulfilment
-- claim is gated on `data_kind` (QaTestPoolSegregationIntegrationTests), so a QA order still only
-- ever consumes test stock and a real order only ever real stock. Give the QA rows a different
-- nominal if that confusion is worse than a per-row constant to maintain.
--
-- Idempotent: re-running tops the pool up to `vouchers_per_package` rather than failing, and never
-- exceeds it. A reviewer who burns through one package and re-runs gets the count back, not a double.
--
-- Usage:
--   docker exec -i fuelflow-postgres psql -U fuelflow -d fuelflow -v ON_ERROR_STOP=1 \
--     -f - < scripts/seed-qa-catalogue.sql
--
--   # one station only
--   ... -v station_id=okko -f - < scripts/seed-qa-catalogue.sql
--
--   # a different depth of stock
--   ... -v vouchers_per_package=25 -f - < scripts/seed-qa-catalogue.sql
--
-- `station_id` is optional; with none given every network in the catalogue is mirrored, which is
-- what an App Review run wants - the reviewer should be able to open any brand and find it stocked.
-- The live packages themselves already need `supplier_id`, batch costs and activation before this
-- produces anything sellable - see scripts/seed-test-vouchers.sql and the operator side in
-- docs/testing/voucher-renewal-80.md.

\set ON_ERROR_STOP on

-- `station_id` is optional. Resolved here rather than in SQL because psql exposes "was this passed?"
-- only as a command, so the branch has to become a variable the query can read.
\if :{?station_id}
\set seed_all_stations false
\else
\set seed_all_stations true
\set station_id ''
\endif

BEGIN;

-- The suffix is what makes a QA package recognisable at a glance in the app and in a screenshot.
\if :{?qa_suffix}
\else
\set qa_suffix '(QA)'
\endif

-- Ten per package is deep enough that a reviewer cannot exhaust a brand by accident, and shallow
-- enough that the "one left" state - and the out-of-stock state, which renders the package disabled -
-- are both still reachable during a session.
\if :{?vouchers_per_package}
\else
\set vouchers_per_package 10
\endif

-- One QA package per live package. The id is derived from the live id, so re-running collides on the
-- primary key and does nothing rather than creating a second twin.
INSERT INTO fuel_packages
    (id, station_id, fuel_type_id, fuel_name, liters, price, original_price,
     supplier_price_per_liter, margin_uah_per_liter, final_price_per_liter,
     data_kind, is_deleted, created_at_utc, updated_at_utc)
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
    'qa',
    false,
    now(), now()
FROM fuel_packages live
WHERE live.data_kind = 'real'
  AND NOT live.is_deleted
  AND (
      :'seed_all_stations'::boolean
      OR live.station_id = :'station_id'
  )
ON CONFLICT (id) DO NOTHING;

-- Stock behind each QA package.
--
-- On the `brand` column, because the name still only half describes it. A supplier and a station are
-- different things here: `fuel_vouchers.supplier_id` is a uuid FK holding who actually supplied the
-- paper, and a supplier is deliberately not tied to one brand - it may hold OKKO, WOG and KLO
-- vouchers at once. That column is what cost and the audit trail join on, and it is left NULL here
-- because a test fixture is not a purchase.
--
-- `brand` is a varchar, and both the storefront key (StockKey) and the fulfilment claim match it
-- against `fuel_packages.station_id` - lower-cased, case-insensitive. So whatever is put here has to
-- be the station id for the package to find its own stock. A row keyed to a supplier name or a
-- network name would silently never match, leaving a visible package that cannot be bought, which
-- reads as an out-of-stock bug rather than as a mismatched key. It was `provider` until #1107
-- renamed it; the rename moved the column without moving what goes in it, which is the whole reason
-- the match key has to be written out here rather than assumed.
--
-- The terms are what make renewal reachable at all: a reviewer who buys a QA voucher sees the
-- near-expiry banner and the renewal entry point only while the customer term sits inside the
-- 14-day trigger window, and a full-term voucher shows neither. Hence customer +7d against a
-- supplier term of +180d - the same shape as the live stock.
--
-- `vouchers_per_package` of them, and only the shortfall: the series is bounded by what is already
-- behind the package, so re-running tops the pool back up and stops, instead of multiplying it.
INSERT INTO fuel_vouchers
(id, brand, fuel_type_id, liters, cost_per_liter,
 supplier_expiration_date, customer_expiration_date, voucher_number, qr_payload,
     status, data_kind, is_deleted, created_at_utc, updated_at_utc)
SELECT
    gen_random_uuid(),
    lower(pkg.station_id),
    pkg.fuel_type_id,
    pkg.liters,
    pkg.supplier_price_per_liter,
    current_date + 180,
    current_date + 7,
    'QA-CAT-' || substr(md5(pkg.id), 1, 8) || '-' || lpad(n::text, 2, '0'),
    'QA-QR-' || substr(md5(pkg.id), 1, 8) || '-' || lpad(n::text, 2, '0'),
    'Available',
    'qa',
    false,
    now(), now()
FROM fuel_packages pkg
CROSS JOIN LATERAL (
    SELECT generate_series(
        1,
        GREATEST(0, :vouchers_per_package::int - (
            SELECT count(*)
            FROM fuel_vouchers v
            WHERE v.data_kind = 'qa'
              AND v.fuel_type_id = pkg.fuel_type_id
              AND v.liters = pkg.liters
              AND lower(v.brand) = lower(pkg.station_id)
        ))
    ) AS n
) AS shortfall
WHERE pkg.id LIKE 'qa-pkg-%'
  AND NOT pkg.is_deleted
  AND (
      :'seed_all_stations'::boolean
      OR pkg.station_id = :'station_id'
  )
ON CONFLICT DO NOTHING;

-- Nothing above is inserted twice: a re-run finds the twin package on the primary key and finds
-- stock already behind it, so this converges on one voucher per QA package instead of growing.

\echo ''
\echo 'QA catalogue seeded. Verify, as the QA account:'
\echo '  select fuel_name, liters, data_kind from fuel_packages where id like ''qa-pkg-%'' order by fuel_name;'
\echo '  select brand, fuel_type_id, liters, customer_expiration_date, status'
\echo '    from fuel_vouchers where data_kind = ''qa'' order by customer_expiration_date;'
\echo ''
\echo 'Anonymous readers must see none of it - check from outside a signed-in session:'
\echo '  curl -s https://api.palne.shop/api/packages | grep -c ''(QA)'''   # expect 0

COMMIT;
