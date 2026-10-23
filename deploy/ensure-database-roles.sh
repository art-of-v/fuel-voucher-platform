#!/usr/bin/env bash
# ------------------------------------------------------------------------------
# The database roles the production stack needs, and the grants that go with them.
#
#     cd ~/FuelFlow/deploy && ./ensure-database-roles.sh
#
# WHAT THIS IS FOR
#
# One PostgreSQL role used to serve both the application's traffic and the schema
# migrations, and that role was the cluster's SUPERUSER - the one role the
# postgres:16-alpine image creates, with the password that sits in .env. The
# consequence was never "an API bug leaks a table"; it was "an API bug owns the
# cluster": read every role's password hash out of pg_authid, create a new superuser,
# drop the database, or write straight into audit_events and voucher_events and
# falsify the trail the money reconciliation depends on. The trail was protected by
# the application's own discipline rather than by the database's privileges.
#
# The four roles this script creates:
#
#   fuelflow_app            the application's traffic. Reads and writes the
#                           application's own tables - EXACTLY the privileges the
#                           matrix in database-privileges.tsv gives it, and nothing
#                           else: the audit and ledger tables are insert-only for it,
#                           the retention tables cannot be deleted by it, and it may
#                           not change the schema, create a role, create a database,
#                           or bypass row-level security. Not a superuser.
#   fuelflow_migrator       the schema. Owns the tables (a migration alters them), and
#                           may do nothing else. Not a superuser: it cannot read the
#                           roles' password hashes, cannot create a role, and cannot
#                           drop the database.
#   fuelflow_maintenance    the destructive end of the data lifecycle. DELETE (and
#                           SELECT) on exactly the tables the nightly retention purge
#                           empties - DataRetentionService connects as this role - so
#                           a defect in the traffic role cannot erase the ledger, the
#                           audit trail or the outbox. It gets no other privilege: it
#                           may not insert or update anything.
#   fuelflow_readonly       SELECT only, for the reporting and support queries a person
#                           runs by hand. Optional - it is created only when a password
#                           for it is configured.
#
# The application's connection uses the first, the migration uses the second and the
# retention job uses the third (deploy/docker-compose.prod.yml names all three). Two
# startup guards keep the separation honest: the configuration guard refuses to boot
# when the connection strings name the same role, and the live catalogue check refuses
# to serve traffic as a role that is a superuser.
#
# THE MATRIX (deploy/database-privileges.tsv) is the single source of truth for the
# app's and maintenance's TABLE grants. This script verifies that every table in the
# public schema is listed in it (an unlisted table is a deploy failure, not a silent
# gap), then applies exactly those grants and revokes everything else. A table a
# migration creates later gets the default privileges below (full DML for the app) so
# the application keeps working between the migration and the matrix update - the PR
# that adds the table adds its matrix row in the same change.
#
# IT IS IDEMPOTENT, and it is safe to run on every deploy: the roles are created if
# they are missing and re-asserted if they are not, the objects the migrator must own
# are transferred to it, and the grants are applied again. Running it twice changes
# nothing, which is what lets the deploy job call it unconditionally.
#
# IT NEEDS THE CLUSTER'S SUPERUSER, because creating a role needs one. The superuser
# is the operator's credential (POSTGRES_USER in .env), not the application's: the
# application never sees it.
# ------------------------------------------------------------------------------
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$SCRIPT_DIR"

# --passwords-only: write the role passwords into the env file and stop.
#
# It exists because the PRODUCTION compose file names the two role passwords, and the
# staging deploy's preflight resolves that compose file against the production env file
# - so the passwords have to be in the env file before the staging deploy runs, not
# only before the production one. This mode does that without touching the database:
# a staging deploy must not depend on the production database being up.
#
# (The alternative - a compose file whose password variable has an empty default - is
# worse: the compose file would load, the application would be recreated with an EMPTY
# password, and the outage would happen AFTER the container was replaced instead of
# before it.)
PASSWORDS_ONLY=0
for argument in "$@"; do
  case "$argument" in
    --passwords-only) PASSWORDS_ONLY=1 ;;
    -h|--help) sed -n '2,63p' "${BASH_SOURCE[0]}"; exit 0 ;;
    *) echo "unknown argument: $argument" >&2; exit 2 ;;
  esac
done

ENV_FILE="${FUELFLOW_ENV_FILE:-.env}"
CONTAINER="${POSTGRES_CONTAINER:-fuelflow-postgres}"

# The role names are a CONTRACT, not a setting: the compose file names them, the
# application's startup guard compares them, and the CI test asserts them. They are
# spelled out here rather than read from .env so that the four places cannot drift
# apart silently - a role that exists under one name and is looked for under another is
# a deploy that fails with "role does not exist" and nothing that says why.
APP_ROLE="fuelflow_app"
MIGRATOR_ROLE="fuelflow_migrator"
MAINTENANCE_ROLE="fuelflow_maintenance"
READONLY_ROLE="fuelflow_readonly"

# The privilege matrix: one row per public table, saying what the application role and
# the maintenance role may do to it. Its absence is a broken checkout, not a deploy.
MATRIX_FILE="$SCRIPT_DIR/database-privileges.tsv"

die() { echo "ERROR: $*" >&2; exit 1; }
say() { echo "  $*"; }

