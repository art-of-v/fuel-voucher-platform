-- Data preconditions, checked BEFORE a new image is pulled.
--
-- What `fuel_types.id` actually is on THIS database, and what type every column that points at it
-- carries. Zero rows means the schema matches what the migrations assume.
--
-- WHY THIS EXISTS. The repository's model says `fuel_types.id` is `text`, and
-- `AddStationCatalog` seeded it as `'okko-95'`. Production disagrees: PR #943 ran a seed against
-- the deployed database on 2026-10-09 and got
--
--     insert or update on table "fuel_vouchers" violates foreign key constraint
--     Key (fuel_type_id)=(okko-95) is not present in table "fuel_types"
--
-- which only happens if the column cannot hold `okko-95` - i.e. it is already a uuid, with no text
-- key to find. So the two schemas diverged somewhere between 2026-06-18 and 2026-10-09, and the
-- repository cannot say where: a migration, a manual seed and a separate dataset all look the same
-- from the code.
--
-- This matters now because planning D-21's second slice is exactly this conversion. `brands` and
-- `brand_stations` shipped 2026-10-18 and the second slice was assumed to be the same shape of
-- work. It is not, or it is not only: if the live column is already uuid then the migration is not
-- a conversion at all, and the `okko-95`-shaped keys in the seed, the parsers and the QA scripts
-- need a crosswalk this document has never worked out. Planning records that as the blocker on the
-- slice; this file is how the blocker gets turned into a fact before anyone writes the migration.
--
-- WHY information_schema AND NOT THE TABLES. The evidence above says production still has
-- `fuel_types.station_id` where the repository has `brand_id`, so a query that SELECTs `f.brand_id`
-- fails on exactly the database this file exists to describe. Every statement below reads the
-- catalogue, never a table, so it answers on both schemas or on neither.
--
-- WHY A QUESTION, NOT A GATE, unlike `010`. Nothing here is wrong by itself: a uuid primary key on
-- the fuel is where D-21 wants to land. It is only surprising, and a deploy should not be held for a
-- surprise that is already documented - so this reports and moves on, like `012`.

BEGIN;
SET TRANSACTION READ ONLY;

-- The primary key, named rather than inferred, so the answer is about the column and not about
-- whichever one happens to be the target of the PK.
SELECT
    c.column_name,
    c.data_type,
    c.character_maximum_length,
    c.is_nullable,
    c.column_default
FROM information_schema.columns c
JOIN information_schema.tables t
    ON t.table_schema = c.table_schema
   AND t.table_name = c.table_name
   AND t.table_type = 'BASE TABLE'
WHERE c.table_schema = 'public'
  AND c.table_name = 'fuel_types'
  AND c.column_name = 'id';

-- Everything that points at a fuel type. These have to move together with it or not at all: a
-- varchar FK against a uuid PK is the exact mismatch that stopped D-21's first slice from being
-- expressible, so the slice cannot start until this list is known to be homogeneous.
SELECT
    c.table_name,
    c.column_name,
    c.data_type,
    c.character_maximum_length,
    EXISTS (
        -- "Points at a fuel type" is a property of the CONSTRAINT, not of the column: a
        -- `fuel_type_id` with no FK on it points at nothing in particular. Constraint_column_usage
        -- carries the referenced side, which is the side this question is about.
        SELECT 1
        FROM information_schema.table_constraints tc
        JOIN information_schema.constraint_column_usage ccu
            ON ccu.constraint_name = tc.constraint_name
           AND ccu.constraint_schema = tc.constraint_schema
        WHERE tc.constraint_type = 'FOREIGN KEY'
          AND tc.table_schema = c.table_schema
          AND tc.table_name = c.table_name
          AND ccu.table_schema = 'public'
          AND ccu.table_name = 'fuel_types'
    ) AS references_fuel_types
FROM information_schema.columns c
JOIN information_schema.tables t
    ON t.table_schema = c.table_schema
   AND t.table_name = c.table_name
   AND t.table_type = 'BASE TABLE'
WHERE c.table_schema = 'public'
  AND c.column_name IN ('fuel_type_id', 'fuel_type', 'nominal_fuel_type_id')
ORDER BY c.table_name, c.column_name;

-- The FK constraint names into `fuel_types`, if any are declared. The model says most catalogue
-- FKs are still absent (planning D-02), so an empty result is the expected answer rather than a
-- finding. There is no `referenced_table_name` on `table_constraints` - the referenced side lives
-- in `constraint_column_usage`, which is why this joins the two rather than reading one.
SELECT
    kcu.table_name,
    tc.constraint_name,
    kcu.column_name,
    ccu.table_name AS referenced_table
FROM information_schema.table_constraints tc
JOIN information_schema.key_column_usage kcu
    ON kcu.constraint_name = tc.constraint_name
   AND kcu.constraint_schema = tc.constraint_schema
JOIN information_schema.constraint_column_usage ccu
    ON ccu.constraint_name = tc.constraint_name
   AND ccu.constraint_schema = tc.constraint_schema
WHERE tc.constraint_type = 'FOREIGN KEY'
  AND ccu.table_schema = 'public'
  AND ccu.table_name = 'fuel_types'
ORDER BY kcu.table_name, tc.constraint_name;

COMMIT;

-- A few ids, because the type alone does not say whether the values are the ones this repository
-- writes. A uuid column full of ids this code cannot produce is a different problem from a text
-- column full of keys it can, and only the rows tell them apart.
--
-- Outside the transaction on purpose: this one has to name a table, and naming a table that does not
-- exist is the one error the queries above were written to avoid. On a database with no
-- `fuel_types` at all - which is what a fresh container looks like, and is how this file was
-- checked - the answer is "nothing to report" rather than a crash.
SELECT to_regclass('public.fuel_types') IS NOT NULL AS fuel_types_exists \gset

\if :fuel_types_exists
    SELECT id
    FROM fuel_types
    ORDER BY created_at_utc
    LIMIT 10;
\endif