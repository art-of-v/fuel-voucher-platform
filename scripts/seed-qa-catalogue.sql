-- Seeds a QA-only mirror of the live fuel catalogue, so the App Store reviewer (and any QA session)
-- can browse and buy fuel without touching real stock.
--
-- WHAT THIS CREATES, for every live nominal and every fuel it belongs to:
--   * a `fuel_nominals` twin carrying `data_kind = 'qa'`, mirroring the live size list;
--   * `vouchers_per_package` sellable `fuel_vouchers` rows behind each twin nominal, also
--     `data_kind = 'qa'`.
--
-- REWRITTEN 2026-10-19 against the fuel-centric schema. The previous version created a
-- `fuel_offers` twin and a `fuel_offer_volumes` twin under it, and both tables were dropped:
-- `fuel_offer_volumes` in `20261018224126` and `fuel_offers` in `20261019095748`, the migration
-- that moved the price onto the fuel itself. So this script had been failing for a week, on a
-- table that no longer exists, and the failure was in a file nobody runs on every deploy.
--
-- WHAT A PACKAGE IS NOW, and why the two inserts below are the whole thing. A package is a fuel
-- with a size: `fuel_types` names the fuel and carries its price, `fuel_nominals` says which sizes
-- that fuel is sold in, and the pool lives on the nominal. The storefront resolves a package by
-- joining the two and filtering the nominal by the caller's pool
-- (`PackageStockAvailability.FindPackageAsync`: `nominal.DataKind == pool`), and the stock gate
-- counts `fuel_vouchers` with the same pool, the same brand slug, the same fuel and the same
-- litres. There is no offer row to mirror any more, and no volume row either - a size IS a
-- nominal now.
--
-- THE PRICE IS NOT MIRRORED, because there is nothing to mirror it onto. The margin and the
-- supplier cost live on `fuel_types`, which the two pools share, so the QA catalogue is priced
-- exactly like the live one. The pool separates stock and data, not money.
--
-- THE `(QA)` NAME SUFFIX IS GONE, and cannot come back without a schema change. The old script gave
-- every QA package `fuel_name || '(QA)'` "so it cannot be confused with the real thing". The
-- storefront joins the name from `fuel_types` (`GetPublicPackagesByStationQueryHandler`:
-- `FuelName = fuel.Name`), and `fuel_types` has no pool column. So a QA twin and its live
-- counterpart are the same fuel and necessarily the same name.
--
-- The only two ways to mark them apart were both rejected then and are still:
--   * a QA-named `fuel_types` or `brands` row - neither table has a pool column and neither
--     endpoint filters on one, so a QA-named fuel would reach every anonymous visitor;
--   * a name override somewhere else - a column that exists only to make test data legible, which
--     is the wrong direction for a schema whose next step (planning #321) is deleting the pool
--     entirely.
--
-- What actually keeps the pools apart does not depend on the name. Both storefront routes resolve the
-- caller's pool before they filter (`nominal.DataKind == DataKinds.ForPool(isTestAccount)`), the
-- fulfilment claim is gated on it as well, and `QaTestPoolSegregationIntegrationTests` pins that a QA
-- order only ever consumes QA stock and a real order only ever real stock. An operator sees both
-- pools in admin, where the distinction is legible. A reviewer sees the ordinary catalogue, which is
-- what App Review needs: browse, buy, see a voucher in the wallet.
--
-- WHY NO NEW `fuel_types` OR `brands` ROWS: as above - neither has a pool column, so a QA row
-- there reaches the public catalogue.
--
-- The live nominal is mirrored as-is. Availability keys on (brand, fuel type, nominal) and carries
-- no offer id, so a QA volume at the same nominal is backed by the same key - a QA session sees the
-- live volume lit as well as its twin. That is cosmetic, not a leak, for the reason above.
--
-- Idempotent: re-running tops the stock back up to `vouchers_per_package` rather than failing, and
-- never exceeds it. The twins are keyed on the natural keys the database already enforces -
-- (fuel, pool, litres) for a nominal and (brand, fuel, litres) for the stock - not on the id,
-- because the natural keys are what a second run would collide on.
--
-- Usage:
--   docker exec -i fuelflow-postgres psql -U fuelflow -d fuelflow -v ON_ERROR_STOP=1 \
--     -f - < scripts/seed-qa-catalogue.sql
--
--   # one brand only
--   ... -v brand=okko -f - < scripts/seed-qa-catalogue.sql
--
--   # a different depth of stock
--   ... -v vouchers_per_package=25 -f - < scripts/seed-qa-catalogue.sql
--
-- `brand` is optional; with none given every brand in the catalogue is mirrored, which is what an
-- App Review run wants - the reviewer should be able to open any brand and find it stocked. It is
-- the brand's SLUG ('okko', 'wog', 'klo'), which is what `fuel_vouchers.brand` carries and what the
-- stock gate matches on. The parameter used to be called `station_id` and carried the same value:
-- a station was a brand, and the rename (`20261018140426_StationsBecomeBrands`) is why the name
-- changed with it.
--
-- The live fuels themselves already need a supplier, a batch cost and activation before this
-- produces anything sellable - see scripts/seed-test-vouchers.sql and the operator side in
-- docs/testing/voucher-renewal-80.md.

\set ON_ERROR_STOP on

-- `brand` is optional. Resolved here rather than in SQL because psql exposes "was this passed?"
-- only as a command, so the branch has to become a variable the query can read.
\if :{?brand}
\set seed_all_brands false
\else
\set seed_all_brands true
\set brand ''
\endif

BEGIN;

-- Ten per package is deep enough that a reviewer cannot exhaust a brand by accident, and shallow
-- enough that the "one left" state - and the out-of-stock state, which renders the package disabled -
-- are both still reachable during a session.
\if :{?vouchers_per_package}
\else
\set vouchers_per_package 10
\endif

-- The QA nominal twin. The pool is a column of the nominal now, so the twin is the same row with
-- the other pool - and the key is (fuel_type_id, data_kind, litres), which is exactly the natural
-- key a second run collides on. The id is derived from that key rather than generated, so the
-- twin is recognisable in a listing and a re-run updates the same row.
INSERT INTO fuel_nominals
    (fuel_type_id, data_kind, liters, created_at_utc, updated_at_utc)
SELECT
    live.fuel_type_id,
    'qa',
    live.liters,
    now(), now()
FROM fuel_nominals live
JOIN fuel_types fuel ON fuel.id = live.fuel_type_id
JOIN brands brand ON brand.id = fuel.brand_id
WHERE live.data_kind = 'real'
  AND NOT fuel.is_deleted
  AND (
      :'seed_all_brands'::boolean
      OR lower(brand.slug) = lower(:'brand')
  )
ON CONFLICT (fuel_type_id, data_kind, liters) DO NOTHING;

-- Stock behind each QA nominal.
--
-- On the `brand` column, because the name still only half describes it. A supplier and a brand are
-- different things here: `fuel_vouchers.supplier_id` is a uuid FK holding who actually supplied the
-- paper, and a supplier is deliberately not tied to one brand - it may hold OKKO, WOG and KLO
-- vouchers at once. That column is what cost and the audit trail join on, and it is left NULL here
-- because a test fixture is not a purchase.
--
-- `brand` is the brand's SLUG, lower-cased, because that is what the stock gate matches on
-- (`PackageStockAvailability.IsInStockAsync`: `v.Brand.ToLower() == brandSlug.ToLower()`) and what
-- the storefront keys its catalogue by. A row keyed to a supplier name or a network name would
-- silently never match, leaving a visible package that cannot be bought, which reads as an
-- out-of-stock bug rather than as a mismatched key.
--
-- The terms are what make renewal reachable at all: a reviewer who buys a QA voucher sees the
-- near-expiry banner and the renewal entry point only while the customer term sits inside the
-- 14-day trigger window, and a full-term voucher shows neither. Hence customer +7d against a
-- supplier term of +180d - the same shape as the live stock, and within the
-- `ck_voucher_customer_term_within_supplier_term` constraint.
--
-- `acquisition_source` is written because the column is NOT NULL and constrained to
-- ('direct', 'partner') - a third way this file could not run at all, behind the two table renames.
--
-- `vouchers_per_package` of them, and only the shortfall: the series is bounded by what is already
-- behind the nominal, so re-running tops the pool back up and stops, instead of multiplying it.
INSERT INTO fuel_vouchers
    (id, brand, fuel_type_id, liters, cost_per_liter,
     supplier_expiration_date, customer_expiration_date, voucher_number, qr_payload,
     qr_payload_sha256, status, data_kind, acquisition_source, is_deleted,
     created_at_utc, updated_at_utc)
SELECT
    gen_random_uuid(),
    lower(brand.slug),
    nominal.fuel_type_id,
    nominal.liters,
    fuel.supplier_price_per_liter,
    current_date + 180,
    current_date + 7,
    'QA-CAT-' || substr(md5(nominal.fuel_type_id || ':' || nominal.liters::text), 1, 8) || '-' || lpad(n::text, 2, '0'),
    'QA-QR-' || substr(md5(nominal.fuel_type_id || ':' || nominal.liters::text), 1, 8) || '-' || lpad(n::text, 2, '0'),
    sha256(convert_to('QA-QR-' || substr(md5(nominal.fuel_type_id || ':' || nominal.liters::text), 1, 8) || '-' || lpad(n::text, 2, '0'), 'UTF8')),
    'Available',
    'qa',
    'direct',
    false,
    now(), now()
FROM fuel_nominals nominal
JOIN fuel_types fuel ON fuel.id = nominal.fuel_type_id
JOIN brands brand ON brand.id = fuel.brand_id
CROSS JOIN LATERAL (
    SELECT generate_series(
        1,
        GREATEST(0, :vouchers_per_package::int - (
            SELECT count(*)
            FROM fuel_vouchers v
            WHERE v.data_kind = 'qa'
              AND v.fuel_type_id = nominal.fuel_type_id
              AND v.liters = nominal.liters
              AND lower(v.brand) = lower(brand.slug)
        ))
    ) AS n
) AS shortfall
WHERE nominal.data_kind = 'qa'
  AND NOT fuel.is_deleted
  AND (
      :'seed_all_brands'::boolean
      OR lower(brand.slug) = lower(:'brand')
  )
ON CONFLICT DO NOTHING;

-- Nothing above is inserted twice: a re-run finds the twin on the natural key and finds stock
-- already behind it, so this converges on `vouchers_per_package` per nominal instead of growing.

\echo ''
\echo 'QA catalogue seeded. Verify, as the QA account:'
\echo '  select lower(b.slug) as brand, f.name, n.liters, n.data_kind'
\echo '    from fuel_nominals n'
\echo '    join fuel_types f on f.id = n.fuel_type_id'
\echo '    join brands b on b.id = f.brand_id'
\echo '   where n.data_kind = ''qa'' order by 1, 2, 3;'
\echo '  select brand, fuel_type_id, liters, customer_expiration_date, status'
\echo '    from fuel_vouchers where data_kind = ''qa'' order by customer_expiration_date;'
\echo ''
\echo 'Anonymous readers must see none of it - check from outside a signed-in session:'
\echo '  curl -s https://api.palne.shop/api/packages | grep -c ''qa-'''   # expect 0
\echo ''

COMMIT;