# ------------------------------------------------------------------------------
# The .env, read without executing it.
#
# load_env parses KEY=VALUE lines and never runs the file, which matters because this
# file holds the cluster's superuser password and the roles' passwords: a value with a
# space in it, or a "$(...)" in it, must not become a command.
#
# The ENVIRONMENT wins over the file. The CI test drives this script through the
# environment, and an operator who exports a value for one run means it.
# ------------------------------------------------------------------------------
declare -A preset=()
for key in POSTGRES_USER POSTGRES_DB POSTGRES_APP_PASSWORD POSTGRES_MIGRATOR_PASSWORD POSTGRES_MAINTENANCE_PASSWORD POSTGRES_READONLY_PASSWORD; do
  if [[ -n "${!key:-}" ]]; then
    preset["$key"]="${!key}"
  fi
done

if [[ -f "$ENV_FILE" ]]; then
  # shellcheck source=./load-env.sh
  source "$SCRIPT_DIR/load-env.sh"
  load_env "$ENV_FILE"
fi

for key in "${!preset[@]}"; do
  printf -v "$key" '%s' "${preset[$key]}"
  export "$key"
done

# The message inside ${VAR:?...} is a quoting context of its own: an apostrophe in it
# opens a single-quoted string that swallows the rest of the file. No contractions in
# these two messages.
: "${POSTGRES_USER:?POSTGRES_USER is not set - it names the cluster superuser, and this script needs it}"
: "${POSTGRES_DB:?POSTGRES_DB is not set in $ENV_FILE}"

# The superuser's own name must not be one of the four roles, or the separation this
# script exists for is a piece of text: `fuelflow_app` would BE the superuser, and the
# application's guard would refuse to start (which is the right outcome, but a deploy
# that dies on the first boot is a bad afternoon - better to say so here, where the
# reason can still be acted on).
case "$POSTGRES_USER" in
  "$APP_ROLE"|"$MIGRATOR_ROLE"|"$MAINTENANCE_ROLE"|"$READONLY_ROLE")
    die "POSTGRES_USER is '$POSTGRES_USER', which is one of the four roles this script creates. The superuser and the application must be different roles - change POSTGRES_USER in $ENV_FILE." ;;
esac

# ------------------------------------------------------------------------------
# The passwords of the three roles the application needs.
#
# They are generated when they are missing and written to the env file, because the
# alternative is a deploy that stops at the preflight with "set POSTGRES_APP_PASSWORD
# in .env" the moment this reaches production - and the operator who merges the change
# is not necessarily the operator with access to the box. The generated values are
# printed once, so they can be put in a password manager.
#
# /dev/urandom rather than openssl: this runs unattended on the production box, and
# the kernel's entropy source is always there.
# ------------------------------------------------------------------------------
generate_password() {
  head -c 32 /dev/urandom | base64 | tr -d '\n='
}

ensure_password() {
  local name="$1" value="${!1:-}"
  if [[ -n "$value" ]]; then
    return 0
  fi

  value="$(generate_password)"
  printf -v "$name" '%s' "$value"
  export "$name"

  if [[ -f "$ENV_FILE" && -w "$ENV_FILE" ]]; then
    printf '\n# Added by ensure-database-roles.sh on %s\n%s=%s\n' \
      "$(date -u +%Y-%m-%dT%H:%M:%SZ)" "$name" "$value" >> "$ENV_FILE"
    say "$name: generated and written to $ENV_FILE"
  else
    say "$name: generated (it could NOT be written to $ENV_FILE - put it there by hand, or the compose file will not start the application)"
  fi
}

ensure_password POSTGRES_APP_PASSWORD
ensure_password POSTGRES_MIGRATOR_PASSWORD
ensure_password POSTGRES_MAINTENANCE_PASSWORD

# Exported unconditionally: `docker exec -e NAME` copies the value out of this
# process's environment, and a variable that is set but not exported is not in it.
export POSTGRES_APP_PASSWORD POSTGRES_MIGRATOR_PASSWORD POSTGRES_MAINTENANCE_PASSWORD

# The read-only role is for a person running a query by hand, so it is optional: no
# password configured means no role, and the application does not need it either way.
# It is exported as an empty value rather than left unset, because psql's \getenv
# refuses to run against a variable that does not exist.
export POSTGRES_READONLY_PASSWORD="${POSTGRES_READONLY_PASSWORD:-}"
READONLY_ENABLED=1
if [[ -z "$POSTGRES_READONLY_PASSWORD" ]]; then
  READONLY_ENABLED=0
fi

if [[ "$PASSWORDS_ONLY" == "1" ]]; then
  say "the role passwords are in $ENV_FILE; the database was not touched"
  exit 0
fi

# ------------------------------------------------------------------------------
# The cluster has to be there.
#
# On the box that has been running the stack it always is. On a box that has never run
# it, the stack has to come up first - and the only thing this script needs from it is
# the database, so it starts that one service itself rather than making the deploy fail
# with "the database container is not running" on the very first deploy.
#
# The compose call is made only for the DEFAULT container name: the CI test drives this
# script against a container of its own, and there is no compose file for that one.
# ------------------------------------------------------------------------------
DEFAULT_CONTAINER="fuelflow-postgres"

container_is_running() {
  docker ps --format '{{.Names}}' | grep -qx "$CONTAINER"
}

