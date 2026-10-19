-- Data preconditions, checked BEFORE a new image is pulled.
--
-- One row per live fuel name that more than one live fuel type claims at the same brand.
-- Zero rows means every brand has exactly one live row per fuel it lists.
--
-- WHY THIS EXISTS. `DropTheStationPrefixFromOfferKeys` (2026-10-17) made the fuel-name index
-- UNIQUE, and refuses to apply if it cannot:
--
--     fuel_types carries more than one live row for: okko/А-95 (2 rows), ...
--
-- A fuel type used to be unreachable twice at the same station, because its id WAS its
-- name - 'okko-95' - and a second A-95 collided on the primary key. `BrandsController.AddFuel`
-- generates `Guid.CreateVersion7()` instead, so the collision stopped being possible, and the
-- duplicate is not a cosmetic row: it becomes a second offer, the storefront lists one fuel at
-- two prices, and `OkkoFuelClassifier`/`WogFuelClassifier` resolve a parsed voucher by
-- `FirstOrDefault` over the name, so a real voucher can be priced against the twin that never
-- received the imported pump price.
--
-- Same shape as `010`: the migration is right to refuse - it cannot merge two fuels on the
-- user's behalf - but a refusal there is a FAILED DEPLOY. Here it is thirty seconds.
--
-- WHAT CHANGED HERE SINCE IT WAS WRITTEN, and it was wrong from 2026-10-18 until 2026-10-19.
-- This file grouped by `f.station_id`. `20261018191320_CatalogueBrandsBecomeUuid` renamed that
-- column to `brand_id`, because a station was a brand and a node was a station - so the query
-- had been aborting with `column f.station_id does not exist` on every deploy since, and
-- NOTHING SAID SO:
--
--     rows="$(... psql ... -f - < "$sql" 2>/dev/null | grep -c '|' || true)"
--
-- discards psql's stderr, and a SQL error produces no stdout, so an aborted query counted as
-- zero rows and was reported "011-fuel-type-name-twins.sql: ok". A gate that cannot run was a
-- gate that said yes. The runner now runs psql with ON_ERROR_STOP=1 and reports a check it
-- could not run as a warning rather than a pass, and `PreflightSqlExecutesTests` executes
-- every file in this directory against a migrated database so a renamed column cannot reach
-- the deploy path again.
--
-- The index this guards is `ux_fuel_types_brand_name`, on (brand_id, name), partial on
-- `NOT is_deleted` - the old `ux_fuel_types_station_name` name went with the rename. The
-- invariant did not change, only the column and the index that enforce it.
--
-- WHAT THIS DOES NOT CATCH. Two rows whose names differ but mean the same grade ('А-95' in
-- Cyrillic and 'A-95' in Latin, 'ДП ЄВРО' and 'ДП Euro') are still two rows, and no index on
-- the name can merge them: what says they are the same fuel is the classifier's category, which
-- lives in C#. That is the "brand + normalized grade" pair planning #140 proposes, and it is
-- the remaining half of this problem (tracked in planning #349).
--
-- ARCHIVED ROWS ARE EXCLUDED, deliberately, and for the same reason as everywhere else here: the
-- index is partial on `NOT is_deleted`, so an archived twin is legal by design and is only
-- reported as history.

BEGIN;
SET TRANSACTION READ ONLY;

SELECT
    f.brand_id,
    f.name                                                          AS fuel_name,
    count(*)                                                        AS live_rows,
    string_agg(f.id, ', ' ORDER BY f.created_at_utc, f.id)          AS fuel_type_ids
FROM fuel_types f
WHERE NOT f.is_deleted
GROUP BY f.brand_id, f.name
HAVING count(*) > 1
ORDER BY f.brand_id, f.name;

COMMIT;