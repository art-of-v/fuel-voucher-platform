-- Seeds a QA-only mirror of the live fuel catalogue, so the App Store reviewer (and any QA session)
-- can browse and buy fuel without touching real stock.
--
-- WHAT THIS CREATES, for every live offer and every volume it sells:
--   * a `fuel_offers` twin carrying `data_kind = 'qa'`, mirroring the live offer's per-litre pricing;
--   * a `fuel_offer_volumes` twin under it, mirroring the litres and the package price;
--   * `vouchers_per_package` sellable `fuel_vouchers` rows behind each twin volume, also `data_kind = 'qa'`.
--
-- REWRITTEN 2026-10-16. The previous version of this script wrote to `fuel_packages`, which
-- `SplitFuelPackagesIntoOffers` (2026-10-15) turned into `fuel_offers` + `fuel_offer_volumes` with
-- nothing reading the old table. It therefore succeeded while seeding nothing at all: the QA
-- catalogue was empty and no error said so. Target #314's split is the cause and this is the
-- repair; `seed-qa-full-emulation.sql` was left stale by the same split and is tracked separately.
--
-- THE `(QA)` NAME SUFFIX IS GONE, and cannot come back without a schema change. The old script gave
-- every QA package `fuel_name || '(QA)'` "so it cannot be confused with the real thing". After the
-- split the offer no longer carries a name: the storefront joins it from `fuel_types`
-- (`GetPublicPackagesByStationQueryHandler`: `FuelName = fuel.Name`), and `fuel_types` has no pool
-- column. So a QA twin and its live counterpart are the same fuel and necessarily the same name.
--
-- The only two ways to mark them apart were both rejected then and are still:
--   * a QA-named `fuel_types` or `stations` row - neither table has a pool column and neither
--     endpoint filters on one, so a QA-named fuel would reach every anonymous visitor;
--   * a name override on the offer - a column that exists only to make test data legible, which is
--     the wrong direction for a schema whose next step (planning #321) is deleting the pool entirely.
--
-- What actually keeps the pools apart does not depend on the name. Both storefront routes resolve the
-- caller's pool before they filter (`offer.DataKind == DataKinds.ForPool(isTestAccount)`), the
-- fulfilment claim is gated on it as well, and `QaTestPoolSegregationIntegrationTests` pins that a QA
-- order only ever consumes QA stock and a real order only ever real stock. An operator sees both
-- pools in admin, where the distinction is legible. A reviewer sees the ordinary catalogue, which is
-- what App Review needs: browse, buy, see a voucher in the wallet.
--
-- WHY NO NEW `fuel_types` OR `stations` ROWS: as above - neither has a pool column, so a QA row
-- there reaches the public catalogue.
--
-- The live nominal is mirrored as-is. Availability keys on (station, fuel type, nominal) and carries
-- no offer id, so a QA volume at the same nominal is backed by the same key - a QA session sees the
-- live volume lit as well as its twin. That is cosmetic, not a leak, for the reason above.
--
-- Idempotent: re-running tops the stock back up to `vouchers_per_package` rather than failing, and
-- never exceeds it. The twins are keyed on the natural keys the database already enforces -
-- (station, fuel, pool) and (offer, litres) - not on the id, because the natural keys are what a
-- second run would collide on.
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
-- The live offers themselves already need a supplier, a batch cost and activation before this
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

-- Ten per volume is deep enough that a reviewer cannot exhaust a brand by accident, and shallow
-- enough that the "one left" state - and the out-of-stock state, which renders the package disabled -
-- are both still reachable during a session.
\if :{?vouchers_per_package}
\else
\set vouchers_per_package 10
\endif

-- The QA offer twin. The id follows OfferKeys.For: `<station>-<fuel>` for the live offer and
-- `<station>-<fuel>-qa` for this one, so the pair is recognisable in a listing and the column stays
-- inside its 204-character bound.
--
-- Conflict target is (station_id, fuel_type_id, data_kind) rather than the id on purpose. That is
-- the unique key the split added, and it is what a second run collides on - the id is derived from
-- the same triple, but an id conflict would only be reported if the two had drifted apart, and a
-- natural-key conflict is the one that would actually mean "already seeded".
INSERT INTO fuel_offers
    (id, station_id, fuel_type_id,
     supplier_price_per_liter, margin_uah_per_liter, margin_percent,
     pump_price_per_liter, min_discount_per_liter, price_updated_at,
     created_at_utc, updated_at_utc, is_deleted, deleted_at_utc, data_kind)
SELECT
    live.id || '-qa',
    live.station_id,
    live.fuel_type_id,
    live.supplier_price_per_liter,
    live.margin_uah_per_liter,
    live.margin_percent,
    live.pump_price_per_liter,
    live.min_discount_per_liter,
    live.price_updated_at,
    now(), now(), false, NULL,
    'qa'
FROM fuel_offers live
WHERE live.data_kind = 'real'
  AND NOT live.is_deleted
  AND (
      :'seed_all_stations'::boolean
      OR live.station_id = :'station_id'
  )
ON CONFLICT (station_id, fuel_type_id, data_kind) DO NOTHING;

-- The QA volume twin, mirroring litres. The live volume kept its own id through the split
-- (the migration copied `p.id` across), so this one prefixes rather than appends - and a volume id
-- that would breach ck_fuel_offer_volumes_id_length is refused loudly by that CHECK rather than
-- silently truncated into a collision.
--
-- The twin also carries no price, and cannot: `price_override_uah` is gone, and a nominal's price is
-- the fuel's rate x its litres. The margin lives on `fuel_types`, which the two pools share, so the QA
-- catalogue is priced exactly like the live one. The pool separates stock and data, not money.
INSERT INTO fuel_offer_volumes
    (id, offer_id, liters,
     created_at_utc, updated_at_utc, is_deleted, deleted_at_utc)
SELECT
    'qa-' || live.id,
    live_offer.id || '-qa',
    live.liters,
    now(), now(), false, NULL
FROM fuel_offer_volumes live
JOIN fuel_offers live_offer ON live_offer.id = live.offer_id
WHERE live_offer.data_kind = 'real'
  AND NOT live_offer.is_deleted
  AND NOT live.is_deleted
  AND (
      :'seed_all_stations'::boolean
      OR live_offer.station_id = :'station_id'
  )
ON CONFLICT (offer_id, liters) DO NOTHING;

-- Stock behind each QA volume.
--
-- On the `brand` column, because the name still only half describes it. A supplier and a station are
-- different things here: `fuel_vouchers.supplier_id` is a uuid FK holding who actually supplied the
-- paper, and a supplier is deliberately not tied to one brand - it may hold OKKO, WOG and KLO
-- vouchers at once. That column is what cost and the audit trail join on, and it is left NULL here
-- because a test fixture is not a purchase.
--
-- `brand` is a varchar, and both the storefront key (StockKey) and the fulfilment claim match it
-- against `fuel_offers.station_id` - lower-cased, case-insensitive. So whatever is put here has to
-- be the station id for the volume to find its own stock. A row keyed to a supplier name or a
-- network name would silently never match, leaving a visible package that cannot be bought, which
-- reads as an out-of-stock bug rather than as a mismatched key.
--
-- The terms are what make renewal reachable at all: a reviewer who buys a QA voucher sees the
-- near-expiry banner and the renewal entry point only while the customer term sits inside the
-- 14-day trigger window, and a full-term voucher shows neither. Hence customer +7d against a
-- supplier term of +180d - the same shape as the live stock.
--
-- `vouchers_per_package` of them, and only the shortfall: the series is bounded by what is already
-- behind the volume, so re-running tops the pool back up and stops, instead of multiplying it.
INSERT INTO fuel_vouchers
    (id, brand, fuel_type_id, liters, cost_per_liter,
     supplier_expiration_date, customer_expiration_date, voucher_number, qr_payload,
     status, data_kind, is_deleted, created_at_utc, updated_at_utc)
SELECT
    gen_random_uuid(),
    lower(offer.station_id),
    offer.fuel_type_id,
    volume.liters,
    offer.supplier_price_per_liter,
    current_date + 180,
    current_date + 7,
    'QA-CAT-' || substr(md5(volume.id), 1, 8) || '-' || lpad(n::text, 2, '0'),
    'QA-QR-' || substr(md5(volume.id), 1, 8) || '-' || lpad(n::text, 2, '0'),
    'Available',
    'qa',
    false,
    now(), now()
FROM fuel_offer_volumes volume
JOIN fuel_offers offer ON offer.id = volume.offer_id
CROSS JOIN LATERAL (
    SELECT generate_series(
        1,
        GREATEST(0, :vouchers_per_package::int - (
            SELECT count(*)
            FROM fuel_vouchers v
            WHERE v.data_kind = 'qa'
              AND v.fuel_type_id = offer.fuel_type_id
              AND v.liters = volume.liters
              AND lower(v.brand) = lower(offer.station_id)
        ))
    ) AS n
) AS shortfall
WHERE offer.data_kind = 'qa'
  AND NOT offer.is_deleted
  AND NOT volume.is_deleted
  AND (
      :'seed_all_stations'::boolean
      OR offer.station_id = :'station_id'
  )
ON CONFLICT DO NOTHING;

-- Nothing above is inserted twice: a re-run finds the twin on the natural key and finds stock
-- already behind it, so this converges on `vouchers_per_package` per volume instead of growing.

\echo ''
\echo 'QA catalogue seeded. Verify, as the QA account:'
\echo '  select o.brand_id, f.name, v.liters, o.data_kind'
\echo '    from fuel_offers o'
\echo '    join fuel_offer_volumes v on v.offer_id = o.id'
\echo '    join fuel_types f on f.id = o.fuel_type_id'
\echo '   where o.id like ''%-qa'' and not o.is_deleted order by 1, 2, 3;'
\echo '  select brand, fuel_type_id, liters, customer_expiration_date, status'
\echo '    from fuel_vouchers where data_kind = ''qa'' order by customer_expiration_date;'
\echo ''
\echo 'Anonymous readers must see none of it - check from outside a signed-in session:'
\echo '  curl -s https://api.palne.shop/api/packages | grep -c ''qa-'''   # expect 0
\echo ''

COMMIT;