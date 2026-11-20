#!/usr/bin/env bash
# Tests deploy/read-attestation-verdicts.sh - the check that keeps an empty verdict series
# from being read as a green light before flipping attestation to Enforce.
#
# WHY THIS EXISTS
#
# The script's whole value is a distinction: "the metric says Enforce would deny nothing"
# and "the metric says nothing at all" have to come out as different exit codes, because
# one of them is a reason to flip the switch and the other is a reason not to. A test that
# only exercised the happy path would prove neither. So every case below is a canned
# Prometheus response, including the three that must NOT report success:
#
#   - an empty vector  ("result":[]), which is what a box with no sign-ins returns;
#   - a nonzero total with zero eligible requests, which is the installed fleet;
#   - a NaN rate, which is what rate() gives you with too few samples.
#
# A tiny fake Prometheus serves those bodies. It reads the `query` parameter and chooses
# its answer by substring, so the script is exercised through its real HTTP path rather
# than through a stubbed function.
#
# python3 is what serves the fake. When it is missing the HTTP cases are SKIPPED and said
# out loud - a skipped test is not a passing test.
# ------------------------------------------------------------------------------
set -uo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
SCRIPT="$SCRIPT_DIR/read-attestation-verdicts.sh"

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
check_lacks() {
  local what="$1" needle="$2" haystack="$3"
  if [[ "$haystack" != *"$needle"* ]]; then
    echo "  PASS  $what"; pass=$((pass + 1))
  else
    echo "  FAIL  $what (output contained '$needle')"; fail=$((fail + 1))
  fi
}

echo "=== the script exists, is executable, and parses ==="
if [[ -x "$SCRIPT" ]]; then
  echo "  PASS  read-attestation-verdicts.sh is executable"; pass=$((pass + 1))
else
  echo "  FAIL  read-attestation-verdicts.sh is not executable"; fail=$((fail + 1))
fi
if bash -n "$SCRIPT" 2>/dev/null; then
  echo "  PASS  read-attestation-verdicts.sh parses"; pass=$((pass + 1))
else
  echo "  FAIL  read-attestation-verdicts.sh does not parse"; fail=$((fail + 1))
fi

echo "=== it refuses to run without a target ==="
out="$(bash "$SCRIPT" 2>&1)"; rc=$?
check "it exits 2 when given no target" "2" "$rc"
check_contains "it says what to pass" "--prometheus" "$out"

echo "=== it asks the three questions the runbook asks ==="
# The selectors ARE the contract with docs/APP_ATTESTATION_AND_PINNING_RUNBOOK.md. If the
# metric name or the eligible/verdict labels drift apart from that page, the number this
# script prints stops being the number the flip depends on.
if grep -q 'fuelflow_attestation_verdict_requests_total' "$SCRIPT"; then
  echo "  PASS  it reads the verdict counter"; pass=$((pass + 1))
else
  echo "  FAIL  it does not read the verdict counter"; fail=$((fail + 1))
fi
if grep -q 'enforce_eligible=\\"true\\"' "$SCRIPT"; then
  echo "  PASS  it filters on enforce_eligible=true"; pass=$((pass + 1))
else
  echo "  FAIL  it does not filter on enforce_eligible=true"; fail=$((fail + 1))
fi
if grep -q 'verdict!=\\"valid\\"' "$SCRIPT"; then
  echo "  PASS  it filters the refusals on verdict!=valid"; pass=$((pass + 1))
else
  echo "  FAIL  it does not filter the refusals on verdict!=valid"; fail=$((fail + 1))
fi

echo "=== it reports an unreachable Prometheus as \"could not answer\" ==="
out="$(bash "$SCRIPT" --prometheus http://127.0.0.1:1 --timeout 2 2>&1)"; rc=$?
check "it exits 2 when Prometheus is not listening" "2" "$rc"
check_contains "it names the failure" "could not answer" "$out"

if ! node -e '' > /dev/null 2>&1; then
  echo "=== SKIPPED: no node, so the fake Prometheus cannot be served ==="
  skip=$((skip + 1))
else
  WORKDIR="$(mktemp -d)"
  PORT_FILE="$WORKDIR/port"
  # The fake answers by substring of the query, so one server plays every case:
  #   ok         - eligible traffic flows, nothing refused
  #   refused    - eligible traffic flows and some of it fails verification
  #   empty      - the vector comes back empty, as it does on a box with no sign-ins
  #   ineligible - protected traffic exists, none of it carries a rollout header
  #   nan        - rate() has too few samples
  cat > "$WORKDIR/fake-prometheus.js" <<'JS'
