-- The schema's fingerprint: one canonical line per object, sorted.
--
-- WHY THIS EXISTS, and why it is not a diff of two pg_dump files.
--
-- The question this answers is "can the repository rebuild what production has?" - and the
-- obvious way to ask it is to dump both schemas and diff the text. That does not work, and
-- the reason is worth writing down, because the failure is silent rather than loud:
-- pg_dump emits indexes, constraints and comments in the order the database happens to
-- hold them, which is the order of their OIDs. Two databases with the SAME schema built in
-- a different order produce different TEXT. So a text diff reports differences that are
-- not differences, and the first person to hit that learns to ignore the output - which is
-- exactly how a real difference gets through.
--
-- The catalog, on the other hand, does not care about creation order. Everything below is
-- a query against pg_catalog, and the result is sorted, so two databases agree exactly
-- when their fingerprints are byte-identical.
--
-- WHAT IS IN THE FINGERPRINT: everything the application depends on - schemas, extensions,
-- enum labels, tables, columns (type, nullability, default, identity, generated,
-- collation), constraints, indexes, views, triggers, functions, sequences, comments.
--
-- WHAT IS NOT: the DATA (a fingerprint is about shape), the OWNERSHIP and the PRIVILEGES
-- (the dump that feeds the comparison is taken with --no-owner --no-privileges, and the
-- two databases are not the same database), and the __EFMigrationsHistory ROWS - the table
-- itself IS in the fingerprint, because its shape is the shape EF creates.
--
-- HOW TO USE IT:
--   psql -X -f deploy/schema-fingerprint.sql "$URL" > a.fingerprint
--   diff -u a.fingerprint b.fingerprint
--
-- The output has one object per line and nothing else, so the diff names the objects that
-- differ and says nothing about how they differ. That is deliberate: a fingerprint that
-- printed a diff-friendly representation of each object would be a second schema
-- definition to keep in sync, and this file has to stay the only one.
--
-- THE ONE SCHEMA THAT IS EXCLUDED: 'hangfire'. Hangfire creates its own tables, in its
-- own schema, the first time the application starts - not through a migration, and not at
-- any time the migrations could. A database that has run the application therefore has a
-- 'hangfire' schema and a database built from the migrations does not, and that difference
-- says nothing about whether the repository can rebuild production. It is excluded here,
-- in one place, rather than filtered out of the diff - a filter in the diff is a rule
-- somebody can forget to widen, and an exclusion in the fingerprint is a rule that is
-- written down next to the thing it excludes.
--
-- (The toast indexes are excluded for a different reason and are handled where they are
-- generated: their names carry an OID, so they differ between two identical databases.)

\pset pager off
\pset format unaligned
\pset tuples_only on
-- THE CANONICAL FORM OF A CONSTRAINT, and why the fingerprint needs one.
--
-- A check constraint's stored form depends on HOW it was created, not only on what it says.
-- Two databases with the same rule can hold it in two different shapes, and PostgreSQL
-- re-shapes it on the way out of a dump:
--
--   CREATE TABLE t (status varchar(50) CHECK (status IN ('Active', 'Revoked')));
--   -- stored and reported as:  (ARRAY['Active'::character varying, 'Revoked'::character varying])::text[]
--
--   CREATE TABLE t (status varchar(50) CHECK (((status)::text = ANY (ARRAY[('Active'::character varying)::text, ...]))));
--   -- stored and reported as:  ARRAY[('Active'::character varying)::text, ...]
--
-- Both enforce the same rule, and BOTH become the second shape when the first one is written
-- out and parsed again. So the shape is an artefact of the parser's history, not a property
-- of the schema - and a fingerprint that compares it reports a difference where there is none.
--
-- That is not a hypothetical: on 2026-10-20 this comparison found 46 check constraints and one
-- partial index that "differed" between a from-zero build and production, and every one of
-- them was the same rule in the same two shapes.
--
-- The canonical form is therefore "what PostgreSQL makes of this definition when it parses
-- it again", and it is computed the only honest way: by CREATING the constraint on a
-- throwaway table and reading back what the catalog says. Both shapes land on the same
-- canonical text, and a real difference - another value, another column, another operator -
-- still lands on a different one.

