#!/usr/bin/env bash
# ------------------------------------------------------------------------------
# Tests deploy/ensure-database-roles.sh - the script that gives the production
# database its three roles.
#
# WHY THIS EXISTS
#
# The finding this script closes is S-17: one PostgreSQL role served both the
# application's traffic and the schema migrations, and that role was the cluster's
# SUPERUSER. The consequence was never "an API bug leaks a table" - it was "an API bug
# owns the cluster": read every role's password hash out of pg_authid, create a new
# superuser, drop the database, or write straight into the audit trail the money
# reconciliation depends on.
#
# A script that SAYS it created the roles proves nothing - it printed what it intended
# to do. So this test asks the database, as each role, what that role may do:
#
#   - the application role may read and write the application's own tables;
#   - the application role may NOT create a table, alter a table, create a role, or
#     create a database;
#   - the migration role may change the schema;
#   - the read-only role may read and may not write;
#   - none of the three is a superuser;
#   - the script runs twice in a row without changing anything.
#
# It also pins the two PostgreSQL facts the design leans on, because both were checked
# by hand and neither is obvious:
#
#   - "CREATE EXTENSION IF NOT EXISTS" on an extension that ALREADY exists is a notice
#     and a skip, and the check happens BEFORE the privilege check - so a non-superuser
#     may run the statement a migration carries, as long as the extension is already
#     there. That is why the script creates the two extensions the migrations need.
#   - a non-superuser may NOT create an extension that does not exist yet, which is
#     why the script cannot leave that to the migration.
#
# A container is needed for a real database, so the test uses one when Docker is
# available and says so plainly when it is not - a skipped test is not a passing test.
# ------------------------------------------------------------------------------
set -uo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
SCRIPT="$SCRIPT_DIR/ensure-database-roles.sh"

pass=0
fail=0
skip=0
check() {
  local what="$1" expected="$2" actual="$3"
  if [[ "$expected" == "$actual" ]]; then
    echo "  PASS  $what"; pass=$((pass + 1))
  else
    echo "  FAIL  $what (expected '$expected', got '$actual')"; fail=$((fail + 1))
  fi
}
check_contains() {
  local what="$1" needle="$2" haystack="$3"
  if [[ "$haystack" == *"$needle"* ]]; then
    echo "  PASS  $what"; pass=$((pass + 1))
  else
    echo "  FAIL  $what (output did not contain '$needle')"; fail=$((fail + 1))
  fi
}

echo "=== the script exists, is executable, and parses ==="
if [[ -x "$SCRIPT" ]]; then
  echo "  PASS  ensure-database-roles.sh is executable"; pass=$((pass + 1))
else
  echo "  FAIL  ensure-database-roles.sh is not executable"; fail=$((fail + 1))
fi
if bash -n "$SCRIPT" 2>/dev/null; then
  echo "  PASS  ensure-database-roles.sh parses"; pass=$((pass + 1))
else
  echo "  FAIL  ensure-database-roles.sh does not parse"; fail=$((fail + 1))
fi

echo "=== it refuses to run without the database ==="
# A script that runs against nothing and prints "the roles are in place" is worse than
# one that stops: the next deploy would find no roles and the application would not
# start, and the log would say everything was fine.
out="$(FUELFLOW_ENV_FILE=/dev/null POSTGRES_CONTAINER=fuelflow-no-such-container \
  POSTGRES_USER=postgres POSTGRES_DB=fuelflow \
  bash "$SCRIPT" 2>&1)"; rc=$?
if [[ "$rc" -ne 0 ]]; then
  echo "  PASS  it exits non-zero when the database is not there"; pass=$((pass + 1))
else
  echo "  FAIL  it exited 0 with no database to talk to"; fail=$((fail + 1))
fi
check_contains "it says which container it wanted" "fuelflow-no-such-container" "$out"

