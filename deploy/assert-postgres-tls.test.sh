#!/usr/bin/env bash
# Tests deploy/assert-postgres-tls.sh - it proves a Postgres stack's
# connections are encrypted and server-authenticated.
#
# WHY THIS EXISTS
#
# The staging stack now says "SSL Mode=VerifyFull", but a connection string
# that names a CA does not prove the server holds a certificate that CA signs.
# This test runs assert-postgres-tls.sh for real against a throwaway Postgres:
# a verified session passes; a connection pointed at a different CA fails;
# reusing production's CA is refused.
#
# It needs Docker and a real cluster; on a machine without Docker it skips,
# because a check nobody can run is no check at all. In CI (the Deploy scripts
# job) Docker is there, so it runs for real.
# ------------------------------------------------------------------------------
set -uo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
SCRIPT="$SCRIPT_DIR/assert-postgres-tls.sh"
TLS_SCRIPT="$SCRIPT_DIR/ensure-postgres-tls.sh"
IMAGE="postgres:16-alpine"
DB="appdb"
USER="appuser"
PASSWORD="testpass"

pass=0
fail=0
fail() { echo "ERROR: $*" >&2; exit 1; }
check() {
  local what="$1" expected="$2" actual="$3"
  if [[ "$expected" == "$actual" ]]; then
    echo "  PASS  $what"; pass=$((pass + 1))
  else
    echo "  FAIL  $what (expected '$expected', got '$actual')"; fail=$((fail + 1))
  fi
}

command -v docker > /dev/null 2>&1 || { echo "SKIP  docker is not available"; exit 0; }

WORK="$(mktemp -d)"

cleanup() {
  docker rm -f fuelflow-tls-assert-app fuelflow-tls-assert-db > /dev/null 2>&1 || true
}
trap 'cleanup; rm -rf "$WORK"' EXIT
cleanup

echo "=== the script exists, is executable, and parses ==="
if [[ -x "$SCRIPT" ]]; then echo "  PASS  assert-postgres-tls.sh is executable"; pass=$((pass + 1)); else echo "  FAIL  assert-postgres-tls.sh is not executable"; fail=$((fail + 1)); fi
if bash -n "$SCRIPT" 2>/dev/null; then echo "  PASS  assert-postgres-tls.sh parses"; pass=$((pass + 1)); else echo "  FAIL  assert-postgres-tls.sh does not parse"; fail=$((fail + 1)); fi

echo "=== it creates the CA and signs a server certificate for localhost ==="
bash "$TLS_SCRIPT" --dir "$WORK/certs" --host localhost > /dev/null 2>&1 \
  || fail "ensure-postgres-tls.sh could not create the certificate"
for f in ca.key ca.crt server.key server.crt; do
  [[ -f "$WORK/certs/$f" ]] || fail "ensure-postgres-tls.sh did not create $f"
done
echo "  PASS  the certificate and its CA exist"; pass=$((pass + 1))

# The certificate helper chowns the key to uid 70 only when it runs as root. Here it
# may not, and PostgreSQL refuses a key the database user cannot read - so hand the
# key to the image's postgres user the way the production deploy does.
docker run --rm --user 0 -v "$WORK/certs:/certs" "$IMAGE" chown 70:70 /certs/server.key

echo "=== start a throwaway Postgres that serves the certificate ==="
docker run -d --name fuelflow-tls-assert-db \
  -e POSTGRES_DB="$DB" -e POSTGRES_USER="$USER" -e POSTGRES_PASSWORD="$PASSWORD" \
  -v "$WORK/certs:/certs:ro" \
  "$IMAGE" postgres \
  -c ssl=on \
  -c ssl_cert_file=/certs/server.crt \
  -c ssl_key_file=/certs/server.key > /dev/null

# The entrypoint runs a temporary server without TLS to execute the init scripts, so
# "ready" over the socket is not "the real server is up". Wait for TCP, which only the
# real (TLS) server listens on.
for i in $(seq 1 30); do
  if docker exec fuelflow-tls-assert-db pg_isready -h 127.0.0.1 -U "$USER" -d "$DB" > /dev/null 2>&1; then break; fi
  sleep 1
done
docker exec fuelflow-tls-assert-db pg_isready -h 127.0.0.1 -U "$USER" -d "$DB" > /dev/null \
  || fail "the throwaway Postgres did not become ready"
echo "  PASS  Postgres is up with TLS"; pass=$((pass + 1))

# A long-lived application connection, so the assertion script has something to
# inspect. It stays open for 40 seconds; the assertion runs well inside that.
docker run -d --name fuelflow-tls-assert-app --network container:fuelflow-tls-assert-db \
  -e PGPASSWORD="$PASSWORD" \
  -v "$WORK/certs/ca.crt:/certs/ca.crt:ro" \
  "$IMAGE" psql "host=localhost dbname=$DB user=$USER sslmode=verify-full sslrootcert=/certs/ca.crt" \
  -c "SELECT pg_sleep(40)" > /dev/null
sleep 3

cat > "$WORK/env" <<EOF
POSTGRES_DB=$DB
POSTGRES_USER=$USER
POSTGRES_PASSWORD=$PASSWORD
EOF

echo "=== a verified-TLS session against the stack's own CA passes ==="
out="$(bash "$SCRIPT" --env-file "$WORK/env" --ca "$WORK/certs/ca.crt" --container fuelflow-tls-assert-db 2>&1)"; rc=$?
check "it exits 0" "0" "$rc"
echo "$out" | grep -q "server-authenticated" && { echo "  PASS  it reports server authentication"; pass=$((pass + 1)); } || { echo "  FAIL  it did not report server authentication"; echo "$out"; fail=$((fail + 1)); }
echo "$out" | grep -q "all .* application connections are encrypted" && { echo "  PASS  it reports the application connections are encrypted"; pass=$((pass + 1)); } || { echo "  FAIL  it did not report encrypted application connections"; echo "$out"; fail=$((fail + 1)); }

echo "=== a different CA refuses the connection ==="
mkdir -p "$WORK/other"
bash "$TLS_SCRIPT" --dir "$WORK/other" --host localhost > /dev/null 2>&1
out="$(bash "$SCRIPT" --env-file "$WORK/env" --ca "$WORK/other/ca.crt" --container fuelflow-tls-assert-db 2>&1)"; rc=$?
[[ "$rc" -ne 0 ]] && { echo "  PASS  the wrong CA is refused"; pass=$((pass + 1)); } || { echo "  FAIL  the wrong CA was accepted"; echo "$out"; fail=$((fail + 1)); }

echo "=== reuse of production's CA is refused ==="
out="$(bash "$SCRIPT" --env-file "$WORK/env" --ca "$WORK/certs/ca.crt" --container fuelflow-tls-assert-db --different-from "$WORK/certs/ca.crt" 2>&1)"; rc=$?
[[ "$rc" -ne 0 ]] && { echo "  PASS  reusing the same CA is refused"; pass=$((pass + 1)); } || { echo "  FAIL  reusing the same CA was accepted"; echo "$out"; fail=$((fail + 1)); }

echo
echo "passed: $pass  failed: $fail"
[[ "$fail" -eq 0 ]] || exit 1
exit 0
