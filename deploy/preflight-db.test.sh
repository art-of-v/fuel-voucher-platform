#!/usr/bin/env bash
# Proves the data-precondition step behaves: that it reports a violation, that it stays a
# warning by default, that strict mode actually blocks, and - the one that matters most - that it
# cannot report success for a check that never ran.
#
# Step 5 runs on the production box before anything is pulled, so "it said fine" has to mean
# "it asked and the answer was fine". The 2026-10-15 outage was a condition that nobody asked
# about until the migration refused at container start; the first version of this check then
# printed "all data preconditions satisfied" when the database was unreachable and no query had
# run at all, which is the same silence it was written to end.

set -uo pipefail
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PREFLIGHT="$SCRIPT_DIR/preflight.sh"
TMP="$(mktemp -d)"
trap 'rm -rf "$TMP"' EXIT

pass=0; fail=0
check() { # description expected actual
  local desc="$1" want="$2" got="$3"
  if [[ "$got" == "$want" ]]; then echo "  PASS  $desc"; pass=$((pass+1));
  else echo "  FAIL  $desc (got '$got', wanted '$want')"; fail=$((fail+1)); fi
}
contains() { # description needle haystack
  local desc="$1" needle="$2" hay="$3"
  if [[ "$hay" == *"$needle"* ]]; then echo "  PASS  $desc"; pass=$((pass+1));
  else echo "  FAIL  $desc (output did not contain '$needle')"; fail=$((fail+1)); fi
}
lacks() { # description needle haystack
  local desc="$1" needle="$2" hay="$3"
  if [[ "$hay" != *"$needle"* ]]; then echo "  PASS  $desc"; pass=$((pass+1));
  else echo "  FAIL  $desc (output unexpectedly contained '$needle')"; fail=$((fail+1)); fi
}

# A stub `docker` on PATH. Step 5 asks exactly two things of it, so the stub answers exactly two.
# DOCKER_STUB_RUNNING="" makes `ps` report no database container; DOCKER_STUB_ROWS supplies what
# psql "returns" for the counting call and for the detail call.
mkdir -p "$TMP/bin"
cat > "$TMP/bin/docker" <<'STUB'
#!/usr/bin/env bash
if [[ "$1" == "ps" ]]; then
  [[ -n "${DOCKER_STUB_RUNNING:-}" ]] && echo "fuelflow-postgres"
  exit 0
fi
if [[ "$1" == "exec" ]]; then
  # The preflight calls psql twice: once with -t -A to count rows, once plain to show them.
  if [[ "$*" == *"-t -A"* ]]; then
    [[ -n "${DOCKER_STUB_ROWS:-}" ]] && printf '%s\n' "$DOCKER_STUB_ROWS"
  else
    [[ -n "${DOCKER_STUB_ROWS:-}" ]] && printf '  station|fuel|real|2|final_price_per_liter|2\n'
  fi
  exit 0
fi
exit 0
STUB
chmod +x "$TMP/bin/docker"
export PATH="$TMP/bin:$PATH"

run() { # env assignments... -- runs the preflight in --data-only mode
  ( cd "$SCRIPT_DIR" && env "$@" bash "$PREFLIGHT" --data-only 2>&1 )
}

# ------------------------------------------------------------------------------
# A clean database
# ------------------------------------------------------------------------------
out="$(run DOCKER_STUB_RUNNING=1 DOCKER_STUB_ROWS= DOCKER_STUB_STRICT_UNUSED=1)"
check "a clean database exits 0" "0" "$?"
contains "the file is reported ok" "010-fuel-package-single-price.sql: ok" "$out"
contains "and satisfaction is stated with a count" "all data preconditions satisfied (1 checked)" "$out"

# ------------------------------------------------------------------------------
# A violated precondition
# ------------------------------------------------------------------------------
out="$(run DOCKER_STUB_RUNNING=1 DOCKER_STUB_ROWS='a|b|real|2|final_price_per_liter|2')"
check "a violation is a WARNING, not a failure, by default" "0" "$?"
contains "the violation is named" "VIOLATED" "$out"
contains "the offending file is named" "010-fuel-package-single-price.sql: VIOLATED" "$out"
contains "the rows are shown, indented under the warning" "station|fuel|real|2|final_price_per_liter|2" "$out"
lacks "and it does not claim the preconditions were satisfied" "all data preconditions satisfied" "$out"

# ------------------------------------------------------------------------------
# Strict mode: the same database, now blocking
# ------------------------------------------------------------------------------
out="$(run DOCKER_STUB_RUNNING=1 DOCKER_STUB_ROWS='a|b|real|2|final_price_per_liter|2' PREFLIGHT_DB_STRICT=1)"
check "strict mode blocks the deploy" "1" "$?"
contains "and says why" "PREFLIGHT_DB_STRICT=1" "$out"
# The whole point of testing the order: "Preflight passed" printed before an exit 1 is a
# contradiction on one screen, and the last line a reader saw is the one they remember.
lacks "and never prints 'Preflight passed' while failing" "Preflight passed." "$out"
contains "it says the details are above" "Details" "$out"

out="$(run DOCKER_STUB_RUNNING=1 DOCKER_STUB_ROWS= PREFLIGHT_DB_STRICT=1)"
check "strict mode with a clean database still passes" "0" "$?"

# ------------------------------------------------------------------------------
# The database is not reachable
# ------------------------------------------------------------------------------
out="$(run DOCKER_STUB_RUNNING=)"
check "an unreachable database is a warning, not a failure" "0" "$?"
contains "and it says the check did not run" "not checked" "$out"
# This is the assertion the check exists for. Reporting "satisfied" here is how a gate stops
# being read: silence that looks exactly like a clean system.
lacks "and it does NOT claim the preconditions were satisfied" "all data preconditions satisfied" "$out"
contains "it states outright that nothing was checked" "NO precondition was checked" "$out"

# ------------------------------------------------------------------------------
# Every file in the directory is read-only
# ------------------------------------------------------------------------------
# The checks below read the file with its COMMENTS stripped. The file explains at length why
# `SET default_transaction_read_only` does not work, so grepping the raw text trips over the
# explanation of the very thing being asserted - and a check that fails on prose about itself
# gets deleted by the next person who edits the comment.
sql_code() { grep -vE '^[[:space:]]*--' "$1"; }

for sql in "$SCRIPT_DIR"/preflight-db/*.sql; do
  [[ -e "$sql" ]] || continue
  name="$(basename "$sql")"
  code="$(sql_code "$sql")"

  # "SET default_transaction_read_only" was the first attempt and does NOT work: psql autocommits
  # each statement, so the setting governs a transaction that has already ended and a write
  # appended after the query sails straight through. A guarantee that is believed on the strength
  # of a comment is worse than none.
  lacks "$name does not rely on the session setting alone" "SET default_transaction_read_only" "$code"
  contains "$name opens an explicit transaction" "BEGIN;" "$code"
  contains "$name is read-only for that transaction" "SET TRANSACTION READ ONLY" "$code"
  contains "$name closes the transaction" "COMMIT;" "$code"

  # It must not be able to stop the deploy by raising either - a precondition reports, a
  # migration raises.
  lacks "$name does not raise" "RAISE EXCEPTION" "$code"
done

echo
echo "  ${pass} passed, ${fail} failed"
[[ $fail -eq 0 ]] || exit 1