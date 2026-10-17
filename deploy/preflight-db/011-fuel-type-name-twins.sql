-- Data preconditions, checked BEFORE a new image is pulled.
--
-- One row per live fuel name that more than one live fuel type claims at the same station.
-- Zero rows means every station has exactly one live row per fuel it lists.
--
-- WHY THIS EXISTS. `DropTheStationPrefixFromOfferKeys` (2026-10-17) makes
-- `ux_fuel_types_station_name` UNIQUE, and refuses to apply if it cannot:
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
-- WHAT THIS DOES NOT CATCH. Two rows whose names differ but mean the same grade ('А-95' in
-- Cyrillic and 'A-95' in Latin, 'ДП ЄВРО' and 'ДП Euro') are still two rows, and no index on
-- the name can merge them: what says they are the same fuel is the classifier's category, which
-- lives in C#. That is the "brand + normalized grade" pair planning #140 proposes, and it is
-- the remaining half of this problem.
--
-- ARCHIVED ROWS ARE EXCLUDED, deliberately, and for the same reason as everywhere else here: the
-- index is partial on `NOT is_deleted`, so an archived twin is legal by design and is only
-- reported as history.

BEGIN;
SET TRANSACTION READ ONLY;

SELECT
    f.station_id,
    f.name                                                          AS fuel_name,
    count(*)                                                        AS live_rows,
    string_agg(f.id, ', ' ORDER BY f.created_at_utc, f.id)          AS fuel_type_ids
FROM fuel_types f
WHERE NOT f.is_deleted
GROUP BY f.station_id, f.name
HAVING count(*) > 1
ORDER BY f.station_id, f.name;

COMMIT;