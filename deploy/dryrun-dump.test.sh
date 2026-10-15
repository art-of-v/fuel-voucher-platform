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
# to completion instead of dying on the first missing byte. `DOCKER_FAIL_ON` names a substring that
# should be treated as "no such table", to drive the per-table warning branch.
mkdir -p "$WORK/bin"
cat > "$WORK/bin/docker" <<'STUB'
#!/usr/bin/env bash
printf '%s\n' "$@" >> "$ARGV_LOG"
case " $* " in
  *--schema-only*|*pg_dump*) echo "-- stub schema" ;;
  *COPY*__EFMigrationsHistory*) echo "MigrationId,ProductVersion"; echo "20240101_Initial,1.0.0" ;;
  *COPY*fuel_types*)            echo "id,name"; echo "1,A-95" ;;
  *COPY*stations*)              echo "id,name"; echo "1,OKKO" ;;
  *COPY*fuel_packages*)         echo "id,station_id"; echo "1,1" ;;
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

# The guard that refuses to ship voucher data. `grep -rlq` over the output dir is what catches a
# payload that landed in an unexpected file, and it has to keep firing.
run_dump > /dev/null
mkdir -p "$WORK/runner/dryrun"
echo "payload,qr_payload" > "$WORK/runner/dryrun/leaked.csv"
(
  PATH="$WORK/bin:$PATH" ARGV_LOG=/dev/null RUNNER_TEMP="$WORK/runner" \
    TABLES="fuel_types stations fuel_packages" bash -c '
    set -euo pipefail
    out="$RUNNER_TEMP/dryrun"
    if [ -e "$out/fuel_vouchers.csv" ] || grep -rlq "qr_payload" "$out" 2>/dev/null; then
      echo "REFUSED"
    fi'
) > "$WORK/guard" 2>&1
grep -q REFUSED "$WORK/guard" \
  && ok "a qr_payload in the dump dir still refuses the upload" \
  || bad "a qr_payload in the dump dir still refuses the upload" "accepted" "REFUSED"

# ...and a clean dump dir must NOT trip it, or the gate refuses every legitimate run.
rm -f "$WORK/runner/dryrun/leaked.csv"
(
  PATH="$WORK/bin:$PATH" ARGV_LOG=/dev/null RUNNER_TEMP="$WORK/runner" \
    TABLES="fuel_types stations fuel_packages" bash -c '
    set -euo pipefail
    out="$RUNNER_TEMP/dryrun"
    if [ -e "$out/fuel_vouchers.csv" ] || grep -rlq "qr_payload" "$out" 2>/dev/null; then
      echo "REFUSED"
    fi
    echo "ACCEPTED"'
) > "$WORK/clean" 2>&1
grep -q ACCEPTED "$WORK/clean" \
  && ok "a clean dump dir is still accepted" \
  || bad "a clean dump dir is still accepted" "refused" "accepted"

echo
if [ "$fail" -eq 0 ]; then
  echo "all $pass dump case(s) pass"
else
  echo "$fail of $((pass + fail)) dump case(s) FAILED"
fi
exit "$fail"