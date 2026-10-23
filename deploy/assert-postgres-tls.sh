#!/usr/bin/env bash
# ------------------------------------------------------------------------------
# assert-postgres-tls.sh - prove a Postgres stack's connections are encrypted
# and server-authenticated.
#
#     assert-postgres-tls.sh \
#         --env-file deploy/.env.staging \
#         --ca deploy/certs/postgres-staging/ca.crt \
#         [--different-from deploy/certs/postgres/ca.crt] \
#         [--container fuelflow-staging-postgres]
#
# Exit 0 = the connection is encrypted AND the server's certificate chains to
# the CA the client was given (server authentication).
# Exit 1 = it could not be proved.
#
# WHY THIS EXISTS
#
# A connection string that names a CA does not, by itself, prove anything: the
# client could still connect to any certificate. This script connects to the
# live database with `sslmode=verify-full` and `sslrootcert=<ca>` and inspects
# `pg_stat_ssl`, so success is the proof itself. It also checks that every
# other connection the same user/database has is SSL - the application's own
# ones - so the application cannot slip back to an unencrypted session.
#
# `--different-from` compares fingerprints: it proves this environment's CA is
# not production's CA. That is the rule: do not reuse production's CA for
# staging.
#
# THE PERMISSIONS ARE NOT COSMETIC
#
# The password reaches the client through a 0600 temporary pgpass file, never
# through argv, so it cannot leak into a process listing or a run log.
# ------------------------------------------------------------------------------
set -uo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=load-env.sh
. "$SCRIPT_DIR/load-env.sh"

ENV_FILE=""
CA=""
DIFFERENT_FROM=""
CONTAINER="postgres"
IMAGE="postgres:16-alpine"

fail() { echo "ERROR: $*" >&2; exit 1; }

while [[ $# -gt 0 ]]; do
  case "$1" in
    --env-file)      ENV_FILE="${2:-}"; shift 2 ;;
    --ca)            CA="${2:-}"; shift 2 ;;
    --different-from) DIFFERENT_FROM="${2:-}"; shift 2 ;;
    --container)     CONTAINER="${2:-}"; shift 2 ;;
    --image)         IMAGE="${2:-}"; shift 2 ;;
    -h|--help)       sed -n '2,25p' "$0"; exit 0 ;;
    *) fail "unknown argument: $1" ;;
  esac
done

[[ -n "$ENV_FILE" ]] || fail "--env-file is required"
[[ -f "$ENV_FILE" ]] || fail "env file not found: $ENV_FILE"
[[ -n "$CA" ]] || fail "--ca is required"
[[ -f "$CA" ]] || fail "CA certificate not found: $CA"

command -v docker > /dev/null 2>&1 || fail "docker is not on this host"
command -v openssl > /dev/null 2>&1 || fail "openssl is not on this host"

# The client shares the database container's network namespace, so localhost:5432
# reaches it directly - no compose network name is needed, and the client never
# touches the host's ports.
if [[ "$(docker inspect -f '{{.State.Running}}' "$CONTAINER" 2>/dev/null)" != "true" ]]; then
  fail "the container '$CONTAINER' is not running"
fi

load_env "$ENV_FILE"

db="${POSTGRES_DB:?POSTGRES_DB is not set in $ENV_FILE}"
user="${POSTGRES_USER:?POSTGRES_USER is not set in $ENV_FILE}"
password="${POSTGRES_PASSWORD:?POSTGRES_PASSWORD is not set in $ENV_FILE}"

WORK="$(mktemp -d)"
chmod 700 "$WORK"
trap 'rm -rf "$WORK"' EXIT

# pgpass format: hostname:port:database:user:password, where a literal ':' or '\'
# inside a field is backslash-escaped. Escape each FIELD, never the separators.
pgpass_field() { printf '%s' "$1" | sed -e 's/\\/\\\\/g' -e 's/:/\\:/g'; }
pgpass="$WORK/pgpass"
printf '%s:%s:%s:%s:%s\n' \
  "localhost" "5432" \
  "$(pgpass_field "$db")" "$(pgpass_field "$user")" "$(pgpass_field "$password")" \
  > "$pgpass"
chmod 600 "$pgpass"

echo "asserting the $CONTAINER database connection, user $user, db $db"
echo "CA: $CA"

run_psql() {
  docker run --rm --network "container:$CONTAINER" \
    -e PGSSLMODE=verify-full \
    -e PGSSLROOTCERT=/certs/ca.crt \
    -e PGHOST=localhost -e PGPORT=5432 \
    -e PGDATABASE="$db" -e PGUSER="$user" \
    -e PGPASSFILE=/pgpass \
    -v "$CA:/certs/ca.crt:ro" \
    -v "$pgpass:/pgpass:ro" \
    "$IMAGE" psql -v ON_ERROR_STOP=1 -A -t -F '|' "$@"
}

# --- 1. this very session must be verified TLS --------------------------------
session="$(run_psql -c "
  SELECT s.ssl::text, s.version, s.cipher
  FROM pg_stat_ssl s WHERE s.pid = pg_backend_pid();
" 2>&1)" || fail "could not open a verified-TLS session against $CONTAINER: $session"

IFS='|' read -r ssl version cipher <<< "$session"

[[ "$ssl" == "true" || "$ssl" == "t" ]] || fail "the client's own session is not SSL: $session"
[[ "$version" == TLSv* ]] || fail "the session reports no TLS version: '$version'"
[[ -n "$cipher" ]] || fail "the session reports no cipher"
echo "  PASS  the session is encrypted and server-authenticated"
echo "        TLS version: $version"
echo "        cipher:      $cipher"

# --- 2. every other connection this user/db has must be SSL too ---------------
counts="$(run_psql -c "
  SELECT count(*) FILTER (WHERE s.ssl)::text,
         count(*)::text
  FROM pg_stat_activity a
  JOIN pg_stat_ssl s ON s.pid = a.pid
  WHERE a.datname = current_database()
    AND a.usename = current_user
    AND a.pid <> pg_backend_pid()
    AND a.backend_type = 'client backend';
" 2>&1)" || fail "could not inspect the application's connections"

IFS='|' read -r encrypted total <<< "$counts"

[[ -n "$total" && "$total" -gt 0 ]] \
  || fail "the application has no open connection to inspect: the API may not be running"
[[ "$encrypted" == "$total" ]] \
  || fail "not all application connections are SSL ($encrypted of $total)"

echo "  PASS  all $total application connections are encrypted ($encrypted verified)"

# --- 3. this CA must not be production's CA -----------------------------------
if [[ -n "$DIFFERENT_FROM" ]]; then
  [[ -f "$DIFFERENT_FROM" ]] || fail "--different-from file not found: $DIFFERENT_FROM"
  staging_fp="$(openssl x509 -in "$CA" -noout -fingerprint -sha256)"
  prod_fp="$(openssl x509 -in "$DIFFERENT_FROM" -noout -fingerprint -sha256)"
  [[ "$staging_fp" != "$prod_fp" ]] \
    || fail "the staging CA is identical to the production CA: staging reuses production's CA"
  echo "  PASS  the CA differs from production's ($staging_fp)"
fi

echo "the $CONTAINER database connection is encrypted and server-authenticated."
