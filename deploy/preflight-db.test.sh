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
  #
  # psql writes its own output to stdout regardless of -t -A: BEGIN, the SET, and COMMIT each print a
  # line, and "(0 rows)" is printed for the result too. The first version of this stub modelled none
  # of that, so the counting bug below shipped: `grep -c .` counted BEGIN/SET/COMMIT and the check
  # reported "VIOLATED - 3 row(s)" against a clean database, then printed "(0 rows)" as the detail.
  # It fired on the first real deploy, which is how it was found.
  #
  # DOCKER_STUB_PSQL_FAILS answers the way psql does under ON_ERROR_STOP=1: the error on stderr,
  # nothing on stdout, exit 3. That is the shape of the bug this file's runner had - a query naming
  # a column that no longer exists, counted as zero rows, reported "ok".
  if [[ "${DOCKER_STUB_PSQL_FAILS:-0}" == "1" ]]; then
    echo 'ERROR:  column f.station_id does not exist' >&2
    echo 'LINE 1: ...' >&2
    exit 3
  fi
  if [[ "$*" == *"-t -A"* ]]; then
    printf 'BEGIN\nSET\n'
    [[ -n "${DOCKER_STUB_ROWS:-}" ]] && printf '%s\n' "$DOCKER_STUB_ROWS"
    printf '(0 rows)\nCOMMIT\n'
  else
    printf '  BEGIN\n  SET\n'
    printf '   station_id | fuel_type_id | data_kind | volumes | column_name | distinct_values\n'
    printf '  ------------+--------------+-----------+---------+-------------+-----------------\n'
    [[ -n "${DOCKER_STUB_ROWS:-}" ]] && printf '   %s\n' "$DOCKER_STUB_ROWS"
    printf '  (0 rows)\n  COMMIT\n'
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
# The count is read from the directory rather than written down, because a literal is a tripwire: the
# first assertion here said "(1 checked)", it went green when the directory held one file, and it went
# red on the deploy of the PR that added the second — asserting a number that has nothing to do with
# what this test is about. The number is an output detail; "a clean database says how many it checked"
# is the behaviour.
checked_expected="$(find "$SCRIPT_DIR/preflight-db" -name '*.sql' | wc -l | tr -d ' ')"

out="$(run DOCKER_STUB_RUNNING=1 DOCKER_STUB_ROWS= DOCKER_STUB_STRICT_UNUSED=1)"
check "a clean database exits 0" "0" "$?"

# The file named below must EXIST. It did not: `010-offer-volume-price-agreement.sql` was deleted on
# 2026-10-17 with its migration, and these three assertions kept naming it, so the deploy preflight
# went red on the next deploy with 33 passing and 3 failing and no hint that the file was gone.
# That is the same tripwire as the literal file COUNT two blocks up, in a different costume: a
# hardcoded name is a claim about the directory that nothing checks.
#
# Read from the directory instead, so deleting a precondition cannot leave the test naming a ghost.
violated_file="$(find "$SCRIPT_DIR/preflight-db" -name '*.sql' | sort | head -1)"

contains "the file is reported ok" "$(basename "$violated_file"): ok" "$out"
contains "and satisfaction is stated with a count" "all data preconditions that ran are satisfied (${checked_expected} checked)" "$out"
# The regression, stated on its own. psql prints BEGIN, SET and COMMIT to stdout whatever -t -A
# says, so counting non-empty lines calls those three a violation on a database that is fine — which
# is exactly what this check did on production on its first real run, printing
# "VIOLATED - 3 row(s)" above a detail of "(0 rows)".
lacks "psql's own chatter is not counted as a violation" "VIOLATED" "$out"

# ------------------------------------------------------------------------------
# A violated precondition
# ------------------------------------------------------------------------------
out="$(run DOCKER_STUB_RUNNING=1 DOCKER_STUB_ROWS='offer-1|klo|ft-1|real|10.00|2.50')"
check "a violation is a WARNING, not a failure, by default" "0" "$?"
contains "the violation is named" "VIOLATED" "$out"
contains "the offending file is named" "$(basename "$violated_file"): VIOLATED" "$out"
contains "the rows are shown, indented under the warning" "offer-1|klo|ft-1|real|10.00|2.50" "$out"
lacks "and it does not claim the preconditions were satisfied" "all data preconditions satisfied" "$out"

# ------------------------------------------------------------------------------
# Strict mode: the same database, now blocking
# ------------------------------------------------------------------------------
out="$(run DOCKER_STUB_RUNNING=1 DOCKER_STUB_ROWS='offer-1|klo|ft-1|real|10.00|2.50' PREFLIGHT_DB_STRICT=1)"
check "strict mode blocks the deploy" "1" "$?"
contains "and says why" "PREFLIGHT_DB_STRICT=1" "$out"
# The file name in the FAILURE LINE, not merely somewhere in the output. The violation warning above
# already prints the name, so a bare "contains <filename>" would pass against the old message that said
# only "N precondition(s) are violated (details above)" - which is the whole thing being fixed here.
# Matched on the punctuation the new message produces, so this fails without the change.
contains "and the failure line names the file to read" "are violated: $(basename "$violated_file")" "$out"
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
lacks "and it does NOT claim the preconditions were satisfied" "all data preconditions that ran are satisfied" "$out"
contains "it states outright that nothing was checked" "NO precondition was checked" "$out"