if ! container_is_running; then
  if [[ "$CONTAINER" == "$DEFAULT_CONTAINER" && -f "$SCRIPT_DIR/docker-compose.prod.yml" ]]; then
    echo "the database is not running yet - starting it"
    # The env file is `.env` and not the variable: the production stack reads its key
    # from `.env`, and the wiring test (StagingQrCipherKeySeparationTests) reads this
    # line the same way - a variable here would hide which file the stack was started
    # with, which is the whole point of that guard.
    docker compose --env-file .env -f "$SCRIPT_DIR/docker-compose.prod.yml" \
      up -d postgres > /dev/null 2>&1 || true
    for _ in $(seq 1 60); do
      container_is_running && break
      sleep 1
    done
  fi
fi

if ! container_is_running; then
  die "the database container '$CONTAINER' is not running. Start it first: docker compose --env-file .env -f docker-compose.prod.yml up -d postgres"
fi

# psql as the cluster's superuser, over the container's unix socket. The socket is
# where the postgres image's pg_hba trusts the local user, so no password travels in
# argv or in the environment of the process list.
psql_super() {
  docker exec -i "$CONTAINER" psql -U "$POSTGRES_USER" -d "$POSTGRES_DB" \
    -v ON_ERROR_STOP=1 -X -q "$@"
}

# The same, for the statements that carry a role's PASSWORD.
#
# `docker exec -e NAME` (with no value) copies that variable out of THIS process's
# environment into the container's, so the password itself never appears in the
# command line - where every local account could read it with `ps` while the deploy
# runs. psql reads it from the environment with \getenv, and the SQL that uses it
# carries the VARIABLE, not the value.
psql_super_with_passwords() {
  docker exec -i \
    -e POSTGRES_APP_PASSWORD \
    -e POSTGRES_MIGRATOR_PASSWORD \
    -e POSTGRES_MAINTENANCE_PASSWORD \
    -e POSTGRES_READONLY_PASSWORD \
    "$CONTAINER" psql -U "$POSTGRES_USER" -d "$POSTGRES_DB" \
    -v ON_ERROR_STOP=1 -X -q "$@"
}

# psql as one of the roles, over the socket. Used for the probes below: they
# ask what a role may do, and a role's privileges are the same however it logged in.
psql_as() {
  local role="$1"; shift
  docker exec -i "$CONTAINER" psql -U "$role" -d "$POSTGRES_DB" \
    -v ON_ERROR_STOP=1 -X -q "$@"
}

echo "== the database roles =="

# ------------------------------------------------------------------------------
# 1. The roles themselves.
#
# CREATE ROLE is guarded by a NOT EXISTS query and \gexec, because CREATE ROLE has no
# IF NOT EXISTS of its own and the script has to run on every deploy. ALTER ROLE then
# re-asserts the attributes and the password, so a role that was promoted by hand goes
# back to being an ordinary login the next time this runs.
#
# NOINHERIT is on all three: a role that inherits another role's privileges can be
# promoted without anybody touching the database - the app role would quietly pick up
# the superuser's privileges through a membership somebody granted. Set the privileges
# on the role itself, not through an inheritance chain.
# ------------------------------------------------------------------------------
psql_super_with_passwords \
  -v app_role="$APP_ROLE" \
  -v migrator_role="$MIGRATOR_ROLE" \
  -v maintenance_role="$MAINTENANCE_ROLE" \
  -v readonly_role="$READONLY_ROLE" <<'SQL'
\getenv app_password POSTGRES_APP_PASSWORD
\getenv migrator_password POSTGRES_MIGRATOR_PASSWORD
\getenv maintenance_password POSTGRES_MAINTENANCE_PASSWORD
\getenv readonly_password POSTGRES_READONLY_PASSWORD

-- The application's traffic role.
SELECT format('CREATE ROLE %I LOGIN PASSWORD %L', :'app_role', :'app_password')
 WHERE NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = :'app_role') \gexec
ALTER ROLE :"app_role" WITH LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOINHERIT
  NOREPLICATION NOBYPASSRLS PASSWORD :'app_password';

-- The schema role.
SELECT format('CREATE ROLE %I LOGIN PASSWORD %L', :'migrator_role', :'migrator_password')
 WHERE NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = :'migrator_role') \gexec
ALTER ROLE :"migrator_role" WITH LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOINHERIT
  NOREPLICATION NOBYPASSRLS PASSWORD :'migrator_password';

-- The maintenance role: the destructive end of the data lifecycle. The nightly
-- retention purge connects as it, and it holds nothing but SELECT and DELETE on the
-- tables the matrix hands it - it may never insert or update a row.
SELECT format('CREATE ROLE %I LOGIN PASSWORD %L', :'maintenance_role', :'maintenance_password')
 WHERE NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = :'maintenance_role') \gexec
ALTER ROLE :"maintenance_role" WITH LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOINHERIT
  NOREPLICATION NOBYPASSRLS PASSWORD :'maintenance_password';

-- The read-only role, only when a password for it is configured.
SELECT format('CREATE ROLE %I LOGIN PASSWORD %L', :'readonly_role', :'readonly_password')
 WHERE :'readonly_password' <> ''
   AND NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = :'readonly_role') \gexec
