#!/usr/bin/env bash
# Tests the `Migration dry run: dump` shell block in .github/workflows/ci.yml.
#
# WHY THIS NEEDS A TEST
# This block runs only on the self-hosted production box, only when a PR touches a migration, and
# it has never been green: it failed the first time it ever ran (PR #1149, 2026-10-15) with
#     psql: unrecognized option: schema-only
# so the gate has been reporting red for every migration PR while proving nothing about migrations.
# Nothing about it is observable from a laptop - it needs a shell, a `docker` and a live psql.
#
# As with changes-detector.test.sh, the block is EXTRACTED from ci.yml rather than copied here, and
# run against a stub `docker` that records the argv it was handed. A pasted copy would keep passing
# after ci.yml changed, which is the whole failure mode. The stub is what turns "psql rejected an
# option" into "here is exactly the argv psql received", which is the only way to tell a quoting
# bug from a client too old to know the flag.
#
# Run: bash deploy/dryrun-dump.test.sh
set -uo pipefail
cd "$(dirname "$0")/.."

WORK="$(mktemp -d)"
trap 'rm -rf "$WORK"' EXIT

CI_YML=".github/workflows/ci.yml"
STEP_NAME="Dump schema, history and catalogue from production"

# Body lines of a `run: |` under this step are indented 10 spaces; the block ends at the next line
# that is not, which is the next `- name:`.
awk -v step="$STEP_NAME" '
  $0 ~ "^      - name: " step "$" { in_step = 1; next }
  in_step && /^        run: \|$/     { in_run = 1; next }
  in_run {
    if ($0 ~ /^[[:space:]]*$/) { print ""; next }
    if ($0 !~ /^          /) exit
    sub(/^ {10}/, "")
    print
  }
' "$CI_YML" > "$WORK/dump.raw"

if ! grep -q 'pg()' "$WORK/dump.raw"; then
  echo "::error::could not extract the dump step from $CI_YML - did the step get renamed or re-indented?"
  exit 1
fi

pass=0
fail=0
ok()  { pass=$((pass + 1)); printf '  ok    %s\n' "$1"; }
bad() { fail=$((fail + 1)); printf '  FAIL  %s\n        got:      %s\n        expected: %s\n' "$1" "$2" "$3"; }
eq()  { if [ "$2" = "$3" ]; then ok "$1"; else bad "$1" "$2" "$3"; fi; }

# A `docker` stub: records its argv one-per-line, and hands back plausible output so the block runs
# to completion instead of dying on the first missing byte.
# LEAK_INTO names a table whose COPY output should contain a qr_payload VALUE - i.e. someone widened
# TABLES or a column mapping changed. That is what the guard exists to catch.
mkdir -p "$WORK/bin"
cat > "$WORK/bin/docker" <<'STUB'
#!/usr/bin/env bash
printf '%s\n' "$@" >> "$ARGV_LOG"
case " $* " in
  # A schema-only dump is DDL. It legitimately mentions qr_payload as a COLUMN NAME - that string
  # being present is not a leak, and modelling it here is what keeps the guard honest: without it
  # this harness cannot tell a real guard from one that refuses every run.
  *--schema-only*) cat <<'SCHEMA'
--
-- Name: fuel_vouchers; Type: TABLE; Schema: public
--
COMMENT ON COLUMN fuel_vouchers.qr_payload IS 'the signed voucher payload';
SCHEMA
  ;;
  *COPY*__EFMigrationsHistory*) echo "MigrationId,ProductVersion"; echo "20240101_Initial,1.0.0" ;;
  *COPY*fuel_types*)            echo "id,name"; echo "1,A-95" ;;
  *COPY*stations*)              echo "id,name"; echo "1,OKKO" ;;
  *COPY*fuel_packages*)         echo "id,station_id"; echo "1,1" ;;
  *COPY*)                       [ -n "${LEAK_INTO:-}" ] && printf 'number,qr_payload\n1234,eyJzaWduYXR1cmUiOiJ4In0=\n' ;;
esac
exit 0
STUB
chmod +x "$WORK/bin/docker"

# Runs the real extracted block and echoes the argv the stub docker collected.
run_dump() {
  local tables="${1:-fuel_types stations fuel_packages}"
  : > "$WORK/argv"
  ( PATH="$WORK/bin:$PATH" ARGV_LOG="$WORK/argv" RUNNER_TEMP="$WORK/runner" TABLES="$tables" \
    bash "$WORK/dump.raw" ) > "$WORK/out" 2>&1
  local rc=$?
  cat "$WORK/argv" 2>/dev/null
  return $rc
}

echo "migration dry-run dump (real block from $CI_YML)"

