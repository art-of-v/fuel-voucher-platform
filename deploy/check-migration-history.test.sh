#!/usr/bin/env bash
# Tests deploy/check-migration-history.sh - the gate that refuses a repository which has lost a
# migration production has already applied.
#
# WHY THIS NEEDS A TEST
#
# The gate's whole value is that it fails when it should. A comparison that never fails is
# indistinguishable from a comparison that never runs, and the thing it guards against - a deleted
# migration file - is exactly the kind of change that looks like a tidy-up in a diff. It happened
# once already, on 2026-09-11, and nothing in the build noticed for two months.
#
# The comparison is over two text files, so it can be tested without a database, without EF and
# without a build. The ci.yml step that CALLS the script is checked too, because a gate that is
# not wired into the workflow is a script nobody runs.
#
# Run: bash deploy/check-migration-history.test.sh
set -uo pipefail
cd "$(dirname "$0")/.."

WORK="$(mktemp -d)"
trap 'rm -rf "$WORK"' EXIT

SCRIPT="deploy/check-migration-history.sh"
CI_YML=".github/workflows/ci.yml"
STEP_NAME="Every applied migration is still in the repository"

pass=0
fail=0
ok()  { pass=$((pass + 1)); printf '  ok    %s\n' "$1"; }
bad() { fail=$((fail + 1)); printf '  FAIL  %s\n        got:      %s\n        expected: %s\n' "$1" "$2" "$3"; }
eq()  { if [ "$2" = "$3" ]; then ok "$1"; else bad "$1" "$2" "$3"; fi; }

write() { printf '%s\n' "$@" > "$1"; }

# ---------------------------------------------------------------- the happy path
write "$WORK/h1.ids" 20260617125815_InitialCreate 20260617133502_AddMonobankPaymentUrl
write "$WORK/a1.ids" 20260617125815_InitialCreate 20260617133502_AddMonobankPaymentUrl 20260625144527_ReplaceFuelTypeEnum

out="$(bash "$SCRIPT" "$WORK/h1.ids" "$WORK/a1.ids" 2>&1)"; code=$?
eq "the same migrations on both sides pass" "$code" "0"

# The assembly may hold migrations production has not applied yet - that is what a PENDING
# migration is, and failing on it would make the gate fail on every pull request with a migration.
out="$(bash "$SCRIPT" "$WORK/h1.ids" "$WORK/a1.ids" 2>&1)"
if echo "$out" | grep -q "every applied migration is still in the repository"; then
  ok "it says what it checked"
else
  bad "it says what it checked" "$out" "a line saying the check passed"
fi

# ---------------------------------------------------------------- the failure that matters
write "$WORK/h2.ids" 20260617125815_InitialCreate 20260803120600_AddRefunds 20260817191435_AddOrderLegalEntity
write "$WORK/a2.ids" 20260617125815_InitialCreate

out="$(bash "$SCRIPT" "$WORK/h2.ids" "$WORK/a2.ids" 2>&1)"; code=$?
eq "a migration production applied and the repository lost is refused" "$code" "1"

if echo "$out" | grep -q "20260803120600_AddRefunds" && echo "$out" | grep -q "20260817191435_AddOrderLegalEntity"; then
  ok "it names every missing migration, not just the first"
else
  bad "it names every missing migration, not just the first" "$out" "both ids in the output"
fi

# The message has to say what to do, not only what went wrong.
if echo "$out" | grep -qi "restore the files"; then
  ok "it says what to do about it"
else
  bad "it says what to do about it" "$out" "a line saying to restore the files"
fi

# ---------------------------------------------------------------- the compensated list
# Eight migrations were deleted on 2026-09-11 and are compensated for by
# 20260919113317_AddMissingDriftedTables, which re-creates what they created. Restoring them
# breaks every database that was migrated from zero while they were gone - which is how staging
# stopped booting on 2026-10-19 with 42P07 "relation refunds already exists".
write "$WORK/c1.txt" "# a comment" "20260803120600_AddRefunds" "20260817191435_AddOrderLegalEntity"
out="$(bash "$SCRIPT" "$WORK/h2.ids" "$WORK/a2.ids" "$WORK/c1.txt" 2>&1)"; code=$?
eq "a compensated migration is not a failure" "$code" "0"