SELECT format('ALTER ROLE %I WITH LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOINHERIT NOREPLICATION NOBYPASSRLS PASSWORD %L', :'readonly_role', :'readonly_password')
 WHERE :'readonly_password' <> '' \gexec
SQL

say "$APP_ROLE, $MIGRATOR_ROLE, $MAINTENANCE_ROLE and${READONLY_ENABLED:+ $READONLY_ROLE} exist"

# ------------------------------------------------------------------------------
# 2. The schemas.
#
# The migrator needs CREATE on the public schema (a migration creates tables there)
# and the application needs USAGE and nothing more: on PostgreSQL 15 and later PUBLIC
# has no CREATE on public, and REVOKE makes that explicit on an older cluster too.
#
# The hangfire schema is Hangfire's own - the job queue's tables live in it, not in
# public. The application may create objects in it (Hangfire creates its own tables
# when it starts), and nowhere else.
# ------------------------------------------------------------------------------
psql_super \
  -v app_role="$APP_ROLE" \
  -v migrator_role="$MIGRATOR_ROLE" \
  -v maintenance_role="$MAINTENANCE_ROLE" \
  -v readonly_role="$READONLY_ROLE" \
  -v readonly_password="$POSTGRES_READONLY_PASSWORD" \
  -v db="$POSTGRES_DB" <<'SQL'
GRANT CONNECT ON DATABASE :"db" TO :"app_role", :"migrator_role", :"maintenance_role";
-- The read-only role only exists when a password for it is configured, so its grants are
-- conditional: a GRANT to a role that does not exist is an error, and an error here
-- would stop a deploy over a role nobody asked for.
SELECT format('GRANT CONNECT ON DATABASE %I TO %I', :'db', :'readonly_role')
 WHERE :'readonly_password' <> '' \gexec

CREATE SCHEMA IF NOT EXISTS hangfire;
ALTER SCHEMA hangfire OWNER TO :"migrator_role";
GRANT USAGE, CREATE ON SCHEMA hangfire TO :"app_role";
REVOKE ALL ON SCHEMA hangfire FROM PUBLIC;

GRANT USAGE ON SCHEMA public TO :"app_role", :"maintenance_role";
GRANT USAGE, CREATE ON SCHEMA public TO :"migrator_role";
REVOKE CREATE ON SCHEMA public FROM PUBLIC;
SQL

say "the schemas are set: the migrator may create objects in public, the application may not"

# ------------------------------------------------------------------------------
# 3. The migrator owns the schema's objects.
#
# A migration ALTERs the tables it finds, and only the owner may do that. The tables
# were created by the superuser, so they are transferred to the migrator - one ALTER
# per object, generated from the catalogue and executed by \gexec.
#
# The sequences that belong to a column (the identity columns) are NOT transferred
# here: they follow their table, and PostgreSQL refuses to change the owner of one
# that is attached to a column.
# ------------------------------------------------------------------------------
psql_super -v migrator_role="$MIGRATOR_ROLE" <<'SQL'
SELECT format('ALTER TABLE %I.%I OWNER TO %I', n.nspname, c.relname, :'migrator_role')
  FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
 WHERE c.relkind IN ('r','p')
   AND n.nspname IN ('public','hangfire')
   AND c.relowner <> (SELECT oid FROM pg_roles WHERE rolname = :'migrator_role') \gexec

SELECT format('ALTER VIEW %I.%I OWNER TO %I', n.nspname, c.relname, :'migrator_role')
  FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
 WHERE c.relkind = 'v'
   AND n.nspname IN ('public','hangfire')
   AND c.relowner <> (SELECT oid FROM pg_roles WHERE rolname = :'migrator_role') \gexec

SELECT format('ALTER MATERIALIZED VIEW %I.%I OWNER TO %I', n.nspname, c.relname, :'migrator_role')
  FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
 WHERE c.relkind = 'm'
   AND n.nspname IN ('public','hangfire')
   AND c.relowner <> (SELECT oid FROM pg_roles WHERE rolname = :'migrator_role') \gexec

SELECT format('ALTER SEQUENCE %I.%I OWNER TO %I', n.nspname, c.relname, :'migrator_role')
  FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
 WHERE c.relkind = 'S'
   AND n.nspname IN ('public','hangfire')
   AND c.relowner <> (SELECT oid FROM pg_roles WHERE rolname = :'migrator_role')
   AND NOT EXISTS (
     SELECT 1 FROM pg_depend d
      WHERE d.classid = 'pg_class'::regclass AND d.objid = c.oid
        AND d.deptype IN ('a','i')) \gexec
SQL

say "the objects in public and hangfire belong to the migrator"