-- The notices are silenced because the probe table is dropped after every constraint, and
-- "table does not exist, skipping" is the expected answer to dropping a table that the
-- exception handler has already rolled back.
SET client_min_messages = warning;

DO $canonicalise$
DECLARE
    r record;
    canonical text;
BEGIN
    CREATE TEMP TABLE IF NOT EXISTS canonical_objects (
        kind text NOT NULL,
        table_name text NOT NULL,
        object_name text NOT NULL,
        definition text NOT NULL
    );

    -- The check constraints.
    FOR r IN
        SELECT c2.relname AS table_name,
               con.conname AS constraint_name,
               -- pg_get_expr, NOT pg_get_constraintdef: the latter wraps the expression in
               -- "CHECK (...)", and re-adding that wrapper would be "CHECK (CHECK (...))".
               pg_catalog.pg_get_expr(con.conbin, con.conrelid) AS definition
        FROM pg_catalog.pg_constraint con
        JOIN pg_catalog.pg_class c2 ON c2.oid = con.conrelid
        JOIN pg_catalog.pg_namespace n ON n.oid = c2.relnamespace
        WHERE con.contype = 'c'
          AND n.nspname = 'public'
          AND c2.relkind IN ('r', 'p')
    LOOP
        canonical := NULL;
        BEGIN
            EXECUTE format('CREATE TEMP TABLE canonical_probe (LIKE public.%I)', r.table_name);
            EXECUTE format('ALTER TABLE canonical_probe ADD CONSTRAINT canonical_probe_ck CHECK (%s)', r.definition);
            SELECT pg_catalog.pg_get_constraintdef(con.oid) INTO canonical
            FROM pg_catalog.pg_constraint con
            WHERE con.conrelid = 'canonical_probe'::regclass
              AND con.conname = 'canonical_probe_ck';
        EXCEPTION WHEN OTHERS THEN
            -- A definition that will not parse on its own table is reported as it stands.
            -- It is still compared, and it is still compared by its text.
            canonical := NULL;
        END;
        EXECUTE 'DROP TABLE IF EXISTS canonical_probe';
        INSERT INTO canonical_objects
        SELECT 'constraint', r.table_name, r.constraint_name, COALESCE(canonical, r.definition);
    END LOOP;

    -- The indexes, for the same reason: a PARTIAL index carries a predicate, and the
    -- predicate is stored in the same two shapes as a check constraint's expression.
    FOR r IN
        SELECT c2.relname AS table_name,
               i2.relname AS index_name,
               pg_catalog.pg_get_indexdef(i.indexrelid) AS definition
        FROM pg_catalog.pg_index i
        JOIN pg_catalog.pg_class c2 ON c2.oid = i.indrelid
        JOIN pg_catalog.pg_class i2 ON i2.oid = i.indexrelid
        JOIN pg_catalog.pg_namespace n ON n.oid = c2.relnamespace
        WHERE n.nspname = 'public'
          AND i.indpred IS NOT NULL
          AND c2.relkind IN ('r', 'p')
    LOOP
        canonical := NULL;
        BEGIN
            EXECUTE format('CREATE TEMP TABLE canonical_probe (LIKE public.%I)', r.table_name);
            -- The index's own definition, with the table it belongs to swapped for the
            -- throwaway one, and with the index renamed so it cannot collide.
            EXECUTE replace(
                replace(r.definition, 'public.' || r.table_name, 'canonical_probe'),
                r.index_name, 'canonical_probe_ix');
            SELECT pg_catalog.pg_get_indexdef(i.indexrelid) INTO canonical
            FROM pg_catalog.pg_index i
            WHERE i.indexrelid = 'canonical_probe_ix'::regclass;
            -- The probe's own names are put back, so the canonical definition is the real
            -- index's definition: the name is part of the shape, and a comparison that
            -- compared two probe names would not compare it.
            IF canonical IS NOT NULL THEN
                canonical := replace(replace(canonical, 'canonical_probe_ix', r.index_name),
                                    'pg_temp.canonical_probe', 'public.' || r.table_name);
            END IF;
        EXCEPTION WHEN OTHERS THEN
            canonical := NULL;
        END;
        EXECUTE 'DROP TABLE IF EXISTS canonical_probe';
        INSERT INTO canonical_objects
        SELECT 'index', r.table_name, r.index_name, COALESCE(canonical, r.definition);
    END LOOP;
