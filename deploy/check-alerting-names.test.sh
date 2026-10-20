#!/usr/bin/env bash
# Tests deploy/check-alerting-names.sh - the gate that resolves every name the
# alerting chain uses against the compose services that exist.
#
# WHY THIS NEEDS A TEST
#
# The gate's whole value is that it fails when it should. A comparison that never fails is
# indistinguishable from a comparison that never runs, and the thing it guards against is a
# rename - which is exactly the kind of change that looks like a tidy-up in a diff, and
# which has already happened twice in this repository.
#
# The comparison is over text files, so it can be tested without a database, without a
# container and without a box. The preflight step that CALLS the script is checked too,
# because a gate that is not wired into the deploy is a script nobody runs.
#
# Run: bash deploy/check-alerting-names.test.sh
set -uo pipefail
cd "$(dirname "$0")/.."

WORK="$(mktemp -d)"
trap 'rm -rf "$WORK"' EXIT

SCRIPT="deploy/check-alerting-names.sh"
PREFLIGHT="deploy/preflight.sh"
CI_YML=".github/workflows/ci.yml"

pass=0
fail=0
ok()  { pass=$((pass + 1)); printf '  ok    %s\n' "$1"; }
bad() { fail=$((fail + 1)); printf '  FAIL  %s\n        got:      %s\n        expected: %s\n' "$1" "$2" "$3"; }
eq()  { if [ "$2" = "$3" ]; then ok "$1"; else bad "$1" "$2" "$3"; fi; }
has() { if echo "$2" | grep -qF -- "$3"; then ok "$1"; else bad "$1" "$2" "a line containing '$3'"; fi; }

# A fixture: the repository's own alerting files, in a directory of their own, so the
# check can be pointed at a broken copy without touching the real one.
FIX="$WORK/fixture"
mkdir -p "$FIX/deploy/observability/prometheus" \
         "$FIX/deploy" \
         "$FIX/backend/observability/grafana/provisioning/datasources"
cp deploy/docker-compose.prod.yml "$FIX/deploy/"
cp deploy/docker-compose.observability.yml "$FIX/deploy/"
cp deploy/observability/prometheus/prometheus.yml "$FIX/deploy/observability/prometheus/"
cp backend/observability/grafana/provisioning/datasources/datasources.yml \
   "$FIX/backend/observability/grafana/provisioning/datasources/"

# The check resolves its files relative to its own directory, so a copy of the script
# inside the fixture is the check run against the fixture.
cp "$SCRIPT" "$FIX/deploy/"

# ---------------------------------------------------------------- the healthy case
out="$(bash "$FIX/deploy/check-alerting-names.sh" 2>&1)"; code=$?
eq "every name resolves in the real repository" "$code" "0"
has "it says the scrape target is a service" "$out" "fuelflow-backend:8080 -> service 'fuelflow-backend'"
has "it says the datasources are services" "$out" "http://prometheus:9090 -> service 'prometheus'"
has "it does not call a self-scrape a service" "$out" "localhost:9090 -> outside the compose"

# ---------------------------------------------------------------- the failure that matters
# The exact breakage of 2026-10-20: the target names a service nobody runs.
sed -i 's/fuelflow-backend:8080/dotnet-backend:8080/' \
  "$FIX/deploy/observability/prometheus/prometheus.yml"
out="$(bash "$FIX/deploy/check-alerting-names.sh" 2>&1)"; code=$?
eq "a target that names no service is refused" "$code" "1"
has "it names the target that does not resolve" "$out" "dotnet-backend:8080"
has "it says where the service should have come from" "$out" "docker-compose.prod.yml"
has "it says what the consequence is" "$out" "can never fire"

# And the file it is in, so nobody has to go looking.
has "it names the file the target is in" "$out" "prometheus.yml"

# ---------------------------------------------------------------- the same for a datasource
cp deploy/observability/prometheus/prometheus.yml "$FIX/deploy/observability/prometheus/"
sed -i 's|http://prometheus:9090|http://prometheus-main:9090|' \
  "$FIX/backend/observability/grafana/provisioning/datasources/datasources.yml"
out="$(bash "$FIX/deploy/check-alerting-names.sh" 2>&1)"; code=$?
eq "a datasource that names no service is refused" "$code" "1"
has "it names the datasource that does not resolve" "$out" "prometheus-main:9090"
has "it says where the service should have come from" "$out" "docker-compose.observability.yml"

# ---------------------------------------------------------------- the trivial mistakes
cp backend/observability/grafana/provisioning/datasources/datasources.yml \
   "$FIX/backend/observability/grafana/provisioning/datasources/"
rm "$FIX/deploy/observability/prometheus/prometheus.yml"
out="$(bash "$FIX/deploy/check-alerting-names.sh" 2>&1)"; code=$?
eq "a missing file is an error, not a pass" "$code" "2"
has "it says which file it could not read" "$out" "prometheus.yml"

# ---------------------------------------------------------------- the gate is wired in
if grep -q "check-alerting-names.sh" "$PREFLIGHT"; then
  ok "the preflight calls the check"
else
  bad "the preflight calls the check" "not found" "check-alerting-names.sh in the preflight"
fi

if grep -q "check-alerting-names.test.sh" "$CI_YML"; then
  ok "ci.yml runs the test"
else
  bad "ci.yml runs the test" "not found" "check-alerting-names.test.sh in ci.yml"
fi

# ---------------------------------------------------------------- and the outcome
echo
if [ "$fail" -eq 0 ]; then
  echo "  $pass checks passed"
  exit 0
fi
echo "  $pass passed, $fail FAILED"
exit 1