# ------------------------------------------------------------------------------
# 4. The grants.
#
# THE MATRIX decides. database-privileges.tsv names every relation in the public
# schema and the privileges the application role and the maintenance role hold on it:
# the audit and ledger tables are insert-only for the app, the retention tables are
# undeletable by it, and the maintenance role holds exactly the destructive half of
# the retention purge - nothing else.
#
# Three properties, in order:
#
#   - COMPLETE: every table and view in public must be listed in the matrix. An
#     unlisted relation is a deploy failure, not a silent gap - the alternative is a
#     table nobody classified keeping whatever the default privileges gave it.
#     The objects that belong to an EXTENSION are exempt: PostgreSQL creates those
#     itself (pg_stat_statements puts its two views in public), they are not ours to
#     classify, and what PostgreSQL itself grants on them is PostgreSQL's business,
#     not the matrix's. Listing them would also tie the matrix to the server version.
#   - EXACT: the script revokes everything it is about to grant before granting, so a
#     privilege somebody added by hand disappears on the next deploy instead of
#     hiding behind an idempotent-looking "it ran fine".
#   - FORWARD-SAFE: a matrix row for a relation that does not exist yet (a fresh
#     cluster, the CI test database) is skipped - the default privileges at the bottom
#     cover a table between its migration and its matrix row.
#
# The default privileges still matter as much as the matrix: every table a FUTURE
# migration creates belongs to the migrator, and without them the application would
# not be able to write to it in the window before the matrix row lands.
# ------------------------------------------------------------------------------
if [[ ! -f "$MATRIX_FILE" ]]; then
  die "database-privileges.tsv is missing next to this script - it is the source of truth for the app's grants"
fi

# A probe table left behind by a crashed earlier run would otherwise look like an
# unlisted relation and stop the deploy with a confusing message.
psql_super -c "DROP TABLE IF EXISTS public.__fuelflow_role_probe" >/dev/null

existing_tables="$(psql_super -t -A -c "
  SELECT nspname || '.' || relname
    FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
   WHERE c.relkind IN ('r','p','v','m','f') AND n.nspname = 'public'
     AND NOT EXISTS (
       SELECT 1 FROM pg_depend d
        WHERE d.classid = 'pg_class'::regclass AND d.objid = c.oid
          AND d.deptype = 'e' AND d.refclassid = 'pg_extension'::regclass)
   ORDER BY 1")"
# comm compares under the locale's collation, not the C order the two sorts above
# produce. On 2026-10-23 the production deploy failed here with "comm: file 2 is not in
# sorted order" because the box's en_US locale ordered the mixed-case
# __EFMigrationsHistory differently from C; both inputs were sorted, just not the way
# comm was reading them. Pin comm to the same locale as the sorts.
unlisted="$(LC_ALL=C comm -23 \
  <(printf '%s\n' "$existing_tables" | awk 'NF' | LC_ALL=C sort) \
  <(awk -F'\t' '!/^#/ && $1 == "public" && NF >= 4 { print $1 "." $2 }' "$MATRIX_FILE" | LC_ALL=C sort -u))"
if [[ -n "$unlisted" ]]; then
  die "public relations not listed in database-privileges.tsv - add a row for each, or the application loses access to them: $unlisted"
fi

# REVOKE ALL ON TABLE clears the TABLE-level privileges, but a column-level grant
# (GRANT UPDATE (processed) ...) lives in its own place and survives it. So the
# column-level UPDATE grants are revoked first, over every column of every public
# relation - otherwise a column the matrix no longer names would keep its privilege
# forever, and the matrix would stop being the whole truth.
psql_super \
  -v app_role="$APP_ROLE" \
  -v maint_role="$MAINTENANCE_ROLE" <<'SQL'
SELECT format('REVOKE UPDATE (%s) ON TABLE %I.%I FROM %I',
              (SELECT string_agg(a.attname, ', ' ORDER BY a.attname)
                 FROM pg_attribute a
                WHERE a.attrelid = c.oid AND a.attnum > 0 AND NOT a.attisdropped),
              n.nspname, c.relname, :'app_role')
  FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
 WHERE c.relkind IN ('r','p','v','m') AND n.nspname = 'public'
   AND EXISTS (SELECT 1 FROM pg_attribute a
                WHERE a.attrelid = c.oid AND a.attnum > 0 AND NOT a.attisdropped) \gexec

SELECT format('REVOKE UPDATE (%s) ON TABLE %I.%I FROM %I',
              (SELECT string_agg(a.attname, ', ' ORDER BY a.attname)
                 FROM pg_attribute a
                WHERE a.attrelid = c.oid AND a.attnum > 0 AND NOT a.attisdropped),
              n.nspname, c.relname, :'maint_role')
  FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
 WHERE c.relkind IN ('r','p','v','m') AND n.nspname = 'public'
   AND EXISTS (SELECT 1 FROM pg_attribute a
                WHERE a.attrelid = c.oid AND a.attnum > 0 AND NOT a.attisdropped) \gexec
SQL

# Turn the matrix into SQL. The privilege letters: S=SELECT, I=INSERT, U=UPDATE,
# D=DELETE; U(col,...) narrows the update to those columns; "-" means nothing at all.
# Rows for relations that do not exist yet are skipped (forward-safety, above).
awk -F'\t' \
    -v app_role="$APP_ROLE" \
    -v maint_role="$MAINTENANCE_ROLE" \
    -v live_tables="$existing_tables" '
  function emit_privs(priv, role, qname,    base, cols, out) {
    base = priv; cols = ""
    if (match(priv, /\+U\(/)) {
      base = substr(priv, 1, RSTART - 1)
      cols = substr(priv, RSTART + 3); sub(/\)$/, "", cols)
    }
    out = ""
    if (index(base, "S")) out = out "SELECT, "
    if (index(base, "I")) out = out "INSERT, "
    if (index(base, "D")) out = out "DELETE, "
    if (index(base, "U") && cols == "") out = out "UPDATE, "
    if (out != "") { sub(/, $/, "", out); print "GRANT " out " ON TABLE " qname " TO " role ";" }
    if (cols != "") print "GRANT UPDATE (" cols ") ON TABLE " qname " TO " role ";"
  }
  BEGIN {
    n = split(live_tables, arr, "\n")
    for (i = 1; i <= n; i++) if (arr[i] != "") exists[arr[i]] = 1
  }
  /^#/ || NF < 4 { next }
  {
    qname = $1 "." $2
    if (!(qname in exists)) next
    print "REVOKE ALL ON TABLE " qname " FROM " app_role ";"
    print "REVOKE ALL ON TABLE " qname " FROM " maint_role ";"
    if ($3 != "-") emit_privs($3, app_role, qname)
    if ($4 != "-") emit_privs($4, maint_role, qname)
  }' "$MATRIX_FILE" | psql_super

