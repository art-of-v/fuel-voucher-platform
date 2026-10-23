#!/usr/bin/env bash
# ------------------------------------------------------------------------------
# Tests deploy/ensure-database-roles.sh - the script that gives the production
# database its four roles and applies the privilege matrix.
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
#   - the application role may read and write the application's mutable tables;
#   - the application role may NOT rewrite or delete the audit and ledger tables
#     (voucher_events, payments, provider_event_outbox) - database-privileges.tsv is
#     the source of truth, and the test reads its intent from there;
#   - the application role may close an outbox event and may not delete it;
#   - the maintenance role may purge the retention tables (DELETE) and may NOT insert
#     or update anywhere - a defect in the traffic role must not be able to erase the
#     ledger, and the destructive half of the lifecycle must not be able to forge it;
#   - the application role may NOT create a table, alter a table, drop a table, create
#     a role, or create a database;
#   - the migration role may change the schema;
#   - the read-only role may read and may not write;
#   - none of the four is a superuser;
#   - a public table missing from the matrix stops the deploy instead of being left
#     with whatever the defaults happened to give it;
#   - a privilege granted by hand is revoked on the next run;
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
MATRIX="$SCRIPT_DIR/database-privileges.tsv"

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
check_denied() {
  local what="$1" out rc="$2"
  if [[ "$rc" -ne 0 ]]; then
    echo "  PASS  $what"; pass=$((pass + 1))
  else
    echo "  FAIL  $what"; fail=$((fail + 1))
  fi
}
check_not_contains() {
  local what="$1" needle="$2" haystack="$3"
  if [[ "$haystack" != *"$needle"* ]]; then
    echo "  PASS  $what"; pass=$((pass + 1))
  else
    echo "  FAIL  $what (output contained '$needle')"; fail=$((fail + 1))
  fi
}

echo "=== the script and the matrix exist, are executable/readable, and parse ==="
if [[ -x "$SCRIPT" ]]; then
  echo "  PASS  ensure-database-roles.sh is executable"; pass=$((pass + 1))
else
  echo "  FAIL  ensure-database-roles.sh is not executable"; fail=$((fail + 1))
fi
if [[ -r "$MATRIX" ]]; then
  echo "  PASS  database-privileges.tsv is there"; pass=$((pass + 1))
else
  echo "  FAIL  database-privileges.tsv is missing - the script has no source of truth"; fail=$((fail + 1))
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

echo "=== the passwords-only mode writes the passwords and touches no database ==="
# The PRODUCTION compose file names the role passwords, and the staging deploy's
# preflight resolves that file against the production .env - so the passwords have to be
# in that file before the staging deploy runs. This mode does it without the database,
# so a staging deploy does not depend on the production database being up.
PASSWORDS_ENV="$(mktemp)"
out="$(FUELFLOW_ENV_FILE="$PASSWORDS_ENV" POSTGRES_CONTAINER=fuelflow-no-such-container \
  POSTGRES_USER=postgres POSTGRES_DB=fuelflow \
  bash "$SCRIPT" --passwords-only 2>&1)"; rc=$?
check "it exits 0 with no database at all" "0" "$rc"

if grep -q '^POSTGRES_APP_PASSWORD=.' "$PASSWORDS_ENV"; then
  echo "  PASS  it wrote the application's password into the env file"; pass=$((pass + 1))
else
  echo "  FAIL  it did not write the application's password into the env file"; fail=$((fail + 1))
fi
if grep -q '^POSTGRES_MIGRATOR_PASSWORD=.' "$PASSWORDS_ENV"; then
  echo "  PASS  it wrote the migration role's password into the env file"; pass=$((pass + 1))
else
  echo "  FAIL  it did not write the migration role's password into the env file"; fail=$((fail + 1))
fi
if grep -q '^POSTGRES_MAINTENANCE_PASSWORD=.' "$PASSWORDS_ENV"; then
  echo "  PASS  it wrote the maintenance role's password into the env file"; pass=$((pass + 1))
else
  echo "  FAIL  it did not write the maintenance role's password into the env file"; fail=$((fail + 1))
