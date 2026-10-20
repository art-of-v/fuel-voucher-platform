#!/usr/bin/env bash
# Tests deploy/assert-collation.sh - the check that stops the collation claim from
# being wrong in a document.
#
# WHY THIS EXISTS
#
# On 2026-10-19 the production cluster was rebuilt with ICU's uk-UA collation. On
# 2026-10-20 a review of the live database reported that it was "still on the C
# collation", and the claim was recorded in a document. It was false, and the reason
# it was false is the kind of thing that will be repeated: pg_database has three
# columns for the collation, and `datcollate` - the obvious one - is the LIBC name.
# Under the ICU provider it says "C" while the cluster is sorting the Ukrainian
# alphabet. `daticulocale` is the column that means it.
#
# So the check does not trust a column on its own. It reads the two columns that
# mean it AND it sorts four Ukrainian words and looks at the order. The behavioural
# half cannot be wrong, and it is the half a reader should run before writing a
# sentence about the collation.
#
# A container is needed for a real database, so the test uses one when Docker is
# available and says so plainly when it is not - a skipped test is not a passing
# test, and this one must not be mistaken for a green that was never earned.
# ------------------------------------------------------------------------------
set -uo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
SCRIPT="$SCRIPT_DIR/assert-collation.sh"

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
  echo "  PASS  assert-collation.sh is executable"; pass=$((pass + 1))
else
  echo "  FAIL  assert-collation.sh is not executable"; fail=$((fail + 1))
fi
if bash -n "$SCRIPT" 2>/dev/null; then
  echo "  PASS  assert-collation.sh parses"; pass=$((pass + 1))
else
  echo "  FAIL  assert-collation.sh does not parse"; fail=$((fail + 1))
fi

echo "=== it refuses to run without a target ==="
out="$(bash "$SCRIPT" 2>&1)"; rc=$?
check "it exits 2 when given no target" "2" "$rc"
check_contains "it says what to pass" "--psql" "$out"

echo "=== it reads daticulocale, not datcollate ==="
# The whole reason this script exists. If someone swaps daticulocale back to
# datcollate, the claim it makes becomes the false one it was written to prevent.
if grep -q "daticulocale" "$SCRIPT"; then
  echo "  PASS  it reads daticulocale"; pass=$((pass + 1))
else
  echo "  FAIL  it does not read daticulocale"; fail=$((fail + 1))
fi
if grep -q "datlocprovider" "$SCRIPT"; then
  echo "  PASS  it reads datlocprovider"; pass=$((pass + 1))
else
  echo "  FAIL  it does not read datlocprovider"; fail=$((fail + 1))
fi

echo "=== it checks the BEHAVIOUR, not only a column ==="
# A check that reads daticulocale alone is one release away from being wrong again.
if grep -q "ORDER BY t.v" "$SCRIPT"; then
  echo "  PASS  it sorts the probe words and reads the order"; pass=$((pass + 1))
else
  echo "  FAIL  it does not run a sort probe"; fail=$((fail + 1))
fi
# 'Ї' is the tell: in the Ukrainian alphabet it comes after 'Г' and before 'Я', and
# by code point it comes FIRST.
if grep -q "Їжак" "$SCRIPT"; then
  echo "  PASS  the probe includes the letter that distinguishes the two orders"; pass=$((pass + 1))
else
  echo "  FAIL  the probe does not include 'Ї'"; fail=$((fail + 1))
fi

echo "=== against a real cluster ==="
# A container is needed, and this host may not have a usable Docker. The check is
# reported as SKIPPED in that case - never as a pass.
CONTAINER_NAME="fuelflow-collation-test-$$"
if ! command -v docker > /dev/null 2>&1 || ! docker info > /dev/null 2>&1; then
  echo "  SKIP  no usable Docker on this host - the behavioural half was not exercised."
  echo "        (CI has Docker, and the migration dry-run job runs this for real.)"
  skip=$((skip + 1))
else
  # The SAME arguments production runs, so the test proves the arguments work.
  docker run -d --name "$CONTAINER_NAME" \
    -e POSTGRES_PASSWORD=postgres -e POSTGRES_DB=fuelflow \
    -e POSTGRES_INITDB_ARGS="--locale-provider=icu --icu-locale=uk-UA --locale=C.UTF-8" \
    postgres:16-alpine > /dev/null 2>&1
  # NOT `pg_isready`. Measured on 2026-10-20: `pg_isready -U postgres -d fuelflow` answers
  # YES within three-tenths of a second of `docker run`, because initdb starts a BOOTSTRAP
  # server on the socket - and that server is not the one that will serve the database. The
  # assertion then runs against a cluster that is still coming up, reads no row, and exits 2.
  # `psql -c 'select 1'` is the honest readiness check: it fails until the real server is up
  # and the database exists.
  ready=0
  for _ in $(seq 1 120); do
    if docker exec "$CONTAINER_NAME" psql -U postgres -d fuelflow -c 'select 1' > /dev/null 2>&1; then ready=1; break; fi
    sleep 1
  done
  if [[ "$ready" -ne 1 ]]; then
    echo "  FAIL  the test container never became ready"
    fail=$((fail + 1))
  else
    out="$(bash "$SCRIPT" --container "$CONTAINER_NAME" --user postgres --db fuelflow 2>&1)"; rc=$?
    check "it exits 0 on a cluster with the production locale" "0" "$rc"
    check_contains "it reports the locale" "uk-UA" "$out"
    check_contains "it reports the provider" "provider:     i" "$out"
    check_contains "it says the cluster sorts Ukrainian" "sorts by the Ukrainian alphabet" "$out"
  fi
  docker rm -f "$CONTAINER_NAME" > /dev/null 2>&1
fi

echo
echo "passed: $pass  failed: $fail  skipped: $skip"
[[ "$fail" -eq 0 ]] || exit 1
exit 0