# hangfire stays Hangfire's own: whatever the job queue creates in its schema keeps
# the full DML it needs, by name rather than by matrix row.
psql_super -v app_role="$APP_ROLE" <<'SQL'
SELECT format('GRANT SELECT, INSERT, UPDATE, DELETE ON TABLE %I.%I TO %I',
              n.nspname, c.relname, :'app_role')
  FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
 WHERE c.relkind IN ('r','p') AND n.nspname = 'hangfire' \gexec

SELECT format('GRANT SELECT ON TABLE %I.%I TO %I', n.nspname, c.relname, :'app_role')
  FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
 WHERE c.relkind IN ('v','m','f') AND n.nspname = 'hangfire' \gexec
SQL

# The sequences the identity columns draw from, and the read-only role, as before.
# The read-only role reads the schema's own relations; the extension's objects are
# left out - they are PostgreSQL's, not ours, and the role has no business being
# handed them by name.
psql_super \
  -v app_role="$APP_ROLE" \
  -v readonly_role="$READONLY_ROLE" \
  -v readonly_password="$POSTGRES_READONLY_PASSWORD" <<'SQL'
SELECT format('GRANT USAGE, SELECT ON SEQUENCE %I.%I TO %I',
              n.nspname, c.relname, :'app_role')
  FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
 WHERE c.relkind = 'S' AND n.nspname IN ('public','hangfire') \gexec

SELECT format('GRANT SELECT ON TABLE %I.%I TO %I', n.nspname, c.relname, :'readonly_role')
  FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
 WHERE :'readonly_password' <> ''
   AND c.relkind IN ('r','p','v','m','f') AND n.nspname IN ('public','hangfire')
   AND NOT EXISTS (
     SELECT 1 FROM pg_depend d
      WHERE d.classid = 'pg_class'::regclass AND d.objid = c.oid
        AND d.deptype = 'e' AND d.refclassid = 'pg_extension'::regclass) \gexec

SELECT format('GRANT USAGE, SELECT ON SEQUENCE %I.%I TO %I',
              n.nspname, c.relname, :'readonly_role')
  FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
 WHERE :'readonly_password' <> ''
   AND c.relkind = 'S' AND n.nspname IN ('public','hangfire') \gexec
SQL

# What the migrator creates from now on: the application keeps full DML until its
# matrix row lands in the same change that adds the table. The maintenance role gets
# NOTHING by default - a table the retention purge does not know about must not
# quietly become deletable.
psql_super \
  -v app_role="$APP_ROLE" \
  -v migrator_role="$MIGRATOR_ROLE" \
  -v readonly_role="$READONLY_ROLE" \
  -v readonly_password="$POSTGRES_READONLY_PASSWORD" <<'SQL'
ALTER DEFAULT PRIVILEGES FOR ROLE :"migrator_role" IN SCHEMA public
  GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO :"app_role";
ALTER DEFAULT PRIVILEGES FOR ROLE :"migrator_role" IN SCHEMA public
  GRANT USAGE, SELECT ON SEQUENCES TO :"app_role";
ALTER DEFAULT PRIVILEGES FOR ROLE :"migrator_role" IN SCHEMA hangfire
  GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO :"app_role";
SELECT format('ALTER DEFAULT PRIVILEGES FOR ROLE %I IN SCHEMA public GRANT SELECT ON TABLES TO %I',
              :'migrator_role', :'readonly_role')
 WHERE :'readonly_password' <> '' \gexec
SELECT format('ALTER DEFAULT PRIVILEGES FOR ROLE %I IN SCHEMA public GRANT USAGE, SELECT ON SEQUENCES TO %I',
              :'migrator_role', :'readonly_role')
 WHERE :'readonly_password' <> '' \gexec
SQL

say "the matrix grants, the read-only role's SELECT, and the default privileges for what comes next"

# ------------------------------------------------------------------------------
# 5. The extensions the migrations need.
#
# Two migrations create an extension: pg_stat_statements (the query statistics) and
# btree_gist (the exclusion constraint on the pricing windows). Creating an extension
# that is not marked "trusted" needs a superuser, and a migration runs as the
# migrator - so on a database built from zero by the migrator alone, those two
# migrations would fail.
#
# The trick is that "CREATE EXTENSION IF NOT EXISTS" on an extension that already
# exists is a NOTICE and a skip, and the check for "already exists" happens BEFORE
# the privilege check. So the superuser creates them here, once, and the migration's
# own statement is then a no-op that the migrator is allowed to run.
# ------------------------------------------------------------------------------
psql_super <<'SQL'
CREATE EXTENSION IF NOT EXISTS pg_stat_statements;
CREATE EXTENSION IF NOT EXISTS btree_gist;
SQL