# One id compensated and one not: the one that is not must still fail, and only it.
write "$WORK/c2.txt" "20260803120600_AddRefunds"
out="$(bash "$SCRIPT" "$WORK/h2.ids" "$WORK/a2.ids" "$WORK/c2.txt" 2>&1)"; code=$?
eq "a partly compensated set is still a failure" "$code" "1"
if echo "$out" | grep -q "20260817191435_AddOrderLegalEntity" && ! echo "$out" | grep -q "20260803120600_AddRefunds"; then
  ok "and it names only the uncompensated one"
else
  bad "and it names only the uncompensated one" "$out" "20260817191435_AddOrderLegalEntity and not 20260803120600_AddRefunds"
fi

# The real list, as it is in the repository: it has to cover exactly the eight migrations that
# were deleted, or the gate is either red or blind.
if [ -f "deploy/compensated-migration-ids.txt" ]; then
  real_count="$(grep -cE '^[0-9]{14}_' deploy/compensated-migration-ids.txt)"
  eq "the repository's compensated list names the eight deleted migrations" "$real_count" "8"
else
  bad "the repository has a compensated list" "" "deploy/compensated-migration-ids.txt"
fi

# ---------------------------------------------------------------- the shapes around it
# Blank lines and anything that is not a migration id are noise, not history.
printf '\n20260617125815_InitialCreate\nnot-a-migration\n\n' > "$WORK/h3.ids"
write "$WORK/a3.ids" 20260617125815_InitialCreate
out="$(bash "$SCRIPT" "$WORK/h3.ids" "$WORK/a3.ids" 2>&1)"; code=$?
eq "blank lines and non-migration lines in the history are ignored" "$code" "0"

# The same id twice in the history is one missing migration, not two.
write "$WORK/h4.ids" 20260617125815_InitialCreate 20260803120600_AddRefunds 20260803120600_AddRefunds
write "$WORK/a4.ids" 20260617125815_InitialCreate
out="$(bash "$SCRIPT" "$WORK/h4.ids" "$WORK/a4.ids" 2>&1)"; code=$?
eq "a duplicated id is still a failure" "$code" "1"
eq "and it is reported once" "$(echo "$out" | grep -c '20260803120600_AddRefunds')" "1"

# A missing file is a broken invocation, not a pass.
out="$(bash "$SCRIPT" "$WORK/nope.ids" "$WORK/a1.ids" 2>&1)"; code=$?
eq "a missing history file is an error, not a pass" "$code" "2"

out="$(bash "$SCRIPT" 2>&1)"; code=$?
eq "no arguments is an error, not a pass" "$code" "2"

# ---------------------------------------------------------------- the step is wired in
# The script is only a gate if the workflow calls it. A script that is correct and never run is
# the same failure as no script at all, and this job's whole history is steps that were written
# and then not reachable.
step_body="$(awk -v step="$STEP_NAME" '
  $0 ~ "^      - name: " step "$" { in_step = 1; next }
  in_step && /^        run: \|$/     { in_run = 1; next }
  in_run {
    if ($0 ~ /^[[:space:]]*$/) { print ""; next }
    if ($0 !~ /^          /) exit
    sub(/^ {10}/, "")
    print
  }
' "$CI_YML")"

if [ -z "$step_body" ]; then
  bad "the workflow has the step" "" "a step named '$STEP_NAME'"
else
  ok "the workflow has the step"

  if echo "$step_body" | grep -q 'check-migration-history.sh'; then
    ok "and the step calls the script"
  else
    bad "and the step calls the script" "$step_body" "a call to check-migration-history.sh"
  fi

  # The history has to come from the restored production database, not from somewhere local.
  if echo "$step_body" | grep -q '__EFMigrationsHistory'; then
    ok "and the history comes from the database"
  else
    bad "and the history comes from the database" "$step_body" "a query against __EFMigrationsHistory"
  fi

  # And the assembly side has to be EF's own listing, not a guess at the file names: a migration
  # file that EF cannot discover is invisible to a file listing, and that is the other half of
  # what this gate is for.
  if echo "$step_body" | grep -q 'dotnet ef migrations list'; then
    ok "and the assembly side is EF's own listing"
  else
    bad "and the assembly side is EF's own listing" "$step_body" "a call to dotnet ef migrations list"
  fi
fi

printf '\n  %d passed, %d failed\n' "$pass" "$fail"
[ "$fail" -eq 0 ]
