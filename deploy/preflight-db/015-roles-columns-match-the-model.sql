-- Data preconditions, checked BEFORE a new image is pulled.
--
-- The columns of `roles` must be exactly the set the EF model maps. Zero rows means the table
-- is the shape a from-zero build produces.
--
-- WHY THIS EXISTS. Production carried two EXTRA columns - "Level" and "IsDefault" - for a month.
-- The cause was commit f150524f, which edited the ALREADY-APPLIED migration
-- 20260917192136_AddRoleLevelAndSeedProductOwnerManager in place: the version production ran
-- created the PascalCase pair, and the version in the repository creates the snake_case pair. A
-- later migration gave everyone the snake_case pair as well, and the model maps
-- `Level` -> `level` and `IsDefault` -> `is_default`. So production had seven columns where the
-- repository produces five, and nothing said so.
--
-- The two dead columns held STALE values - all zeros and all false - disagreeing with the live
-- columns on four rows and one row respectively. That is what made them safe to drop, and it is
-- also why they are dangerous to keep: a reader who trusts them gets the wrong answer.
--
-- They were dropped on 2026-10-20. This file is here so they cannot come back, and so that the
-- next divergence between production and a from-zero build is NAMED rather than discovered
-- months later.
--
-- The model's set, from RoleConfiguration.cs: id, name, created_at_utc, level, is_default.
--
-- A question, not a gate: a precondition reports, a migration raises. And the runner counts
-- rows carrying a pipe, so every row here is pipe-separated.

BEGIN;
SET TRANSACTION READ ONLY;

SELECT to_regclass('public.roles') IS NOT NULL AS t \gset
\if :t
SELECT 'roles' AS table_name,
       'a column the model does not map' AS violation,
       column_name AS detail
FROM information_schema.columns
WHERE table_schema = 'public'
  AND table_name = 'roles'
  AND column_name NOT IN ('id', 'name', 'created_at_utc', 'level', 'is_default')
UNION ALL
SELECT 'roles' AS table_name,
       'a column the model maps is missing' AS violation,
       m.expected AS detail
FROM (VALUES ('id'), ('name'), ('created_at_utc'), ('level'), ('is_default')) AS m(expected)
WHERE NOT EXISTS (
  SELECT 1
  FROM information_schema.columns c
  WHERE c.table_schema = 'public'
    AND c.table_name = 'roles'
    AND c.column_name = m.expected
)
ORDER BY 3;
\endif

COMMIT;