say "the extensions the migrations create are in place (pg_stat_statements, btree_gist)"

# ------------------------------------------------------------------------------
# 6. What the roles may actually do.
#
# A script that says it created the roles proves nothing: it printed what it intended
# to do. These probes ASK the database, as each role, and the run fails if the
# separation is not the separation this script claims.
#
# The probe table is created by the MIGRATOR, which is also what proves the default
# privileges work: a table the migrator creates is granted to the application without
# anybody granting it by hand, and that is exactly what a future migration's table will
# get.
# ------------------------------------------------------------------------------
echo "== the probes =="

psql_super -c "DROP TABLE IF EXISTS public.__fuelflow_role_probe" >/dev/null
psql_as "$MIGRATOR_ROLE" -c "CREATE TABLE public.__fuelflow_role_probe(id integer PRIMARY KEY, note text)" >/dev/null

# The application may do the application's work.
if ! psql_as "$APP_ROLE" -c "INSERT INTO public.__fuelflow_role_probe(id, note) VALUES (1, 'written by the application role'); SELECT count(*) FROM public.__fuelflow_role_probe; DELETE FROM public.__fuelflow_role_probe;" >/dev/null 2>&1; then
  die "the application role cannot write to the application's own tables. It has no DML on them, so the application would fail on its first query."
fi
say "the application role may read and write the application's tables"

# The application may NOT change the schema.
if psql_as "$APP_ROLE" -c "CREATE TABLE public.__fuelflow_role_probe_two(id integer)" >/dev/null 2>&1; then
  psql_super -c "DROP TABLE IF EXISTS public.__fuelflow_role_probe_two" >/dev/null 2>&1 || true
  die "the application role may create tables in the public schema. It must not: the schema belongs to the migration role."
fi
say "the application role may not create tables in the public schema"

if psql_as "$APP_ROLE" -c "ALTER TABLE public.__fuelflow_role_probe ADD COLUMN extra text" >/dev/null 2>&1; then
  die "the application role may alter the application's own tables. It must not: the schema belongs to the migration role."
fi
say "the application role may not alter the tables"

# The application may NOT own the cluster.
if psql_as "$APP_ROLE" -c "CREATE ROLE probe_role LOGIN" >/dev/null 2>&1; then
  psql_super -c "DROP ROLE IF EXISTS probe_role" >/dev/null 2>&1 || true
  die "the application role may create roles. It must not: a role that can create a role can create itself a superuser."
fi
say "the application role may not create roles"

if psql_as "$APP_ROLE" -c "CREATE DATABASE probe_database" >/dev/null 2>&1; then
  psql_super -c "DROP DATABASE IF EXISTS probe_database" >/dev/null 2>&1 || true
  die "the application role may create databases. It must not: another database on this cluster is somewhere else to read."
fi
say "the application role may not create databases"

# The read-only role may read and nothing else.
if [[ "$READONLY_ENABLED" == "1" ]]; then
  if ! psql_as "$READONLY_ROLE" -c "SELECT count(*) FROM public.__fuelflow_role_probe" >/dev/null 2>&1; then
    die "the read-only role cannot read the application's tables."
  fi
  if psql_as "$READONLY_ROLE" -c "INSERT INTO public.__fuelflow_role_probe(id) VALUES (2)" >/dev/null 2>&1; then
    die "the read-only role may write. It must not: a read-only role that writes is not read-only."
  fi
  say "the read-only role may read and may not write"
fi

# ------------------------------------------------------------------------------
# The matrix, asked for real. The probes below only mean something when the relation
# the matrix classifies exists: on a fresh cluster (or the CI test database) the
# migration has not created the schema yet, and the CI test probes the same
# properties against stub tables of its own. `WHERE false` keeps every probe a
# privilege check and nothing more - no row is read, written or locked.
# ------------------------------------------------------------------------------
have_relation() {
  [[ -n "$(psql_super -t -A -c "SELECT to_regclass('$1')" 2>/dev/null)" ]]
}

if have_relation public.voucher_events; then
  if psql_as "$APP_ROLE" -c "UPDATE public.voucher_events SET event_type = event_type WHERE false" >/dev/null 2>&1; then
    die "the application role may UPDATE voucher_events. The voucher audit trail is insert-only for it - database-privileges.tsv says SI."
  fi
  if psql_as "$APP_ROLE" -c "DELETE FROM public.voucher_events WHERE false" >/dev/null 2>&1; then
    die "the application role may DELETE from voucher_events. A redemption nobody can prove happened is a sale nobody has to honour."
  fi
  say "the application role may not rewrite or delete voucher_events"
else
  say "voucher_events does not exist yet - the CI test probes this against a stub table"
fi

if have_relation public.payments; then
  if psql_as "$APP_ROLE" -c "DELETE FROM public.payments WHERE false" >/dev/null 2>&1; then
    die "the application role may DELETE from payments. A payment a bug can erase is a payment the reconciliation cannot trust."
  fi
  say "the application role may not delete payments"
