-- Data preconditions, checked BEFORE a new image is pulled.
--
-- One row per pair of live fuels at one brand that are the SAME grade, spelled two ways.
-- Zero rows means every brand has one live row per grade it sells.
--
-- WHY THIS EXISTS. The catalogue says a grade once: "А-95" and "A-95" are one grade, and the
-- owner's decision (planning #349, 2026-10-21) is that the second spelling must not become a
-- second row. A second row is not a cosmetic duplicate - it is a second price on the storefront
-- and a place a real voucher can be priced against, because the voucher importer resolves a fuel
-- by name and picks one of the two.
--
-- The index that refuses it is `ux_fuel_types_brand_grade_key`, on (brand_id, grade_key), and
-- the key is the name folded onto one spelling. The migration that adds it refuses to create the
-- index when the rows are already there, and a refusal inside a migration is a FAILED DEPLOY:
-- `MigrateDatabaseOnStartup` rethrows, so the API does not come up. Here the same question is
-- thirty seconds and a list of rows to reconcile, before anything is pulled.
--
-- WHY THE RULE IS WRITT OUT HERE instead of called. The function that computes the key
-- (`fuelflow_fuel_grade_key`) arrives with the migration that adds the column, so on the deploy
-- that carries the migration this file would have nothing to call - and the migration's own
-- guard is what refuses there. The expression below is that function's body, written out, and
-- `FuelTypeGradeKeyTests` reads both and compares them over every name the catalogue holds: a
-- preflight that asked a different question from the constraint would be a preflight that
-- reports "ok" while the deploy fails.
--
-- WHAT IT DOES NOT CATCH. The key says the NAME: "А-95" and "A-95" are one grade, and "Газ" and
-- "ГАЗ" are one grade. It does not say the PRODUCT - "Газ" and "LPG" are the same fuel and two
-- names, and only the brand's own code (`brand_fuel_code`) says they are one fuel. That half is
-- `ux_fuel_types_brand_brand_fuel_code`, and it was there before this file.
--
-- ARCHIVED ROWS ARE EXCLUDED, deliberately, and for the same reason as everywhere else here: the
-- index is partial on `NOT is_deleted`, so an archived twin is legal by design and is only
-- reported as history.

BEGIN;
SET TRANSACTION READ ONLY;

WITH graded AS (
    SELECT
        f.brand_id,
        f.id,
        f.name,
        replace(replace(replace(replace(
            regexp_replace(
                lower(translate(f.name,
                    'АБВГҐДЕЄЁЖЗИІЇЙКЛМНОПРСТУФХЦЧШЩЪЫЬЭЮЯабвгґдеєёжзиіїйклмнопрстуфхцчшщъыьэюя',
                    'ABVGGDEEEJZIIIJKLMNOPRSTUFHCCSS_Y_EUAabvggdeeejziiijklmnoprstufhccss_y_eua')),
                '[^a-z0-9+]', '', 'g'),
            'puls', 'pulls'),
        'dizel', 'diesel'),
        'gaz', 'gas'),
        'evro', 'euro')                                       AS grade_key
    FROM fuel_types f
    WHERE NOT f.is_deleted
)
SELECT
    g.brand_id,
    g.grade_key,
    count(*)                                                  AS live_rows,
    string_agg(g.name || ' (' || g.id::text || ')', ', ' ORDER BY g.name, g.id::text) AS fuels
FROM graded g
WHERE g.grade_key <> ''
GROUP BY g.brand_id, g.grade_key
HAVING count(*) > 1
ORDER BY g.brand_id, g.grade_key;

COMMIT;