END
$canonicalise$;

WITH objects AS (

    -- The schemas themselves. A table in a schema that does not exist on the other side
    -- would otherwise show up only as a missing table, which is a slower thing to read.
    SELECT 'schema ' || n.nspname AS line
    FROM pg_catalog.pg_namespace n
    WHERE n.nspname NOT IN ('pg_catalog', 'information_schema', 'hangfire')
      AND n.nspname NOT LIKE 'pg_toast%'
      AND n.nspname NOT LIKE 'pg_temp%'

    UNION ALL

    -- Extensions, with their versions: an extension's version changes what it creates.
    SELECT 'extension ' || e.extname || ' ' || e.extversion
    FROM pg_catalog.pg_extension e
    WHERE e.extname <> 'plpgsql'

    UNION ALL

    -- Enum types, with their labels in label order. The ORDER BY inside string_agg is
    -- load-bearing: the labels of an enum are ordered, and that order is part of the type.
    SELECT 'enum ' || n.nspname || '.' || t.typname || ' = ' ||
           string_agg(e.enumlabel, ',' ORDER BY e.enumsortorder)
    FROM pg_catalog.pg_type t
    JOIN pg_catalog.pg_namespace n ON n.oid = t.typnamespace
    JOIN pg_catalog.pg_enum e ON e.enumtypid = t.oid
    GROUP BY n.nspname, t.typname

    UNION ALL

    -- Tables. 'p' is a partitioned table, which is a table with a shape of its own.
    SELECT 'table ' || n.nspname || '.' || c.relname
    FROM pg_catalog.pg_class c
    JOIN pg_catalog.pg_namespace n ON n.oid = c.relnamespace
    WHERE c.relkind IN ('r', 'p')
      AND n.nspname NOT IN ('pg_catalog', 'information_schema', 'hangfire')
      AND n.nspname NOT LIKE 'pg_temp%'
      AND n.nspname NOT LIKE 'pg_toast%'

    UNION ALL

    -- Columns. This is the line that matters most: a column's TYPE, its nullability, its
    -- DEFAULT and its COLLATION are what an application reads, and a migration that
    -- creates a column differently from the model is invisible until something reads it.
    --
    -- attisdropped columns are excluded, and that is not a shortcut: a dropped column
    -- leaves a tombstone with a different type, and the tombstone is part of the
    -- physical shape but not of the logical one.
    SELECT 'column ' || n.nspname || '.' || c.relname || '.' || a.attname
           || ' type=' || pg_catalog.format_type(a.atttypid, a.atttypmod)
           || ' notnull=' || CASE WHEN a.attnotnull THEN 'yes' ELSE 'no' END
           || ' identity=' || CASE WHEN a.attidentity <> '' THEN a.attidentity::text ELSE 'no' END
           || ' generated=' || CASE WHEN a.attgenerated <> '' THEN a.attgenerated::text ELSE 'no' END
           -- 0 means "whatever the database's default is". The NAME is shown for an
           -- explicit collation, because that is a choice the migration made.
           || ' collation=' || CASE WHEN a.attcollation = 0 THEN 'default' ELSE co.collname END
           || ' default=' || COALESCE(pg_catalog.pg_get_expr(d.adbin, d.adrelid), '-')
    FROM pg_catalog.pg_attribute a
    JOIN pg_catalog.pg_class c ON c.oid = a.attrelid
    JOIN pg_catalog.pg_namespace n ON n.oid = c.relnamespace
    LEFT JOIN pg_catalog.pg_attrdef d ON d.adrelid = a.attrelid AND d.adnum = a.attnum
    LEFT JOIN pg_catalog.pg_collation co ON co.oid = a.attcollation
    WHERE a.attnum > 0
      AND NOT a.attisdropped
      AND c.relkind IN ('r', 'p', 'v', 'm', 'f')
      AND n.nspname NOT IN ('pg_catalog', 'information_schema', 'hangfire')
      AND n.nspname NOT LIKE 'pg_temp%'
      AND n.nspname NOT LIKE 'pg_toast%'

    UNION ALL

    -- Constraints: primary keys, unique, foreign keys, checks, exclusions. The
    -- definition comes from the catalog, so it is the constraint's meaning and not the
    -- SQL that created it - which is what makes two databases comparable at all.
    --
    -- A CHECK constraint's definition is the CANONICAL one (see the block above): the same
    -- rule written in two shapes is one rule, and comparing the shapes would report a
    -- difference that is not there.
    SELECT 'constraint ' || n.nspname || '.' || c.relname || '.' || con.conname
           || ' ' || COALESCE(
                  (SELECT co.definition
                   FROM canonical_objects co
                   WHERE co.kind = 'constraint'
                     AND co.table_name = c.relname
                     AND co.object_name = con.conname),
                  pg_catalog.pg_get_constraintdef(con.oid))
    FROM pg_catalog.pg_constraint con
    JOIN pg_catalog.pg_class c ON c.oid = con.conrelid
    JOIN pg_catalog.pg_namespace n ON n.oid = c.relnamespace
    WHERE n.nspname NOT IN ('pg_catalog', 'information_schema', 'hangfire')
    AND n.nspname NOT LIKE 'pg_temp%'

    UNION ALL

    -- Indexes. Included even when they back a constraint: the constraint's definition says
    -- WHICH columns, and the index's says HOW they are stored - a unique index and a
    -- plain index over the same columns are different shapes.
    --
    -- The toast indexes are EXCLUDED, and they are excluded because they are noise that
    -- cannot be excluded any other way: a toast index is named after the OID of the table
    -- it belongs to (pg_toast_17077_index), so its name differs between two databases that
    -- have the same schema - the OIDs are assigned in creation order, and the two databases
    -- were not created in the same order. They are PostgreSQL's own storage for large
    -- values, they are never named by a migration, and no application reads them.
    SELECT 'index ' || n.nspname || '.' || c.relname || ' ' || COALESCE(
                  (SELECT co.definition
                   FROM canonical_objects co
                   WHERE co.kind = 'index'
                     AND co.table_name = c.relname
                     AND co.object_name = i2.relname),
                  pg_catalog.pg_get_indexdef(i.indexrelid))
    FROM pg_catalog.pg_index i
    JOIN pg_catalog.pg_class c ON c.oid = i.indrelid
    JOIN pg_catalog.pg_class i2 ON i2.oid = i.indexrelid
    JOIN pg_catalog.pg_namespace n ON n.oid = c.relnamespace
    WHERE n.nspname NOT IN ('pg_catalog', 'information_schema', 'hangfire')
    AND n.nspname NOT LIKE 'pg_temp%'
      AND n.nspname NOT LIKE 'pg_toast%'

    UNION ALL

    -- Views, with their definitions. The refunds view (a view over payments) exists
    -- because a table and a view have the same name in the catalog and different
    -- behaviour, so the definition is what distinguishes them.
    SELECT 'view ' || n.nspname || '.' || c.relname || ' = ' || pg_catalog.pg_get_viewdef(c.oid, true)
    FROM pg_catalog.pg_class c
    JOIN pg_catalog.pg_namespace n ON n.oid = c.relnamespace
    WHERE c.relkind = 'v'
      AND n.nspname NOT IN ('pg_catalog', 'information_schema', 'hangfire')
      AND n.nspname NOT LIKE 'pg_temp%'

    UNION ALL

    -- Triggers. The internal ones - the ones the database makes for a foreign key - are
    -- excluded, because they are described by the foreign key's own definition and their
    -- names are implementation detail.
    SELECT 'trigger ' || n.nspname || '.' || c.relname || '.' || tg.tgname
           || ' = ' || pg_catalog.pg_get_triggerdef(tg.oid)
    FROM pg_catalog.pg_trigger tg
    JOIN pg_catalog.pg_class c ON c.oid = tg.tgrelid
    JOIN pg_catalog.pg_namespace n ON n.oid = c.relnamespace
    WHERE NOT tg.tgisinternal
      AND n.nspname NOT IN ('pg_catalog', 'information_schema', 'hangfire')
      AND n.nspname NOT LIKE 'pg_temp%'

    UNION ALL

    -- Functions and procedures. A function's body is part of the schema: a database can
    -- have the same tables and a different trigger function and still be a different
    -- database.
    SELECT 'function ' || n.nspname || '.' || p.proname
           || ' ' || pg_catalog.pg_get_function_identity_arguments(p.oid)
           || ' = ' || pg_catalog.pg_get_functiondef(p.oid)
    FROM pg_catalog.pg_proc p
    JOIN pg_catalog.pg_namespace n ON n.oid = p.pronamespace
    WHERE n.nspname NOT IN ('pg_catalog', 'information_schema', 'hangfire')
    AND n.nspname NOT LIKE 'pg_temp%'

    UNION ALL

    -- Sequences, with their parameters. A sequence's INCREMENT is part of its meaning.
    SELECT 'sequence ' || n.nspname || '.' || c.relname
           || ' type=' || s.seqtypid::regtype::text
           || ' start=' || s.seqstart
           || ' increment=' || s.seqincrement
           || ' min=' || s.seqmin
           || ' max=' || s.seqmax
           || ' cycle=' || s.seqcycle
    FROM pg_catalog.pg_sequence s
    JOIN pg_catalog.pg_class c ON c.oid = s.seqrelid
    JOIN pg_catalog.pg_namespace n ON n.oid = c.relnamespace
    WHERE n.nspname NOT IN ('pg_catalog', 'information_schema', 'hangfire')
    AND n.nspname NOT LIKE 'pg_temp%'

    UNION ALL

    -- Comments on tables. A comment is not behaviour, but it is documentation that
    -- lives INSIDE the database, and a difference here is a difference.
    SELECT 'comment table ' || n.nspname || '.' || c.relname
           || ' = ' || pg_catalog.obj_description(c.oid, 'pg_class')
    FROM pg_catalog.pg_class c
    JOIN pg_catalog.pg_namespace n ON n.oid = c.relnamespace
    WHERE c.relkind IN ('r', 'p')
      AND pg_catalog.obj_description(c.oid, 'pg_class') IS NOT NULL
      AND n.nspname NOT IN ('pg_catalog', 'information_schema', 'hangfire')
      AND n.nspname NOT LIKE 'pg_temp%'
      AND n.nspname NOT LIKE 'pg_toast%'

    UNION ALL

    -- Comments on columns.
    SELECT 'comment column ' || n.nspname || '.' || c.relname || '.' || a.attname
           || ' = ' || pg_catalog.col_description(a.attrelid, a.attnum)
    FROM pg_catalog.pg_attribute a
    JOIN pg_catalog.pg_class c ON c.oid = a.attrelid
    JOIN pg_catalog.pg_namespace n ON n.oid = c.relnamespace
    WHERE a.attnum > 0
      AND NOT a.attisdropped
      AND pg_catalog.col_description(a.attrelid, a.attnum) IS NOT NULL
      AND n.nspname NOT IN ('pg_catalog', 'information_schema', 'hangfire')
      AND n.nspname NOT LIKE 'pg_temp%'
      AND n.nspname NOT LIKE 'pg_toast%'

)
SELECT line FROM objects ORDER BY line;
