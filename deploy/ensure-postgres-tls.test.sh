#!/usr/bin/env bash
# Tests deploy/ensure-postgres-tls.sh - the script that gives the database a certificate
# the application can verify.
#
# WHY THIS EXISTS
#
# The production connection was "SSL Mode=Disable;Trust Server Certificate=true": either
# readable to anyone on the network or encrypted against nobody in particular. The fix is a
# certificate, and a certificate is only worth anything if three things hold: the server
# certificate verifies against the CA, the name in the certificate is the name the client
# connects by, and the script does not quietly replace the CA on every deploy (a new CA
# invalidates the certificate the running application is configured with).
#
# Those three are what this test checks. It also checks the permissions, because PostgreSQL
# refuses to start with a key that is readable by group or others - a failure that would show
# up as a database that will not start, on the day the certificate is introduced.
# ------------------------------------------------------------------------------
set -uo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
SCRIPT="$SCRIPT_DIR/ensure-postgres-tls.sh"

pass=0
fail=0
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
  echo "  PASS  ensure-postgres-tls.sh is executable"; pass=$((pass + 1))
else
  echo "  FAIL  ensure-postgres-tls.sh is not executable"; fail=$((fail + 1))
fi
if bash -n "$SCRIPT" 2>/dev/null; then
  echo "  PASS  ensure-postgres-tls.sh parses"; pass=$((pass + 1))
else
  echo "  FAIL  ensure-postgres-tls.sh does not parse"; fail=$((fail + 1))
fi

WORK="$(mktemp -d)"
trap 'rm -rf "$WORK"' EXIT

echo "=== it creates the CA, the server certificate, and the key ==="
out="$(bash "$SCRIPT" --dir "$WORK/certs" --host postgres 2>&1)"; rc=$?
check "it exits 0" "0" "$rc"
for f in ca.key ca.crt server.key server.crt; do
  if [[ -f "$WORK/certs/$f" ]]; then
    echo "  PASS  it created $f"; pass=$((pass + 1))
  else
    echo "  FAIL  it did not create $f"; fail=$((fail + 1))
  fi
done

echo "=== the server certificate verifies against the CA ==="
if openssl verify -CAfile "$WORK/certs/ca.crt" "$WORK/certs/server.crt" > /dev/null 2>&1; then
  echo "  PASS  the server certificate verifies against the CA"; pass=$((pass + 1))
else
  echo "  FAIL  the server certificate does not verify against the CA"; fail=$((fail + 1))
fi

echo "=== the certificate names the host the client connects by ==="
# A modern client ignores the CN and reads the SAN. A certificate without the right SAN
# verifies and is still rejected, which is the failure mode worth catching here.
san="$(openssl x509 -in "$WORK/certs/server.crt" -noout -ext subjectAltName 2>/dev/null)"
check_contains "the SAN names the database service" "DNS:postgres" "$san"
check_contains "the SAN also names localhost" "DNS:localhost" "$san"

echo "=== the permissions are the ones PostgreSQL insists on ==="
key_mode="$(stat -c '%a' "$WORK/certs/server.key" 2>/dev/null || stat -f '%Lp' "$WORK/certs/server.key")"
check "the server key is 0600" "600" "$key_mode"
ca_key_mode="$(stat -c '%a' "$WORK/certs/ca.key" 2>/dev/null || stat -f '%Lp' "$WORK/certs/ca.key")"
check "the CA key is 0600" "600" "$ca_key_mode"
crt_mode="$(stat -c '%a' "$WORK/certs/server.crt" 2>/dev/null || stat -f '%Lp' "$WORK/certs/server.crt")"
check "the server certificate is world-readable" "644" "$crt_mode"

echo "=== it is idempotent: the CA is the trust anchor and must not move ==="
before="$(openssl x509 -in "$WORK/certs/ca.crt" -noout -fingerprint -sha256)"
out2="$(bash "$SCRIPT" --dir "$WORK/certs" --host postgres 2>&1)"; rc2=$?
after="$(openssl x509 -in "$WORK/certs/ca.crt" -noout -fingerprint -sha256)"
check "the second run exits 0" "0" "$rc2"
check "the CA is the same one" "$before" "$after"
check_contains "the second run says it reuses the CA" "reusing the certificate authority" "$out2"

echo "=== it says how the application must connect ==="
out3="$(bash "$SCRIPT" --dir "$WORK/certs2" --host postgres 2>&1)"
check_contains "it prints the connection string to use" "SSL Mode=VerifyFull" "$out3"

echo
echo "passed: $pass  failed: $fail"
[[ "$fail" -eq 0 ]] || exit 1
exit 0