else
  say "payments does not exist yet - the CI test probes this against a stub table"
fi

if have_relation public.outbox_events; then
  # EF Core's Update(entity) writes EVERY column of the row: the dispatcher's own save is
  # "UPDATE outbox_events SET created_at_utc = ..., event_type = ..., payload = ..., processed =
  # ..., processed_at_utc = ...". A grant on two columns refuses that save, and the outbox stops
  # closing - so the grant covers the row, and the row stays undeletable for the traffic role.
  if ! psql_as "$APP_ROLE" -c "UPDATE public.outbox_events SET event_type = event_type WHERE false" >/dev/null 2>&1; then
    die "the application role may not UPDATE outbox_events. The dispatcher closes an event by updating the whole row - database-privileges.tsv grants SIU."
  fi
  if psql_as "$APP_ROLE" -c "DELETE FROM public.outbox_events WHERE false" >/dev/null 2>&1; then
    die "the application role may DELETE from outbox_events. The purge belongs to the maintenance role."
  fi
  say "the application role may close an outbox event and may not delete it"
  if ! psql_as "$MAINTENANCE_ROLE" -c "DELETE FROM public.outbox_events WHERE false" >/dev/null 2>&1; then
    die "the maintenance role may not DELETE from outbox_events. The retention purge would delete nothing, night after night."
  fi
  if psql_as "$MAINTENANCE_ROLE" -c "INSERT INTO public.outbox_events SELECT * FROM public.outbox_events WHERE false" >/dev/null 2>&1; then
    die "the maintenance role may INSERT into outbox_events. It holds the destructive half of the lifecycle only - database-privileges.tsv says SD for it."
  fi
  say "the maintenance role may purge the outbox and may not write it"
else
  say "outbox_events does not exist yet - the CI test probes this against a stub table"
fi

if have_relation public.webhook_events; then
  # The recorder settles a delivery with a tracked read-modify-write and claims one with a
  # conditional UPDATE - both write only the columns the matrix names, so the grant may be the
  # narrow one. The body exactly as received is the evidence, and it is not one of them.
  if ! psql_as "$APP_ROLE" -c "UPDATE public.webhook_events SET status = 'Processed', processed_at_utc = now() WHERE false" >/dev/null 2>&1; then
    die "the application role may not settle a webhook delivery. The recorder closes a delivery exactly this way - database-privileges.tsv grants U(status,processed_at_utc,processing_started_at_utc,error,attempts)."
  fi
  if psql_as "$APP_ROLE" -c "UPDATE public.webhook_events SET payload = payload WHERE false" >/dev/null 2>&1; then
    die "the application role may UPDATE webhook_events.payload. The body exactly as received is the evidence the delivery is worth - database-privileges.tsv grants the five outcome columns and nothing else."
  fi
  say "the application role may settle a webhook delivery and may not rewrite it"
else
  say "webhook_events does not exist yet - the CI test probes this against a stub table"
fi

if have_relation public.provider_event_outbox; then
  if psql_as "$APP_ROLE" -c "UPDATE public.provider_event_outbox SET payload = payload WHERE false" >/dev/null 2>&1; then
    die "the application role may UPDATE provider_event_outbox. The admin change audit trail is insert-only for it - database-privileges.tsv says SI."
  fi
  if psql_as "$APP_ROLE" -c "DELETE FROM public.provider_event_outbox WHERE false" >/dev/null 2>&1; then
    die "the application role may DELETE from provider_event_outbox. The audit purge belongs to the maintenance role."
  fi
  if ! psql_as "$MAINTENANCE_ROLE" -c "SELECT count(*) FROM public.provider_event_outbox" >/dev/null 2>&1; then
    die "the maintenance role may not SELECT from provider_event_outbox - it cannot age the audit trail without reading it."
  fi
  say "the audit trail is insert-only for the app, purged by the maintenance role"
else
  say "provider_event_outbox does not exist yet - the CI test probes this against a stub table"
fi

psql_super -c "DROP TABLE IF EXISTS public.__fuelflow_role_probe" >/dev/null

# ------------------------------------------------------------------------------
# 7. What the catalogue says about the four roles.
#
# The last word belongs to the catalogue: this is the same query the application's own
# startup guard runs against the role it is connected as.
# ------------------------------------------------------------------------------
echo "== the catalogue says =="
roles_report="$(psql_super -t -A -F' ' -c "
  SELECT rolname, rolsuper, rolcreaterole, rolcreatedb, rolbypassrls, rolcanlogin
    FROM pg_roles WHERE rolname IN ('$APP_ROLE', '$MIGRATOR_ROLE', '$MAINTENANCE_ROLE', '$READONLY_ROLE')
   ORDER BY rolname")"
while read -r name super createrole createdb bypassrls login; do
  [[ -z "$name" ]] && continue
  if [[ "$super" == "t" ]]; then
    die "the role '$name' is a superuser. The whole point of this script is that it is not."
  fi
  printf '  %-20s superuser=%s createrole=%s createdb=%s bypassrls=%s login=%s\n' \
    "$name" "$super" "$createrole" "$createdb" "$bypassrls" "$login"
done <<< "$roles_report"

echo "the roles are in place: the application's traffic runs as $APP_ROLE, the schema as $MIGRATOR_ROLE, and the retention purge as $MAINTENANCE_ROLE."