const http = require('http');
const fs = require('fs');
const CASE = process.env.ATTESTATION_FAKE_CASE;
const PORT_FILE = process.env.ATTESTATION_FAKE_PORT_FILE;

function value(query) {
  if (CASE === 'empty') return null;
  const hasEligible = query.includes('enforce_eligible');
  const isRefusal = query.includes('verdict!=');
  if (!hasEligible) return CASE === 'ineligible' ? '4' : '12.5';
  if (isRefusal) return { ok: '0', refused: '0.4', ineligible: '0', nan: 'NaN' }[CASE];
  return { ok: '8.25', refused: '8.25', ineligible: '0', nan: 'NaN' }[CASE];
}

const server = http.createServer((req, res) => {
  const query = decodeURIComponent(new URL(req.url, 'http://x').searchParams.get('query') || '');
  const v = value(query);
  const body = v === null
    ? { status: 'success', data: { resultType: 'vector', result: [] } }
    : { status: 'success', data: { resultType: 'vector',
        result: [{ metric: {}, value: [1700000000.0, v] }] } };
  const raw = JSON.stringify(body);
  res.writeHead(200, { 'Content-Type': 'application/json', 'Content-Length': Buffer.byteLength(raw) });
  res.end(raw);
});

server.listen(0, '127.0.0.1', () => {
  fs.writeFileSync(PORT_FILE, String(server.address().port));
});
JS

  run_case() {
    local case="$1"
    : > "$PORT_FILE"
    ATTESTATION_FAKE_CASE="$case" ATTESTATION_FAKE_PORT_FILE="$PORT_FILE"       node "$WORKDIR/fake-prometheus.js" > /dev/null 2>&1 &
    local pid=$!
    local ready=0
    for _ in $(seq 1 100); do
      if [[ -s "$PORT_FILE" ]]; then ready=1; break; fi
      sleep 0.1
    done
    if [[ "$ready" -ne 1 ]]; then
      echo "  FAIL  the fake Prometheus for '$case' never started"; fail=$((fail + 1))
      kill "$pid" 2>/dev/null; LAST_OUT=""; LAST_RC=9; return
    fi
    local port; port="$(cat "$PORT_FILE")"
    LAST_OUT="$(bash "$SCRIPT" --prometheus "http://127.0.0.1:${port}" --timeout 5 2>&1)"
    LAST_RC=$?
    kill "$pid" 2>/dev/null; wait "$pid" 2>/dev/null
  }

  echo "=== eligible traffic, nothing refused: this is the green light ==="
  run_case ok
  check "it exits 0" "0" "$LAST_RC"
  check_contains "it says Enforce would deny nothing" "would deny nothing" "$LAST_OUT"
  check_contains "it prints the eligible rate" "8.25" "$LAST_OUT"

  echo "=== eligible traffic, and some of it fails verification ==="
  run_case refused
  check "it exits 1" "1" "$LAST_RC"
  check_contains "it prints the rate Enforce would deny" "0.4" "$LAST_OUT"
  check_contains "it says the flip would deny live traffic" "would deny" "$LAST_OUT"

  echo "=== the empty series: the case the script exists for ==="
  run_case empty
  check "it exits 2, not 0" "2" "$LAST_RC"
  check_contains "it refuses to call silence safe" "NOT evidence that Enforce is safe" "$LAST_OUT"
  check_lacks "it does not claim Enforce would deny nothing" "would deny nothing" "$LAST_OUT"

  echo "=== protected traffic, none of it eligible ==="
  run_case ineligible
  check "it exits 2 when nothing is eligible" "2" "$LAST_RC"
  check_contains "it says why that answers nothing" "no eligible traffic" "$LAST_OUT"

  echo "=== rate() with too few samples ==="
  run_case nan
  check "it exits 2 on a NaN rate" "2" "$LAST_RC"
  check_contains "it names the value it could not use" "not a number" "$LAST_OUT"

  rm -rf "$WORKDIR"
fi

echo
echo "passed: $pass  failed: $fail  skipped: $skip"
[[ "$fail" -eq 0 ]] || exit 1
exit 0
