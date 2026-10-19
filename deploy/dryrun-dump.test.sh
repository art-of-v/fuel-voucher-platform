#!/usr/bin/env bash
# Tests the migration dry run in .github/workflows/ci.yml - both halves, the dump on the production
# box and the restore on the hosted runner.
#
# WHY THIS NEEDS A TEST
# The dry run landed in #1149 and had never been green. It was not one bug but a chain of six, each
# one hiding the next because a failing step ends the job before the following step can also be
# wrong:
#   1. `psql --schema-only` is not a psql flag                -> the dump never produced a file
#   2. the restore job had no setup-dotnet and no dotnet-ef   -> masked by 1
#   3. the leak guard scanned the schema dump and matched the column name -> masked by 1
#   4. upload-artifact declared no `path`                    -> surfaced once 1-3 were fixed
#   5. download-artifact declared no `name`, so it pulled every artifact in the run -> masked by 4
#   6. the restore step resolved its paths from the workspace root, not the download directory
#   7. the apply step called `dotnet ef` with no `dotnet restore` before it -> NETSDK1004
# Fixing any one of them in isolation would have looked like progress and still been red.
#
# None of it is observable from a laptop - the dump half needs the production box and a live
# postgres, the restore half needs a service container. So the blocks are EXTRACTED from ci.yml and
# run here against stubs, rather than copied: a pasted copy keeps passing after ci.yml changes, which
# is the entire failure mode. The `docker` stub records its argv, which is the only way to tell a
# quoting bug from a client too old to know a flag.
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
  *COPY*brands*)                echo "id,name"; echo "okko,OKKO" ;;
        *COPY*fuel_nominals*)        echo "fuel_type_id,liters"; echo "1,10" ;;
 
  *COPY*)                       [ -n "${LEAK_INTO:-}" ] && printf 'number,qr_payload\n1234,eyJzaWduYXR1cmUiOiJ4In0=\n' ;;
esac
exit 0
STUB
chmod +x "$WORK/bin/docker"

# Runs the real extracted block and echoes the argv the stub docker collected.
run_dump() {
  local tables="${1:-brands fuel_types fuel_nominals}"
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
run_block() { : > "$WORK/argv"; ( PATH="$WORK/bin:$PATH" ARGV_LOG="$WORK/argv" RUNNER_TEMP="$WORK/runner" TABLES="brands fuel_types fuel_nominals" bash "$WORK/dump.raw" ) >"$WORK/out" 2>&1; }

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
  TABLES="brands fuel_types fuel_nominals fingerprint_leak" bash "$WORK/dump.raw" ) >"$WORK/leak" 2>&1
rc=$?
if [ "$rc" -ne 0 ] && grep -q "refusing to upload" "$WORK/leak"; then
  ok "a qr_payload VALUE in a data file refuses the upload"
else
  bad "a qr_payload VALUE in a data file refuses the upload" "exit $rc, no refusal" "non-zero + refusal"
fi

# And the other arm: TABLES is widened to name the vouchers table outright, so a file with that name
# lands in the artifact whatever its content.
( PATH="$WORK/bin:$PATH" ARGV_LOG="$WORK/argv" RUNNER_TEMP="$WORK/runner" \
  TABLES="brands fuel_types fuel_nominals fuel_vouchers" bash "$WORK/dump.raw" ) >"$WORK/wide" 2>&1
rc=$?
if [ "$rc" -ne 0 ] && [ -e "$WORK/runner/dryrun/fuel_vouchers.csv" ]; then
  ok "dumping the vouchers table still refuses the upload"
else
  bad "dumping the vouchers table still refuses the upload" "exit $rc" "non-zero, file present"
fi

# The upload step. It shipped with no `path:` at all, so upload-artifact exited "Input required and
# not supplied: path" - invisible until the dump block above started succeeding. Asserted here as
# text because the failure is a missing key in this file, not anything the shell block can reach.
upload_block="$(awk '/^      - name: Upload the dump$/ { f = 1 } f && /^  [a-z]/ && !/Upload the dump/ { exit } f' "$CI_YML")"
if printf '%s\n' "$upload_block" | grep -q '^ *path:'; then
  ok "the upload step declares a path"
else
  bad "the upload step declares a path" "no path: key" "a path: key"
fi
# It has to point at the directory the guard scans, not a hand-listed set of files - a file list
# here is a second place the dump contents are named, outside the guard's reach.
if printf '%s\n' "$upload_block" | grep -qE '^ *path:.*dryrun'; then
  ok "the upload path is the guarded dump directory"
