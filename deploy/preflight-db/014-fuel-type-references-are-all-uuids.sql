-- Data preconditions, checked BEFORE a new image is pulled.
--
-- Every reference to a fuel type that still holds a TEXT-shaped id - 'okko-95' and its kind - rather
-- than a uuid. Zero rows means planning D-21's second slice is a type change and nothing else.
--
-- WHY THIS EXISTS. `20261018191320_CatalogueBrandsBecomeUuid` shipped 2026-10-18 and, per the CI dry
-- run's production dump, the live `fuel_types` rows read:
--
--     01a14b20-c2f0-7207-b803-43b246a8eb08,А-95,...,89a11944-bdb3-5ed4-b55c-edae31335a6e
--
-- (the brand id is the derived one: `20261019201500_SeededBrandIdsBecomeDerived` gave the seeded
-- networks ids derived from their slug, and the dump of 2026-10-18 carried the constant that
-- migration replaced. The fuel type's own id is what this file is about, and it does not change.)
--
-- so the fuel's identity on production is ALREADY a uuid - carried in a `text` column. Planning
-- records the second slice as blocked because "production `fuel_types.id` is already a uuid, so the
-- slice needs a crosswalk". The column is not uuid; the VALUES are, and those are two different
-- statements with two different consequences. A crosswalk is needed for the first and not for the
-- second:
--
--   - Every value already parses, so `ALTER COLUMN id TYPE uuid USING id::uuid` succeeds on every
--     row and there is nothing to translate.
--   - What is left to translate is every OTHER table that names a fuel by that id, and those have
--     no foreign key to check them: `order_line_items.fuel_type_id varchar(100)`, and the same
--     shape on `purchase_batches`. An orphaned text id there would survive
--     the type change as a row that can never resolve, and nothing in the schema would say so.
--
-- That second list is the whole reason this file exists, and the reason it is a values question
-- rather than a types question. `013` reports what the columns ARE; this reports what they HOLD.
-- Converting a `text` column full of uuids is a formality. Converting one that still has 'okko-95'
-- in a table with no FK is data loss dressed as a migration, and the only place it shows up is here.
--
-- WHEN THE LAST TEXT ID DISAPPEARED. `c353f630` (2026-10-12) switched generated keys to time-ordered
-- uuids, but the production dump shows a fuel created 2026-09-24 already holding a uuid, so the
-- switch was earlier than that commit and the repository does not record when. Rows created before
-- whatever replaced the natural key are the only ones at risk, and this file is how their existence
-- is established rather than assumed either way.
--
-- WHY EVERY TABLE IS GUARDED, AND WHY THEY ARE NOT ONE QUERY. The runner counts rows and discards
-- psql's stderr:
--
--     rows="$(... psql ... -f - < "$sql" 2>/dev/null | grep -c '|')"
--
-- so a file that aborts on a missing relation emits no rows and is reported "ok". A check that
-- cannot tell "clean" from "crashed" is worse than none. Each table therefore gets its own
-- `to_regclass` guard and its own statement - a single UNION over four tables would look tidier and
-- would reintroduce exactly the failure: one missing table takes the other three down with it, and
-- the guard that was supposed to prevent it guards only the first branch.
--
-- WHY A QUESTION, NOT A GATE, like `011` and `012`. Finding rows here is not a broken database - it
-- is the one fact D-21's second slice has to be planned around, and the plan belongs to a human. The
-- migration it informs can still refuse; this only has to make the refusal not surprising.
--
-- WHY THE COLUMNS ARE COMPARED AS TEXT. The columns this reads were text until D-21's second slice
-- converted them to uuid, and `uuid !~* '...'` is not an operator Postgres has: the file would stop
-- running the day the column changed, and a precondition that cannot run reports success without
-- having checked anything. The cast runs on both shapes, so the file answers the same question
-- before and after the conversion - which is the only way it can be a question at all.

BEGIN;
SET TRANSACTION READ ONLY;

-- The referenced side first: a fuel type whose own id is text is the root of the problem, and every
-- dangling reference points back at one.
SELECT to_regclass('public.fuel_types') IS NOT NULL AS t \gset
\if :t
SELECT 'fuel_types' AS table_name, id, name AS detail
FROM fuel_types
WHERE id::text !~* '^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$'
ORDER BY id;
\endif

-- Then every table that names one. No ORDER BY across the statements: they run in sequence, and the
-- order they run in is the order above, which is also the order worth reading them in.
SELECT to_regclass('public.order_line_items') IS NOT NULL AS t \gset
\if :t
SELECT 'order_line_items' AS table_name, fuel_type_id AS id, count(*)::text AS rows
FROM order_line_items
WHERE fuel_type_id IS NOT NULL
  AND fuel_type_id::text !~* '^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$'
GROUP BY fuel_type_id
ORDER BY fuel_type_id;
\endif

SELECT to_regclass('public.purchase_batches') IS NOT NULL AS t \gset
\if :t
SELECT 'purchase_batches' AS table_name, fuel_type_id AS id, count(*)::text AS rows
FROM purchase_batches
WHERE fuel_type_id IS NOT NULL
  AND fuel_type_id::text !~* '^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$'
GROUP BY fuel_type_id
ORDER BY fuel_type_id;
\endif

-- The exchange pairs used to live in their own table and carried a fuel_type_id of their own. They
-- live in the ledger now (planning #319), and the ledger's rows name the paper, not a fuel - so
-- there is nothing left to check here. The check that remains is the one above: the tables that
-- still carry a fuel of their own.

COMMIT;