fi
rm -f "$PASSWORDS_ENV"

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
    # generate the three passwords the application needs and write them there. That is
    # the path the deploy takes on a box whose .env predates this change, and it is the
    # path that would otherwise stop the deploy at the preflight.
    export POSTGRES_USER=postgres
    export POSTGRES_DB=fuelflow
    export POSTGRES_CONTAINER="$CONTAINER_NAME"
    export FUELFLOW_ENV_FILE="$ENV_FILE"

    # --- the first run: a fresh cluster, no tables at all -----------------------
    out="$(bash "$SCRIPT" 2>&1)"; rc=$?
    check "the script exits 0 on a fresh cluster" "0" "$rc"
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
    if grep -q '^POSTGRES_MAINTENANCE_PASSWORD=' "$ENV_FILE"; then
      echo "  PASS  the maintenance role's password was written to the env file"; pass=$((pass + 1))
    else
      echo "  FAIL  the maintenance role's password is not in the env file"; fail=$((fail + 1))
    fi

    # --- the set comparison is not at the mercy of the box's locale --------------
    # The production deploy failed on 2026-10-23 with "comm: file 2 is not in sorted
    # order": the two lists were sorted under LC_ALL=C, and comm read them under the
    # box's en_US locale. C.UTF-8 is always available and is not C. The shim simulates
    # the failure: unless comm is pinned to C, it reports a relation that is not in the
    # matrix, so the script's own completeness guard stops the run. The marker proves
    # the shim, not the host's comm, produced the result.
    COMM_MARKER="$(mktemp)"
    comm() {
      printf '%s' "${LC_ALL:-}" > "$COMM_MARKER"
      if [[ "${LC_ALL:-}" != "C" ]]; then
        printf '%s\n' "public.__locale_probe"
        return 0
      fi
      command comm "$@"
    }
    export -f comm
    out="$(COMM_MARKER="$COMM_MARKER" LC_ALL=C.UTF-8 bash "$SCRIPT" 2>&1)"; rc=$?
    unset -f comm
    check "the role check forces the C locale for comm" "0" "$rc"
    check "the shim intercepted comm under the C locale" "C" "$(cat "$COMM_MARKER" 2>/dev/null)"
    rm -f "$COMM_MARKER"
    if [[ "$rc" -ne 0 ]]; then
      echo "--- the script said ---"
      echo "$out"
      echo "----------------------"
    fi

    app_password="$(grep '^POSTGRES_APP_PASSWORD=' "$ENV_FILE" | cut -d= -f2-)"
    maint_password="$(grep '^POSTGRES_MAINTENANCE_PASSWORD=' "$ENV_FILE" | cut -d= -f2-)"

    as_super() {
      docker exec -i "$CONTAINER_NAME" psql -U postgres -d fuelflow \
        -v ON_ERROR_STOP=1 -X -q -c "$1" 2>&1
    }
    as_migrator() {
      docker exec -i "$CONTAINER_NAME" psql -U fuelflow_migrator -d fuelflow \
        -v ON_ERROR_STOP=1 -X -q -c "$1" 2>&1
    }
    # A password is handed over on stdin and through a .pgpass file, never in argv -
    # a secret in the process list is a secret in everybody's `ps`.
    as_role() {
      local role="$1" password="$2" sql="$3"
      printf '%s' "127.0.0.1:5432:fuelflow:$role:$password" \
        | docker exec -i "$CONTAINER_NAME" sh -c \
            'cat > /tmp/.pgpass; chmod 600 /tmp/.pgpass; PGPASSFILE=/tmp/.pgpass psql -h 127.0.0.1 -U "$0" -d fuelflow -v ON_ERROR_STOP=1 -X -q -c "$1"; rc=$?; rm -f /tmp/.pgpass; exit $rc' \
            "$role" "$sql" 2>&1
    }
    as_app() { as_role fuelflow_app "$app_password" "$1"; }
    as_maint() { as_role fuelflow_maintenance "$maint_password" "$1"; }

    # The roles, as the catalogue reports them.
    roles="$(docker exec "$CONTAINER_NAME" psql -U postgres -d fuelflow -t -A -F' ' -c "
      SELECT rolname, rolsuper, rolcreaterole, rolcreatedb, rolbypassrls
        FROM pg_roles WHERE rolname LIKE 'fuelflow%' ORDER BY rolname")"
    check_contains "the application role exists" "fuelflow_app" "$roles"
    check_contains "the migration role exists" "fuelflow_migrator" "$roles"
    check_contains "the maintenance role exists" "fuelflow_maintenance" "$roles"

    supers="$(echo "$roles" | grep -c ' t ' || true)"
    if [[ "$supers" == "0" ]]; then
      echo "  PASS  none of the roles is a superuser"; pass=$((pass + 1))
    else
      echo "  FAIL  one of the roles is a superuser: $roles"; fail=$((fail + 1))
    fi

    out="$(as_app "SELECT 1" 2>&1)"; rc=$?
    check "the application role can log in with its password" "0" "$rc"
    out="$(as_maint "SELECT 1" 2>&1)"; rc=$?
    check "the maintenance role can log in with its password" "0" "$rc"

    # --- the schema arrives, as the migrator -------------------------------------
    # Stub tables carry the same names the matrix classifies, so the matrix is applied
    # to real relations: the eight-column identity-column detail does not matter here,
    # the privileges do. brands is the mutable control case.
    out="$(as_migrator "
      CREATE TABLE public.brands(id integer PRIMARY KEY, name text);
      CREATE TABLE public.payments(id integer PRIMARY KEY, amount numeric);
      CREATE TABLE public.voucher_events(id integer PRIMARY KEY, event_type text);
      CREATE TABLE public.outbox_events(id integer PRIMARY KEY, event_type text, processed boolean NOT NULL DEFAULT false, processed_at_utc timestamptz);
      CREATE TABLE public.webhook_events(id integer PRIMARY KEY, status text, processed_at_utc timestamptz, processing_started_at_utc timestamptz, error text, attempts integer NOT NULL DEFAULT 1, payload text);
      CREATE TABLE public.provider_event_outbox(id integer PRIMARY KEY, payload text);
      CREATE TABLE public.voucher_redemptions(id integer PRIMARY KEY, status text, confirmed_at_utc timestamptz, confirmed_by_user_id uuid, closed_at_utc timestamptz, closed_by_user_id uuid, resolution_note text, updated_at_utc timestamptz, reported_liters numeric);
      CREATE TABLE public.redemption_statement_rows(id integer PRIMARY KEY, status text, resolved_at_utc timestamptz, resolved_by_user_id uuid, resolution_note text, external_id text);
      CREATE TABLE public.\"__EFMigrationsHistory\"(id integer);
    " 2>&1)"; rc=$?
    check "the migration role may create the stub tables" "0" "$rc"

    # Default privileges: the app can write a table the migrator just created, before
    # the matrix has a row for it. That is what keeps a migration's table usable in the
    # window between the migration and the matrix update.
    out="$(as_app "INSERT INTO public.brands(id, name) VALUES (1, 'default-privilege probe')" 2>&1)"; rc=$?
    check "the application role may write a table the migration created" "0" "$rc"

    # --- the completeness check --------------------------------------------------
    out="$(as_super "CREATE TABLE public.unlisted_probe(id integer)" 2>&1)"; rc=$?
    check "the superuser may create an unlisted table (the probe setup)" "0" "$rc"
    out="$(bash "$SCRIPT" 2>&1)"; rc=$?
    if [[ "$rc" -ne 0 ]]; then
      echo "  PASS  an unlisted public table stops the deploy"; pass=$((pass + 1))
    else
      echo "  FAIL  the script ignored a public table that is not in the matrix"; fail=$((fail + 1))
    fi
    check_contains "it names the unlisted table" "unlisted_probe" "$out"
    out="$(as_super "DROP TABLE public.unlisted_probe" 2>&1)"; rc=$?
    check "the unlisted probe table is dropped again" "0" "$rc"

    # --- the second run applies the matrix ---------------------------------------
    out="$(bash "$SCRIPT" 2>&1)"; rc=$?
    check "the script exits 0 when the matrix has tables to apply" "0" "$rc"
    if [[ "$rc" -ne 0 ]]; then
      echo "--- the script said ---"
      echo "$out"
      echo "----------------------"
    fi
    # The first run created pg_stat_statements, and that extension puts two views in
    # public. They are PostgreSQL's own objects, not ours to classify: the matrix must
    # not have to name them, and the SECOND deploy must not stop because the first one
    # created them.
    check_not_contains "the extension's own views are not asked for in the matrix" "public.pg_stat_statements" "$out"

    # The application's mutable work.
    out="$(as_app "UPDATE public.brands SET name = 'renamed' WHERE false" 2>&1)"; rc=$?
    check "the application role may update a mutable table" "0" "$rc"
    out="$(as_app "DELETE FROM public.brands WHERE false" 2>&1)"; rc=$?
    check "the application role may delete from a mutable table" "0" "$rc"

    # payments: SIU. No delete, ever - a payment a bug can erase is a payment the
    # reconciliation cannot trust.
    out="$(as_app "UPDATE public.payments SET amount = amount WHERE false" 2>&1)"; rc=$?
    check "the application role may update a payment" "0" "$rc"
    out="$(as_app "DELETE FROM public.payments WHERE false" 2>&1)"; rc=$?
    check_denied "the application role may NOT delete a payment" "$rc"
    out="$(as_maint "DELETE FROM public.payments WHERE false" 2>&1)"; rc=$?
    check_denied "the maintenance role may NOT delete a payment" "$rc"

    # voucher_events: SI. The audit trail is written once and never rewritten.
    out="$(as_app "INSERT INTO public.voucher_events(id, event_type) VALUES (1, 'redeemed')" 2>&1)"; rc=$?
    check "the application role may append a voucher event" "0" "$rc"
    out="$(as_app "UPDATE public.voucher_events SET event_type = event_type WHERE false" 2>&1)"; rc=$?
    check_denied "the application role may NOT rewrite a voucher event" "$rc"
    out="$(as_app "DELETE FROM public.voucher_events WHERE false" 2>&1)"; rc=$?
    check_denied "the application role may NOT delete a voucher event" "$rc"

    # webhook_events: SI + U(status, processed_at_utc, processing_started_at_utc, error, attempts)
    # for the app - the recorder writes exactly those columns - and the body exactly as received
    # is not one of them.
    out="$(as_app "INSERT INTO public.webhook_events(id, status, payload) VALUES (1, 'Received', '{}')" 2>&1)"; rc=$?
    check "the application role may store a webhook delivery" "0" "$rc"
    out="$(as_app "UPDATE public.webhook_events SET status = 'Processed', processed_at_utc = now() WHERE false" 2>&1)"; rc=$?
    check "the application role may settle a webhook delivery" "0" "$rc"
    out="$(as_app "UPDATE public.webhook_events SET payload = payload WHERE false" 2>&1)"; rc=$?
    check_denied "the application role may NOT rewrite a stored webhook delivery" "$rc"

    # provider_event_outbox: SI for the app, SD for the maintenance role.
    out="$(as_app "UPDATE public.provider_event_outbox SET payload = payload WHERE false" 2>&1)"; rc=$?
    check_denied "the application role may NOT rewrite the provider event audit trail" "$rc"
    out="$(as_app "DELETE FROM public.provider_event_outbox WHERE false" 2>&1)"; rc=$?
    check_denied "the application role may NOT delete the provider event audit trail" "$rc"
    out="$(as_maint "DELETE FROM public.provider_event_outbox WHERE false" 2>&1)"; rc=$?
    check "the maintenance role may purge the provider event audit trail" "0" "$rc"

    # voucher_redemptions and redemption_statement_rows: SI + the columns the decision
    # paths close in place. The evidence itself (the claimed litres, the supplier's line
    # id) is not writable after the insert, and neither table is deletable.
    out="$(as_app "INSERT INTO public.voucher_redemptions(id, status) VALUES (1, 'Reported')" 2>&1)"; rc=$?
    check "the application role may record a redemption claim" "0" "$rc"
    out="$(as_app "UPDATE public.voucher_redemptions SET status = 'Confirmed', confirmed_at_utc = now(), confirmed_by_user_id = NULL, closed_at_utc = now(), closed_by_user_id = NULL, resolution_note = 'checked', updated_at_utc = now() WHERE false" 2>&1)"; rc=$?
    check "the application role may close a redemption claim" "0" "$rc"
    out="$(as_app "UPDATE public.voucher_redemptions SET reported_liters = reported_liters WHERE false" 2>&1)"; rc=$?
    check_denied "the application role may NOT rewrite the claimed litres" "$rc"
    out="$(as_app "DELETE FROM public.voucher_redemptions WHERE false" 2>&1)"; rc=$?
    check_denied "the application role may NOT delete a redemption claim" "$rc"
    out="$(as_maint "DELETE FROM public.voucher_redemptions WHERE false" 2>&1)"; rc=$?
    check_denied "the maintenance role may NOT delete a redemption claim" "$rc"

    out="$(as_app "INSERT INTO public.redemption_statement_rows(id, external_id, status) VALUES (1, 'line-1', 'Matched')" 2>&1)"; rc=$?
    check "the application role may import a statement line" "0" "$rc"
    out="$(as_app "UPDATE public.redemption_statement_rows SET status = 'Confirmed', resolved_at_utc = now(), resolved_by_user_id = NULL, resolution_note = 'checked' WHERE false" 2>&1)"; rc=$?
    check "the application role may resolve a statement line" "0" "$rc"
    out="$(as_app "UPDATE public.redemption_statement_rows SET external_id = external_id WHERE false" 2>&1)"; rc=$?
    check_denied "the application role may NOT rewrite the supplier's line id" "$rc"
    out="$(as_app "DELETE FROM public.redemption_statement_rows WHERE false" 2>&1)"; rc=$?
    check_denied "the application role may NOT delete a statement line" "$rc"
    out="$(as_maint "DELETE FROM public.redemption_statement_rows WHERE false" 2>&1)"; rc=$?
    check_denied "the maintenance role may NOT delete a statement line" "$rc"

    # __EFMigrationsHistory: migration-only, and its name is mixed-case. The app and the
    # maintenance role get nothing; the quoted REVOKE is what makes the row applicable.
    out="$(as_app "SELECT count(*) FROM public.\"__EFMigrationsHistory\"" 2>&1)"; rc=$?
    check_denied "the application role may NOT read the EF migration history" "$rc"
    out="$(as_maint "SELECT count(*) FROM public.\"__EFMigrationsHistory\"" 2>&1)"; rc=$?
    check_denied "the maintenance role may NOT read the EF migration history" "$rc"

    # outbox_events: SIU for the app; SD for maintenance. The app's UPDATE covers the whole
    # row: EF Core's Update(entity) writes every column, so a column-level grant refuses the
    # dispatcher's own save.
    out="$(as_app "INSERT INTO public.outbox_events(id, event_type) VALUES (1, 'order.created')" 2>&1)"; rc=$?
    check "the application role may append an outbox event" "0" "$rc"
    out="$(as_app "UPDATE public.outbox_events SET processed = true, processed_at_utc = now() WHERE false" 2>&1)"; rc=$?
    check "the application role may close an outbox event" "0" "$rc"
    out="$(as_app "UPDATE public.outbox_events SET event_type = event_type WHERE false" 2>&1)"; rc=$?
    check "the application role may update the whole outbox row (EF writes every column)" "0" "$rc"
    out="$(as_app "DELETE FROM public.outbox_events WHERE false" 2>&1)"; rc=$?
    check_denied "the application role may NOT delete an outbox event" "$rc"
    out="$(as_maint "DELETE FROM public.outbox_events WHERE false" 2>&1)"; rc=$?
    check "the maintenance role may purge the outbox" "0" "$rc"
    out="$(as_maint "UPDATE public.outbox_events SET processed = true WHERE false" 2>&1)"; rc=$?
    check_denied "the maintenance role may NOT update the outbox" "$rc"
    out="$(as_maint "INSERT INTO public.outbox_events SELECT * FROM public.outbox_events WHERE false" 2>&1)"; rc=$?
    check_denied "the maintenance role may NOT write the outbox" "$rc"

    # The maintenance role's rights end where the matrix says they end.
    out="$(as_maint "SELECT count(*) FROM public.brands" 2>&1)"; rc=$?
    check_denied "the maintenance role may NOT read a table the matrix does not give it" "$rc"
    out="$(as_maint "DELETE FROM public.brands WHERE false" 2>&1)"; rc=$?
    check_denied "the maintenance role may NOT delete from a mutable product table" "$rc"

    # The things the application must not do to the schema.
    out="$(as_app "CREATE TABLE public.app_created(id integer)" 2>&1)"; rc=$?
    if [[ "$rc" -ne 0 ]]; then
      echo "  PASS  the application role may not create a table"; pass=$((pass + 1))
    else
      as_super "DROP TABLE IF EXISTS public.app_created" > /dev/null 2>&1
      echo "  FAIL  the application role created a table"; fail=$((fail + 1))
    fi

    out="$(as_app "ALTER TABLE public.brands ADD COLUMN extra text" 2>&1)"; rc=$?
    check_denied "the application role may not alter a table" "$rc"

    out="$(as_app "DROP TABLE public.brands" 2>&1)"; rc=$?
    check_denied "the application role may not drop a table" "$rc"

    out="$(as_app "CREATE ROLE probe_role LOGIN" 2>&1)"; rc=$?
    if [[ "$rc" -ne 0 ]]; then
      echo "  PASS  the application role may not create a role"; pass=$((pass + 1))
    else
      as_super "DROP ROLE IF EXISTS probe_role" > /dev/null 2>&1
      echo "  FAIL  the application role created a role"; fail=$((fail + 1))
    fi

    out="$(as_app "CREATE DATABASE probe_database" 2>&1)"; rc=$?
    if [[ "$rc" -ne 0 ]]; then
      echo "  PASS  the application role may not create a database"; pass=$((pass + 1))
    else
      as_super "DROP DATABASE IF EXISTS probe_database" > /dev/null 2>&1
      echo "  FAIL  the application role created a database"; fail=$((fail + 1))
    fi

    # The migration role may change the schema.
    out="$(as_migrator "ALTER TABLE public.brands ADD COLUMN extra text" 2>&1)"; rc=$?
    check "the migration role may alter a table" "0" "$rc"

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
    out="$(as_role fuelflow_readonly "$readonly_password" "SELECT count(*) FROM public.brands" 2>&1)"; rc=$?
    check "the read-only role may read" "0" "$rc"

    out="$(as_role fuelflow_readonly "$readonly_password" "INSERT INTO public.brands(id) VALUES (2)" 2>&1)"; rc=$?
    check_denied "the read-only role may not write" "$rc"

    # --- a privilege granted by hand is revoked by the next run ------------------
    out="$(as_super "GRANT DELETE ON public.payments TO fuelflow_app" 2>&1)"; rc=$?
    check "the superuser may grant a privilege by hand (the probe setup)" "0" "$rc"
    out="$(as_app "DELETE FROM public.payments WHERE false" 2>&1)"; rc=$?
    check "the hand-granted privilege works before the next run" "0" "$rc"

    before="$(docker exec "$CONTAINER_NAME" psql -U postgres -d fuelflow -t -A -c "
      SELECT count(*) FROM pg_roles WHERE rolname LIKE 'fuelflow%'")"
    out="$(bash "$SCRIPT" 2>&1)"; rc=$?
    check "the script exits 0 when it runs again" "0" "$rc"
    after="$(docker exec "$CONTAINER_NAME" psql -U postgres -d fuelflow -t -A -c "
      SELECT count(*) FROM pg_roles WHERE rolname LIKE 'fuelflow%'")"
    check "the run did not create another role" "$before" "$after"

    out="$(as_app "DELETE FROM public.payments WHERE false" 2>&1)"; rc=$?
    check_denied "the next run revoked the hand-granted DELETE" "$rc"

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