else
  bad "the upload path is the guarded dump directory" "$(printf '%s\n' "$upload_block" | grep '^ *path:' || echo none)" "a path under dryrun"
fi

# ---- the restore half -------------------------------------------------------------------
# download-artifact with no `name` pulls every artifact in the run into the workspace root, so the
# restore's relative paths resolve against the repository instead of the dump.
dl_block="$(awk '/^      - name: Download the dump$/ { f = 1 } f && /^  [a-z]/ && !/Download the dump/ { exit } f' "$CI_YML")"
if printf '%s\n' "$dl_block" | grep -q '^ *name:'; then
  ok "the download step names the artifact"
else
  bad "the download step names the artifact" "no name: key - pulls every artifact in the run" "a name: key"
fi
# The upload and download must name the SAME artifact; a rename on one side alone silently
# restores nothing.
up_name="$(printf '%s\n' "$upload_block" | grep '^ *name:' | head -1 | sed 's/^ *name: *//')"
dl_name="$(printf '%s\n' "$dl_block"   | grep '^ *name:' | head -1 | sed 's/^ *name: *//')"
eq "upload and download name the same artifact" "$up_name" "$dl_name"

# The restore block, extracted and run for real against a stub psql in a throwaway tree laid out
# the way the workflow lays it out. This is what pins defect 6: without the `cd`, the csv glob
# matches the repository's own files and the restore prints "restored" having loaded nothing.
awk '
  /^      - name: Restore the dump$/ { in_step = 1; next }
  in_step && /^        run: \|$/   { in_run = 1; next }
  in_run {
    if ($0 ~ /^[[:space:]]*$/) { print ""; next }
    if ($0 !~ /^          /) exit
    sub(/^ {10}/, "")
    print
  }
' "$CI_YML" > "$WORK/restore.raw"

if ! grep -q 'copy' "$WORK/restore.raw"; then
  echo "::error::could not extract the restore step from $CI_YML"
  exit 1
fi

cat > "$WORK/bin/psql" <<'STUB'
#!/usr/bin/env bash
printf '%s\n' "$*" >> "$PSQL_LOG"
case " $* " in
  *-f\ schema.sql*) echo "-- schema applied" ;;
  *copy*)            echo "COPY 1" ;;
  *count*)           echo "42" ;;
esac
exit 0
STUB
chmod +x "$WORK/bin/psql"

# Builds a tree shaped like the runner's: a workspace root holding a decoy CSV (what the repository
# itself contains) and the artifact under ./dryrun.
: > "$WORK/psql.log"
mkdir -p "$WORK/restore-tree/dryrun"
echo "decoy,not,ours" > "$WORK/restore-tree/decoy.csv"
echo "MigrationId,ProductVersion" > "$WORK/restore-tree/dryrun/history.csv"
echo "id,name" > "$WORK/restore-tree/dryrun/brands.csv"
echo "id,name" > "$WORK/restore-tree/dryrun/fuel_types.csv"
echo "fuel_type_id,liters" > "$WORK/restore-tree/dryrun/fuel_nominals.csv"

echo "-- schema" > "$WORK/restore-tree/dryrun/schema.sql"
( cd "$WORK/restore-tree" && PATH="$WORK/bin:$PATH" PSQL_LOG="$WORK/psql.log" PGPASSWORD=postgres \
    bash "$WORK/restore.raw" ) > "$WORK/restore.out" 2>&1
rc=$?

if [ "$rc" -ne 0 ]; then
  bad "the restore step runs against the downloaded dump" "exit $rc: $(tail -2 "$WORK/restore.out" | tr '\n' ' ')" "exit 0"
else
  ok "the restore step runs against the downloaded dump"
fi
# The decoy sits in the workspace root on purpose. If the block never changes directory, `*.csv`
# picks it up and a table named "decoy" is restored - the silent-wrong-answer failure mode.
if grep -q 'decoy' "$WORK/psql.log"; then
  bad "the restore never reads CSVs from outside the dump directory" "restored decoy.csv" "dump dir only"
else
  ok "the restore never reads CSVs from outside the dump directory"
fi
if grep -q 'fuel_types' "$WORK/psql.log"; then
  ok "the restore loads the tables it was given"
else
  bad "the restore loads the tables it was given" "no fuel_types" "fuel_types restored"
fi