# ------------------------------------------------------------------------------
# The query itself fails
# ------------------------------------------------------------------------------
# The unreachable-database case above is the polite version of a check that did not run: the runner
# knew before it asked. This is the one that shipped. `011` grouped by `f.station_id` from
# 2026-10-18, `20261018191320_CatalogueBrandsBecomeUuid` renamed that column, and every deploy
# printed "011-fuel-type-name-twins.sql: ok" - because the runner sent psql's stderr to /dev/null,
# counted the empty stdout as zero rows, and psql exits 0 on a SQL error unless it is told not to.
# A gate that cannot run was a gate that said yes, for a week, on the production deploy path.
#
# DOCKER_STUB_PSQL_FAILS makes the stub answer the way psql does under ON_ERROR_STOP=1: the error
# on stderr, nothing on stdout, exit 3.
out="$(run DOCKER_STUB_RUNNING=1 DOCKER_STUB_ROWS= DOCKER_STUB_PSQL_FAILS=1)"
check "a query that cannot run is a warning, not a failure" "0" "$?"
contains "it says the check could not run" "COULD NOT RUN" "$out"
# The file is named, because "a check did not run" without saying which one is the same silence.
contains "and it names the file" ": COULD NOT RUN" "$out"
# And the psql error travels with it: "could not run" is not actionable, "column f.station_id does
# not exist" is.
contains "and it carries the error psql reported" "does not exist" "$out"
# The regression, stated as the thing that must NOT come back.
lacks "a failed query is never reported ok" ": ok" "$out"
lacks "and it does not claim the preconditions were satisfied" "are satisfied" "$out"
contains "it counts the unreadable file separately" "precondition(s) could NOT be run" "$out"

# A clean database under the same stub still passes, so the branch above is about the failure and
# not about every file being unreadable.
out="$(run DOCKER_STUB_RUNNING=1 DOCKER_STUB_ROWS= DOCKER_STUB_PSQL_FAILS=0)"
check "the same database with a working query still passes" "0" "$?"
contains "and reports the file ok" ": ok" "$out"

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

# ------------------------------------------------------------------------------
# Exactly one step-5, and it is the safe one
# ------------------------------------------------------------------------------
# This is the gap that shipped a duplicate. Every other assertion here drives the preflight through
# --data-only, which calls check_data_preconditions() - so the inline copy that a botched refactor
# left behind never ran under test, while the normal deploy path ran THAT one instead. The function
# carrying all the safety was dead code, and its two distinguishing features never appeared in a
# deploy log: the "(N checked)" count and the "NO precondition was checked" line.
#
# A duplicate definition is legal bash - the last one wins - so nothing errors. It just means the
# thing being tested is not the thing that runs. Hence a structural assertion rather than a
# behavioural one.
script="$(cat "$PREFLIGHT")"
banners="$(grep -c '== data preconditions ==' <<<"$script" || true)"
check "the data-preconditions step is defined exactly once" "1" "$banners"
lacks "no stale copy of the first version survives" "db_preconditions" "$script"
contains "the running version distinguishes 'clean' from 'did not run'" "NO precondition was checked" "$script"
contains "the running version says how many were checked" "satisfied (\${checked} checked)" "$script"

# #1143 deleted the duplicate block and took the only call on the NORMAL path with it, because the
# remaining call sits inside the --data-only branch. Every test here drives --data-only, so all 23
# passed while every deploy skipped the check, and the log simply ended after step 4. Counting calls
# cannot see that — one call existed and it was the wrong one. What matters is REACHABILITY: that
# step 5 is invoked from the ordinary flow, after the images step, and not only under a flag.
calls="$(grep -nE '^[[:space:]]*check_data_preconditions$' <<<"$script" | cut -d: -f1 | tr '\n' ' ')"
total="$(wc -l <<<"$script")"
# Two call sites: the --data-only branch, and the tail of the normal run. Both must exist.
check "the normal path reaches the data preconditions" "2" "$(wc -w <<<"$calls")"
last_call="$(grep -nE '^[[:space:]]*check_data_preconditions$' <<<"$script" | tail -1 | cut -d: -f1)"
if [[ -n "$last_call" && "$last_call" -gt $((total / 2)) ]]; then
  echo "  PASS  and the one on the normal path is the tail of the script"; pass=$((pass+1))
else
  echo "  FAIL  the normal-path call should sit at the end (found at line ${last_call:-none} of ${total})"; fail=$((fail+1))
fi
# Guard the tail so a failing [[ ]] there cannot become the script's exit status. That turned every
# deploy red on 2026-10-15 with no message: `set -e` plus a bare `[[ cond ]] && echo` as the final
# line exits 1 whenever the condition is false, which is the ordinary case.
if tail -5 "$PREFLIGHT" | grep -qE '^[[:space:]]*\[\[ .* \]\] && '; then
  echo "  FAIL  the script ends in a bare '[[ ]] && ...', whose exit status becomes the script's"; fail=$((fail+1))
else
  echo "  PASS  the script does not end in a bare '[[ ]] && ...'"; pass=$((pass+1))
fi

echo
echo "  ${pass} passed, ${fail} failed"
[[ $fail -eq 0 ]] || exit 1