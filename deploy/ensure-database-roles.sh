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
# The three roles this script creates:
#
#   fuelflow_app        the application's traffic. Reads and writes the application's
#                       own tables. May NOT change the schema, create a role, create a
#                       database, or bypass row-level security. Not a superuser.
#   fuelflow_migrator   the schema. Owns the tables (a migration alters them), and may
#                       do nothing else. Not a superuser: it cannot read the roles'
#                       password hashes, cannot create a role, and cannot drop the
#                       database.
#   fuelflow_readonly   SELECT only, for the reporting and support queries a person
#                       runs by hand. Optional - it is created only when a password for
#                       it is configured.
#
# The application's connection uses the first, and the migration uses the second
# (deploy/docker-compose.prod.yml names both). Two startup guards keep the separation
# honest: the configuration guard refuses to boot when the two connection strings name
# the same role, and the live catalogue check refuses to serve traffic as a role that
# is a superuser.
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
    -h|--help) sed -n '2,40p' "${BASH_SOURCE[0]}"; exit 0 ;;
    *) echo "unknown argument: $argument" >&2; exit 2 ;;
  esac
done

ENV_FILE="${FUELFLOW_ENV_FILE:-.env}"
CONTAINER="${POSTGRES_CONTAINER:-fuelflow-postgres}"

# The role names are a CONTRACT, not a setting: the compose file names them, the
# application's startup guard compares them, and the CI test asserts them. They are
# spelled out here rather than read from .env so that the three places cannot drift
# apart silently - a role that exists under one name and is looked for under another is
# a deploy that fails with "role does not exist" and nothing that says why.
APP_ROLE="fuelflow_app"
MIGRATOR_ROLE="fuelflow_migrator"
READONLY_ROLE="fuelflow_readonly"

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
for key in POSTGRES_USER POSTGRES_DB POSTGRES_APP_PASSWORD POSTGRES_MIGRATOR_PASSWORD POSTGRES_READONLY_PASSWORD; do
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

# The superuser's own name must not be one of the three roles, or the separation this
# script exists for is a piece of text: `fuelflow_app` would BE the superuser, and the
# application's guard would refuse to start (which is the right outcome, but a deploy
# that dies on the first boot is a bad afternoon - better to say so here, where the
# reason can still be acted on).
case "$POSTGRES_USER" in
  "$APP_ROLE"|"$MIGRATOR_ROLE"|"$READONLY_ROLE")
    die "POSTGRES_USER is '$POSTGRES_USER', which is one of the three roles this script creates. The superuser and the application must be different roles - change POSTGRES_USER in $ENV_FILE." ;;
esac

# ------------------------------------------------------------------------------
# The passwords of the two roles the application needs.
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

# Exported unconditionally: `docker exec -e NAME` copies the value out of this
# process's environment, and a variable that is set but not exported is not in it.
export POSTGRES_APP_PASSWORD POSTGRES_MIGRATOR_PASSWORD

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
    -e POSTGRES_READONLY_PASSWORD \
    "$CONTAINER" psql -U "$POSTGRES_USER" -d "$POSTGRES_DB" \
    -v ON_ERROR_STOP=1 -X -q "$@"
}

# psql as one of the three roles, over the socket. Used for the probes below: they
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
  -v readonly_role="$READONLY_ROLE" <<'SQL'
\getenv app_password POSTGRES_APP_PASSWORD
\getenv migrator_password POSTGRES_MIGRATOR_PASSWORD
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

-- The read-only role, only when a password for it is configured.
SELECT format('CREATE ROLE %I LOGIN PASSWORD %L', :'readonly_role', :'readonly_password')
 WHERE :'readonly_password' <> ''
   AND NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = :'readonly_role') \gexec
SELECT format('ALTER ROLE %I WITH LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOINHERIT NOREPLICATION NOBYPASSRLS PASSWORD %L', :'readonly_role', :'readonly_password')
 WHERE :'readonly_password' <> '' \gexec
SQL

say "$APP_ROLE, $MIGRATOR_ROLE and${READONLY_ENABLED:+ $READONLY_ROLE} exist"

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
  -v readonly_role="$READONLY_ROLE" \
  -v readonly_password="$POSTGRES_READONLY_PASSWORD" \
  -v db="$POSTGRES_DB" <<'SQL'
