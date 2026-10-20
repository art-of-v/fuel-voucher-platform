-- Data preconditions, checked BEFORE a new image is pulled.
--
-- Every `MigrationId` must be `^<14 digits>_<name>$`, and no two rows may carry the same id.
-- Zero rows means the history is well formed.
--
-- WHY THIS EXISTS. Production carried one row whose id had a LEADING SPACE -
-- " 20260917192136_AddRoleLevelAndSeedProductOwnerManager" - alongside the correctly-spelled
-- one. It described a migration that was never applied under that name, and it was INVISIBLE to
-- the CI gate: deploy/check-migration-history.sh filters both sides with
-- `grep -E '^[0-9]{14}_'`, so an id that does not start with a digit was dropped before the
-- comparison, and the gate reported "173 ids checked" when the table held 174 rows. A gate that
-- cannot see the shape of what it is checking is not a gate.
--
-- The row was deleted on 2026-10-20, and the gate now reports "174 ids checked, 8 compensated
-- for" against production's own history. This file is here so that a malformed id cannot appear
-- again without anyone being told.
--
-- A question, not a gate: a precondition reports, a migration raises. And the runner counts
-- rows carrying a pipe, so every row here is pipe-separated.

BEGIN;
SET TRANSACTION READ ONLY;

SELECT to_regclass('public."__EFMigrationsHistory"') IS NOT NULL AS t \gset
\if :t
SELECT '__EFMigrationsHistory' AS table_name,
       'a migration id is malformed' AS violation,
       '[' || "MigrationId" || ']' AS detail
FROM public."__EFMigrationsHistory"
WHERE "MigrationId" !~ '^[0-9]{14}_[A-Za-z0-9_]+$'
UNION ALL
SELECT '__EFMigrationsHistory' AS table_name,
       'a migration id is recorded twice' AS violation,
       '[' || "MigrationId" || ']' AS detail
FROM public."__EFMigrationsHistory"
GROUP BY "MigrationId"
HAVING count(*) > 1
ORDER BY 3;
\endif

COMMIT;