# The order the schema demands, asserted rather than assumed. fuel_types.brand_id references
# brands.id, and a glob-ordered restore is one edit away from loading the child first - which is what
# the real dry run did on 2026-10-16, dying on FK_fuel_offer_volumes_fuel_offers_offer_id against a
# perfectly good dump. Both of those tables are gone; the chain the failure demonstrated on is not,
# and this PR adds six more brand_id foreign keys along it, so the assertion stays - on the child
# that is actually in the order now.
brands_line="$(grep -ni 'copy brands ' "$WORK/psql.log" | head -1 | cut -d: -f1)"
child_line="$(grep -ni 'copy fuel_types ' "$WORK/psql.log" | head -1 | cut -d: -f1)"
if [ -n "$brands_line" ] && [ -n "$child_line" ] && [ "$brands_line" -lt "$child_line" ]; then
  ok "the parent is restored before the child that references it"
else
  bad "the parent is restored before the child that references it" \
    "brands at '${brands_line:-none}', fuel_types at '${child_line:-none}'" "brands first"
fi

# A table in the dump that the restore order does not name is a silent gap, and the loop must say
# so rather than skipping it quietly. This is what stops the next dump change from repeating this.
: > "$WORK/psql.log"
echo "id,x" > "$WORK/restore-tree/dryrun/brand_new_table.csv"
( cd "$WORK/restore-tree" && PATH="$WORK/bin:$PATH" PSQL_LOG="$WORK/psql.log" PGPASSWORD=postgres \
    bash "$WORK/restore.raw" ) > "$WORK/restore-unknown.out" 2>&1
rc=$?
if [ "$rc" -ne 0 ] && grep -q 'brand_new_table' "$WORK/restore-unknown.out"; then
  ok "an unnamed table in the dump fails the restore loudly"
else
  bad "an unnamed table in the dump fails the restore loudly" \
      "exit $rc, output: $(tail -1 "$WORK/restore-unknown.out")" "non-zero exit naming the table"
fi
rm -f "$WORK/restore-tree/dryrun/brand_new_table.csv"

# The other direction, and the one that actually bit: a table the RESTORE ORDER names that the dump
# does not carry. Until 2026-10-18 that was a warning and the loop continued, so a stale name in the
# order (it still said `stations` after the rename to `brands`) restored an empty table, the
# migrations then ran against a database with no brands at all, and the uuid migration's orphan guard
# fired on nineteen rows that were not orphans. The two lists live in the same file and are meant to
# agree; when they do not, that is a bug in the gate and must not be a green run.
mv "$WORK/restore-tree/dryrun/brands.csv" "$WORK/brands.csv.hidden"
: > "$WORK/psql.log"
( cd "$WORK/restore-tree" && PATH="$WORK/bin:$PATH" PSQL_LOG="$WORK/psql.log" PGPASSWORD=postgres \
    bash "$WORK/restore.raw" ) > "$WORK/restore-missing.out" 2>&1
rc=$?
if [ "$rc" -ne 0 ] && grep -q 'brands' "$WORK/restore-missing.out"; then
  ok "a table in the restore order but not in the dump fails the restore loudly"
else
  bad "a table in the restore order but not in the dump fails the restore loudly" \
      "exit $rc, output: $(tail -1 "$WORK/restore-missing.out")" "non-zero exit naming the table"
fi
mv "$WORK/brands.csv.hidden" "$WORK/restore-tree/dryrun/brands.csv"

# The migration step. `dotnet ef` builds the project it targets, so a step that does not restore
# first dies on a fresh runner with NETSDK1004 and never reaches a migration at all. Asserted as
# text: whether restore actually succeeds needs the network, which this harness deliberately has no
# stake in.
migrate_block="$(awk '/^      - name: Apply pending migrations$/ { f = 1 } f && /^  [a-z]/ && !/Apply pending migrations/ { exit } f' "$CI_YML")"
if printf '%s\n' "$migrate_block" | grep -q 'dotnet restore'; then
  ok "the apply step restores before calling dotnet ef"
else
  bad "the apply step restores before calling dotnet ef" "no dotnet restore - dies on NETSDK1004" "a dotnet restore"
fi
if printf '%s\n' "$migrate_block" | grep -q 'dotnet ef database update'; then
  ok "the apply step still runs dotnet ef database update"
else
  bad "the apply step still runs dotnet ef database update" "missing" "dotnet ef database update"
fi

echo
if [ "$fail" -eq 0 ]; then
  echo "all $pass dry-run case(s) pass"
else
  echo "$fail of $((pass + fail)) dry-run case(s) FAILED"
fi
exit "$fail"