# The reason this gate has been red. `--schema-only` on a `psql` invocation is what the step used to
# run; the production box's psql rejected it. `pg_dump --schema-only` is the spelling that the SAME
# box already runs successfully every night in backup.sh:102, so it is the known-good form.
argv="$(run_dump)"
schema_line="$(printf '%s\n' "$argv" | grep -n -- '--schema-only' | head -1)"
schema_tool="$(printf '%s\n' "$argv" | grep -B99 -- '--schema-only' | tail -100 | grep -E '^(psql|pg_dump)$' | tail -1)"
eq "the schema dump asks pg_dump (not psql) for --schema-only" "$schema_tool" "pg_dump"

# Whatever replaced it, the schema dump must still be produced: owner and privilege stripping keeps
# the artifact restorable into a throwaway container, and dropping them would break the restore step.
argv="$(run_dump)"
printf '%s\n' "$argv" | grep -q -- '--no-owner' \
  && ok "the schema dump still strips owner" || bad "the schema dump still strips owner" "absent" "--no-owner"
printf '%s\n' "$argv" | grep -q -- '--no-privileges' \
  && ok "the schema dump still strips privileges" || bad "the schema dump still strips privileges" "absent" "--no-privileges"

# The rest of the step talks to psql, and must keep doing so - a COPY query is SQL, not a dump format.
argv="$(run_dump)"
printf '%s\n' "$argv" | grep -q '^psql$' \
  && ok "the COPY queries still go through psql" || bad "the COPY queries still go through psql" "no psql call" "a psql call"

# Both invocations name the same database explicitly, so the restore side connects to the same one.
argv="$(run_dump)"
missing=""
for want in -U fuelflow -d fuelflow; do
  printf '%s\n' "$argv" | grep -q -- "^$want$" || missing="$missing $want"
done
eq "every call carries -U fuelflow -d fuelflow" "$missing" ""

# The voucher-data guard, exercised by running the WHOLE extracted block and reading its exit code.
# The exit code is the assertion: 0 means the upload was allowed, non-zero means the block stopped.
# These two cases bracket the bug that shipped - a guard that scans the schema dump refuses every
# run, because "qr_payload" is a column name that pg_dump puts in the DDL.
run_block() { : > "$WORK/argv"; ( PATH="$WORK/bin:$PATH" ARGV_LOG="$WORK/argv" RUNNER_TEMP="$WORK/runner" TABLES="fuel_types stations fuel_packages" bash "$WORK/dump.raw" ) >"$WORK/out" 2>&1; }

# Baseline: the block as it stands, against a stub schema that mentions qr_payload as a column name.
run_block; rc=$?
eq "a schema-only dump naming qr_payload as a column still uploads" "$rc" "0"
if [ "$rc" -ne 0 ]; then
  echo "--- block output ---"; cat "$WORK/out"; echo "--- argv ---"; cat "$WORK/argv"; echo "----------------"
fi

# Now a real payload inside a dumped data file. The guard must fire, or the artifact is a data leak.
# Injected through the stub rather than by planting a file, because the block opens with
# `rm -rf "$out"` and would delete anything written before it ran - which is exactly how a guard
# test can pass for the wrong reason.
( PATH="$WORK/bin:$PATH" ARGV_LOG="$WORK/argv" RUNNER_TEMP="$WORK/runner" LEAK_INTO=1 \
  TABLES="fuel_types stations fuel_packages fuel_offers" bash "$WORK/dump.raw" ) >"$WORK/leak" 2>&1
rc=$?
if [ "$rc" -ne 0 ] && grep -q "refusing to upload" "$WORK/leak"; then
  ok "a qr_payload VALUE in a data file refuses the upload"
else
  bad "a qr_payload VALUE in a data file refuses the upload" "exit $rc, no refusal" "non-zero + refusal"
fi

# And the other arm: TABLES is widened to name the vouchers table outright, so a file with that name
# lands in the artifact whatever its content.
( PATH="$WORK/bin:$PATH" ARGV_LOG="$WORK/argv" RUNNER_TEMP="$WORK/runner" \
  TABLES="fuel_types stations fuel_packages fuel_vouchers" bash "$WORK/dump.raw" ) >"$WORK/wide" 2>&1
rc=$?
if [ "$rc" -ne 0 ] && [ -e "$WORK/runner/dryrun/fuel_vouchers.csv" ]; then
  ok "dumping the vouchers table still refuses the upload"
else
  bad "dumping the vouchers table still refuses the upload" "exit $rc" "non-zero, file present"
fi

echo
if [ "$fail" -eq 0 ]; then
  echo "all $pass dump case(s) pass"
else
  echo "$fail of $((pass + fail)) dump case(s) FAILED"
fi
exit "$fail"