GRANT CONNECT ON DATABASE :"db" TO :"app_role", :"migrator_role";
-- The read-only role only exists when a password for it is configured, so its grants are
-- conditional: a GRANT to a role that does not exist is an error, and an error here
-- would stop a deploy over a role nobody asked for.
SELECT format('GRANT CONNECT ON DATABASE %I TO %I', :'db', :'readonly_role')
 WHERE :'readonly_password' <> '' \gexec

CREATE SCHEMA IF NOT EXISTS hangfire;
ALTER SCHEMA hangfire OWNER TO :"migrator_role";
GRANT USAGE, CREATE ON SCHEMA hangfire TO :"app_role";
REVOKE ALL ON SCHEMA hangfire FROM PUBLIC;

GRANT USAGE ON SCHEMA public TO :"app_role";
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
# The application may do the application's work: DML on the tables, SELECT on the
# views, and USAGE on the sequences the identity columns draw from. The read-only role
# may read and nothing else.
#
# The default privileges matter as much as the grants above: every table a FUTURE
# migration creates belongs to the migrator, and without these the application would
# not be able to write to it - the schema would be migrated and the application would
# fail on the first query that touches the new table.
# ------------------------------------------------------------------------------
psql_super \
  -v app_role="$APP_ROLE" \
  -v migrator_role="$MIGRATOR_ROLE" \
  -v readonly_role="$READONLY_ROLE" \
  -v readonly_password="$POSTGRES_READONLY_PASSWORD" <<'SQL'
SELECT format('GRANT SELECT, INSERT, UPDATE, DELETE ON TABLE %I.%I TO %I',
              n.nspname, c.relname, :'app_role')
  FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
 WHERE c.relkind IN ('r','p') AND n.nspname IN ('public','hangfire') \gexec

SELECT format('GRANT SELECT ON TABLE %I.%I TO %I', n.nspname, c.relname, :'app_role')
  FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
 WHERE c.relkind IN ('v','m','f') AND n.nspname IN ('public','hangfire') \gexec

SELECT format('GRANT USAGE, SELECT ON SEQUENCE %I.%I TO %I',
              n.nspname, c.relname, :'app_role')
  FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
 WHERE c.relkind = 'S' AND n.nspname IN ('public','hangfire') \gexec

-- The read-only role, when there is one.
SELECT format('GRANT SELECT ON TABLE %I.%I TO %I', n.nspname, c.relname, :'readonly_role')
  FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
 WHERE :'readonly_password' <> ''
   AND c.relkind IN ('r','p','v','m','f') AND n.nspname IN ('public','hangfire') \gexec

SELECT format('GRANT USAGE, SELECT ON SEQUENCE %I.%I TO %I',
              n.nspname, c.relname, :'readonly_role')
  FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
 WHERE :'readonly_password' <> ''
   AND c.relkind = 'S' AND n.nspname IN ('public','hangfire') \gexec

-- What the migrator creates from now on.
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

say "the application's DML, the read-only role's SELECT, and the default privileges for what comes next"

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

psql_super -c "DROP TABLE IF EXISTS public.__fuelflow_role_probe" >/dev/null

# ------------------------------------------------------------------------------
# 7. What the catalogue says about the three roles.
#
# The last word belongs to the catalogue: this is the same query the application's own
# startup guard runs against the role it is connected as.
# ------------------------------------------------------------------------------
echo "== the catalogue says =="
roles_report="$(psql_super -t -A -F' ' -c "
  SELECT rolname, rolsuper, rolcreaterole, rolcreatedb, rolbypassrls, rolcanlogin
    FROM pg_roles WHERE rolname IN ('$APP_ROLE', '$MIGRATOR_ROLE', '$READONLY_ROLE')
   ORDER BY rolname")"
while read -r name super createrole createdb bypassrls login; do
  [[ -z "$name" ]] && continue
  if [[ "$super" == "t" ]]; then
    die "the role '$name' is a superuser. The whole point of this script is that it is not."
  fi
  printf '  %-20s superuser=%s createrole=%s createdb=%s bypassrls=%s login=%s\n' \
    "$name" "$super" "$createrole" "$createdb" "$bypassrls" "$login"
done <<< "$roles_report"

echo "the roles are in place: the application's traffic runs as $APP_ROLE and the schema as $MIGRATOR_ROLE."