echo "=== against a real cluster ==="
CONTAINER_NAME="fuelflow-roles-test-$$"
ENV_FILE="$(mktemp)"
if ! command -v docker > /dev/null 2>&1 || ! docker info > /dev/null 2>&1; then
  echo "  SKIP  no usable Docker on this host - the behavioural half was not exercised."
  echo "        (CI has Docker, and the deploy-scripts job runs this for real.)"
  skip=$((skip + 1))
  rm -f "$ENV_FILE"
else
  docker run -d --name "$CONTAINER_NAME" \
    -e POSTGRES_PASSWORD=postgres -e POSTGRES_DB=fuelflow \
    -e POSTGRES_INITDB_ARGS="--locale-provider=icu --icu-locale=uk-UA --locale=C.UTF-8" \
    postgres:16-alpine -c shared_preload_libraries=pg_stat_statements > /dev/null 2>&1

  # NOT `pg_isready`: initdb answers YES against a BOOTSTRAP server that is not the one
  # that will serve the database. `psql -c 'select 1'` fails until the real server is up
  # and the database exists.
  ready=0
  for _ in $(seq 1 120); do
    if docker exec "$CONTAINER_NAME" psql -U postgres -d fuelflow -c 'select 1' > /dev/null 2>&1; then
      ready=1; break
    fi
    sleep 1
  done

  if [[ "$ready" -ne 1 ]]; then
    echo "  FAIL  the test container never became ready"
    fail=$((fail + 1))
  else
    # The environment drives the script: the env file is EMPTY, so the script has to
    # generate the two passwords the application needs and write them there. That is the
    # path the deploy takes on a box whose .env predates this change, and it is the path
    # that would otherwise stop the deploy at the preflight.
    export POSTGRES_USER=postgres
    export POSTGRES_DB=fuelflow
    export POSTGRES_CONTAINER="$CONTAINER_NAME"
    export FUELFLOW_ENV_FILE="$ENV_FILE"

    out="$(bash "$SCRIPT" 2>&1)"; rc=$?
    check "the script exits 0 on a healthy cluster" "0" "$rc"
    if [[ "$rc" -ne 0 ]]; then
      echo "--- the script said ---"
      echo "$out"
      echo "----------------------"
    fi

    # The passwords it generated are in the env file, and the compose file reads them
    # from there - so a password that was generated but not written would leave the
    # application unable to connect at all.
    if grep -q '^POSTGRES_APP_PASSWORD=' "$ENV_FILE"; then
      echo "  PASS  the application's password was written to the env file"; pass=$((pass + 1))
    else
      echo "  FAIL  the application's password is not in the env file"; fail=$((fail + 1))
    fi
    if grep -q '^POSTGRES_MIGRATOR_PASSWORD=' "$ENV_FILE"; then
      echo "  PASS  the migration role's password was written to the env file"; pass=$((pass + 1))
    else
      echo "  FAIL  the migration role's password is not in the env file"; fail=$((fail + 1))
    fi

    app_password="$(grep '^POSTGRES_APP_PASSWORD=' "$ENV_FILE" | cut -d= -f2-)"

    # The roles, as the catalogue reports them.
    roles="$(docker exec "$CONTAINER_NAME" psql -U postgres -d fuelflow -t -A -F' ' -c "
      SELECT rolname, rolsuper, rolcreaterole, rolcreatedb, rolbypassrls
        FROM pg_roles WHERE rolname LIKE 'fuelflow%' ORDER BY rolname")"
    check_contains "the application role exists" "fuelflow_app" "$roles"
    check_contains "the migration role exists" "fuelflow_migrator" "$roles"

    supers="$(echo "$roles" | grep -c ' t ' || true)"
    if [[ "$supers" == "0" ]]; then
      echo "  PASS  none of the three roles is a superuser"; pass=$((pass + 1))
    else
      echo "  FAIL  one of the roles is a superuser: $roles"; fail=$((fail + 1))
    fi

    # The application's password works over TCP, which is how the container connects.
    # The password is handed over on stdin and through a .pgpass file, never in argv -
    # a secret in the process list is a secret in everybody's `ps`.
    as_app() {
      printf '%s' "127.0.0.1:5432:fuelflow:fuelflow_app:$app_password" \
        | docker exec -i "$CONTAINER_NAME" sh -c \
            'cat > /tmp/.pgpass; PGPASSFILE=/tmp/.pgpass psql -h 127.0.0.1 -U fuelflow_app -d fuelflow -v ON_ERROR_STOP=1 -X -q -c "$1"; rc=$?; rm -f /tmp/.pgpass; exit $rc' \
            sh "$1" 2>&1
    }
    as_migrator() {
      docker exec -i "$CONTAINER_NAME" psql -U fuelflow_migrator -d fuelflow \
        -v ON_ERROR_STOP=1 -X -q -c "$1" 2>&1
    }

    out="$(as_app "SELECT 1" 2>&1)"; rc=$?
    check "the application role can log in with its password" "0" "$rc"

    # The application's own work.
    out="$(as_migrator "CREATE TABLE public.role_probe(id integer PRIMARY KEY, note text)" 2>&1)"; rc=$?
    check "the migration role may create a table" "0" "$rc"

    out="$(as_app "INSERT INTO public.role_probe(id, note) VALUES (1, 'x'); SELECT count(*) FROM public.role_probe; DELETE FROM public.role_probe;" 2>&1)"; rc=$?
    check "the application role may read and write the application's tables" "0" "$rc"

    # The things it must not do.
    out="$(as_app "CREATE TABLE public.role_probe_two(id integer)" 2>&1)"; rc=$?
    if [[ "$rc" -ne 0 ]]; then
      echo "  PASS  the application role may not create a table"; pass=$((pass + 1))
    else
      echo "  FAIL  the application role created a table"; fail=$((fail + 1))
    fi

    out="$(as_app "ALTER TABLE public.role_probe ADD COLUMN extra text" 2>&1)"; rc=$?
    if [[ "$rc" -ne 0 ]]; then
      echo "  PASS  the application role may not alter a table"; pass=$((pass + 1))
    else
      echo "  FAIL  the application role altered a table"; fail=$((fail + 1))
    fi

    out="$(as_app "DROP TABLE public.role_probe" 2>&1)"; rc=$?
    if [[ "$rc" -ne 0 ]]; then
      echo "  PASS  the application role may not drop a table"; pass=$((pass + 1))
    else
      echo "  FAIL  the application role dropped a table"; fail=$((fail + 1))
    fi

    out="$(as_app "CREATE ROLE probe_role LOGIN" 2>&1)"; rc=$?
    if [[ "$rc" -ne 0 ]]; then
      echo "  PASS  the application role may not create a role"; pass=$((pass + 1))
    else
      echo "  FAIL  the application role created a role"; fail=$((fail + 1))
    fi

    out="$(as_app "CREATE DATABASE probe_database" 2>&1)"; rc=$?
    if [[ "$rc" -ne 0 ]]; then
      echo "  PASS  the application role may not create a database"; pass=$((pass + 1))
    else
      echo "  FAIL  the application role created a database"; fail=$((fail + 1))
    fi

    # The migration may change the schema, and the table a migration creates is granted
    # to the application without anybody granting it by hand - that is the default
    # privilege, and it is what keeps the next migration's table usable.
    out="$(as_migrator "ALTER TABLE public.role_probe ADD COLUMN extra text" 2>&1)"; rc=$?
    check "the migration role may alter a table" "0" "$rc"

    out="$(as_app "INSERT INTO public.role_probe(id, note, extra) VALUES (2, 'y', 'z')" 2>&1)"; rc=$?
    check "the application role may write to a table the migration created" "0" "$rc"

    # The extension facts the migrations lean on.
    out="$(as_migrator "CREATE EXTENSION IF NOT EXISTS pg_stat_statements" 2>&1)"; rc=$?
    if [[ "$rc" -eq 0 ]]; then
      echo "  PASS  the migration role may run CREATE EXTENSION IF NOT EXISTS for an extension that exists"; pass=$((pass + 1))
    else
      echo "  FAIL  the migration role could not run the statement a migration carries: $out"; fail=$((fail + 1))
    fi

    out="$(as_migrator "CREATE EXTENSION postgres_fdw" 2>&1)"; rc=$?
    if [[ "$rc" -eq 0 ]]; then
      echo "  FAIL  the migration role created an extension that is not trusted - the migrations cannot be relied on to do this"; fail=$((fail + 1))
    elif [[ "$out" == *"is not available"* ]]; then
      echo "  SKIP  postgres_fdw is not in this image - the check needs an extension that is not trusted"; skip=$((skip + 1))
    else
      echo "  PASS  the migration role may not create an extension that is not trusted"; pass=$((pass + 1))
    fi

    # The read-only role.
    readonly_password="$(grep '^POSTGRES_READONLY_PASSWORD=' "$ENV_FILE" | cut -d= -f2-)"
    if [[ -z "$readonly_password" ]]; then
      out="$(POSTGRES_READONLY_PASSWORD="read-only-probe-password" bash "$SCRIPT" 2>&1)"; rc=$?
      check "the script exits 0 when the read-only role is configured" "0" "$rc"
      readonly_password="read-only-probe-password"
    fi
    as_readonly() {
      printf '%s' "127.0.0.1:5432:fuelflow:fuelflow_readonly:$readonly_password" \
        | docker exec -i "$CONTAINER_NAME" sh -c \
            'cat > /tmp/.pgpass; PGPASSFILE=/tmp/.pgpass psql -h 127.0.0.1 -U fuelflow_readonly -d fuelflow -v ON_ERROR_STOP=1 -X -q -c "$1"; rc=$?; rm -f /tmp/.pgpass; exit $rc' \
            sh "$1" 2>&1
    }
    out="$(as_readonly "SELECT count(*) FROM public.role_probe" 2>&1)"; rc=$?
    check "the read-only role may read" "0" "$rc"

    out="$(as_readonly "INSERT INTO public.role_probe(id) VALUES (3)" 2>&1)"; rc=$?
    if [[ "$rc" -ne 0 ]]; then
      echo "  PASS  the read-only role may not write"; pass=$((pass + 1))
    else
      echo "  FAIL  the read-only role wrote to a table"; fail=$((fail + 1))
    fi

    # Idempotence: the deploy runs this on every deploy, so running it twice has to
    # work and has to change nothing.
    before="$(docker exec "$CONTAINER_NAME" psql -U postgres -d fuelflow -t -A -c "
      SELECT count(*) FROM pg_roles WHERE rolname LIKE 'fuelflow%'")"
    out="$(bash "$SCRIPT" 2>&1)"; rc=$?
    check "the script exits 0 when it runs a second time" "0" "$rc"
    after="$(docker exec "$CONTAINER_NAME" psql -U postgres -d fuelflow -t -A -c "
      SELECT count(*) FROM pg_roles WHERE rolname LIKE 'fuelflow%'")"
    check "the second run did not create another role" "$before" "$after"

    # The probe table the script leaves behind is not left behind.
    leftover="$(docker exec "$CONTAINER_NAME" psql -U postgres -d fuelflow -t -A -c "
      SELECT count(*) FROM pg_class WHERE relname = '__fuelflow_role_probe'")"
    check "the script's probe table is gone" "0" "$leftover"
  fi

  docker rm -f "$CONTAINER_NAME" > /dev/null 2>&1
  rm -f "$ENV_FILE"
fi

echo
echo "passed: $pass  failed: $fail  skipped: $skip"
[[ "$fail" -eq 0 ]] || exit 1
exit 0
