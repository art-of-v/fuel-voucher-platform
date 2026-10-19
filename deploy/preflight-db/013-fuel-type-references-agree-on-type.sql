-- Data preconditions, checked BEFORE a new image is pulled.
--
-- One row per column that names a fuel type but disagrees with `fuel_types.id` about what a fuel
-- type's id IS. Zero rows means every reference and the thing it points at are the same type.
--
-- WHY THIS EXISTS. Production carries the fuel's identity as a uuid VALUE inside a `text` COLUMN:
--
--     CREATE TABLE public.fuel_types (
--         id text NOT NULL,
--         CONSTRAINT ck_fuel_types_id_length CHECK ((length(id) <= 100)),
--         brand_id uuid NOT NULL,
--     )
--     01a14b20-c2f0-7207-b803-43b246a8eb08,А-95,...
--
-- Planning D-21's second slice converts that column to `uuid`, and the conversion is only
-- expressible if the tables pointing at it move with it. A `varchar(100)` FK against a `text` PK is
-- the mismatch that made the same problem inexpressible in the first slice, so it is worth knowing
-- the list is homogeneous BEFORE the migration is written rather than after it fails on it.
--
-- WHAT THIS FILE GOT WRONG THE FIRST TIME, because it took the production deploy down with it.
-- The first version answered "what type is `fuel_types.id`, and here are ten of its values" - and
-- every row a precondition emits is counted as a violation:
--
--     rows="$(... psql ... -f - < "$sql" 2>/dev/null | grep -c '|')"
--     if [[ "${rows:-0}" -eq 0 ]]; then echo "  $name: ok"
--
-- Ten healthy ids is ten violations. With `PREFLIGHT_DB_STRICT=1` on the production box that is an
-- exit 1 and nothing is pulled, which on 2026-10-19 it did. The header claimed to be "a question,
-- not a gate, like 012" - and 012 is a gate in everything but name. The harness has no notion of a
-- question: a precondition either emits nothing or stops the deploy. So a file in this directory
-- that wants to REPORT rather than FLAG cannot exist here, and this one now does the second thing.
--
-- The information it used to carry did not die with it. What `fuel_types.id` is on production was
-- read once, from the CI dry run's dump, and is recorded in `TARGET_SCHEMA.md` §D-21 - which is
-- where a finding belongs, since a finding that nobody re-reads is not a finding.

BEGIN;
SET TRANSACTION READ ONLY;

-- The type every reference has to match, read from the catalogue rather than assumed, so this is
-- correct on a database that has already converted and still correct on one that has not.
SELECT to_regclass('public.fuel_types') IS NOT NULL AS t \gset
\if :t
WITH fuel_key AS (
    SELECT data_type, coalesce(character_maximum_length, -1) AS max_length
    FROM information_schema.columns
    WHERE table_schema = 'public' AND table_name = 'fuel_types' AND column_name = 'id'
)
SELECT
    c.table_name,
    c.column_name,
    c.data_type                        AS reference_type,
    coalesce(c.character_maximum_length, -1) AS reference_max_length,
    f.data_type                        AS fuel_types_id_type,
    f.max_length                       AS fuel_types_id_max_length
FROM information_schema.columns c
CROSS JOIN fuel_key f
JOIN information_schema.tables t
    ON t.table_schema = c.table_schema
   AND t.table_name = c.table_name
   AND t.table_type = 'BASE TABLE'
WHERE c.table_schema = 'public'
  AND c.column_name IN ('fuel_type_id', 'fuel_type', 'nominal_fuel_type_id')
  AND (c.data_type <> f.data_type
       OR coalesce(c.character_maximum_length, -1) <> f.max_length)
ORDER BY c.table_name, c.column_name;
\endif

COMMIT;