#!/usr/bin/env bash
# Tests deploy/prune-wal-archive.sh - the job that trims the WAL archive.
#
# WHY THIS TEST EXISTS
#
# The trim is the guard that stands between a full disk and a database that stops
# accepting writes:
#
#   FATAL: could not write lock file "postmaster.pid": No space left on device
#
# That is not hypothetical on this box; it happened on 2026-09-27. The trimming
# function itself is already covered by deploy/pitr-common.test.sh. What is NOT
# covered is the part that is new here: that the JOB runs, on its own, with the
# right bounds, and that it says so when the disk is still tight afterwards.
#
# A guard that has never been exercised is a belief.
#
# No database, no network and no age key: the trim works on a directory of files,
# so the test is a directory of files.
# ------------------------------------------------------------------------------
set -uo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
SCRIPT="$SCRIPT_DIR/prune-wal-archive.sh"

TMP="$(mktemp -d "${TMPDIR:-/tmp}/prune-wal-archive-test.XXXXXX")"
trap 'rm -rf "$TMP"' EXIT

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
check_not_contains() {
  local what="$1" needle="$2" haystack="$3"
  if [[ "$haystack" != *"$needle"* ]]; then
    echo "  PASS  $what"; pass=$((pass + 1))
  else
    echo "  FAIL  $what (output unexpectedly contained '$needle')"; fail=$((fail + 1))
  fi
}

# A WAL segment with a controlled age. PostgreSQL's segment names are 24 hex
# characters; the trim goes by mtime, not by name, so the name only has to look
# like one.
make_segment() {
  local dir="$1" name="$2" days_old="$3" size_kb="$4"
  dd if=/dev/zero of="$dir/$name" bs=1024 count="$size_kb" >/dev/null 2>&1
  touch -d "$days_old days ago" "$dir/$name" 2>/dev/null || touch -t 202001010000 "$dir/$name"
}

count_segments() { find "$1" -maxdepth 1 -name '0000000*' -type f 2>/dev/null | wc -l | tr -d ' '; }

echo "=== the script exists and is executable ==="
if [[ -x "$SCRIPT" ]]; then
  echo "  PASS  prune-wal-archive.sh is executable"; pass=$((pass + 1))
else
  echo "  FAIL  prune-wal-archive.sh is not executable"; fail=$((fail + 1))
fi

echo "=== the age bound: an old segment goes, a recent one stays ==="
ARCHIVE="$TMP/archive"
mkdir -p "$ARCHIVE"
make_segment "$ARCHIVE" "000000010000000000000001" 10 64   # 10 days old
make_segment "$ARCHIVE" "000000010000000000000002" 0 64    # now
out="$(WAL_ARCHIVE_DIR="$ARCHIVE" \
       STAGING_WAL_ARCHIVE_DIR="$TMP/none" \
       WAL_RETENTION_DAYS=3 \
       WAL_MAX_BYTES=8589934592 \
       WATCH_PATH="$TMP" \
       DISK_ALERT_THRESHOLD=100 \
       bash "$SCRIPT" 2>&1)"
rc=$?
check "the script exits 0" "0" "$rc"
check "the 10-day-old segment was removed" "1" "$(count_segments "$ARCHIVE")"
check_contains "it says what it did" "production" "$out"

echo "=== the size bound: the newest segment is never removed ==="
# Ten segments of 1 MiB, the oldest first by mtime. A cap of 4 MiB cannot hold
# them all, so the trim has to delete - and it must stop before the one being
# written to.
ARCHIVE2="$TMP/size"
mkdir -p "$ARCHIVE2"
for i in 1 2 3 4 5 6 7 8 9 10; do
  make_segment "$ARCHIVE2" "0000000100000000000000$(printf '%02d' "$i")" "$(( 11 - i ))" 1024
done
out="$(WAL_ARCHIVE_DIR="$ARCHIVE2" \
       STAGING_WAL_ARCHIVE_DIR="$TMP/none" \
       WAL_RETENTION_DAYS=365 \
       WAL_MAX_BYTES=4194304 \
       WATCH_PATH="$TMP" \
       DISK_ALERT_THRESHOLD=100 \
       bash "$SCRIPT" 2>&1)"
rc=$?
check "the script exits 0" "0" "$rc"
remaining="$(count_segments "$ARCHIVE2")"
if (( remaining >= 1 )); then
  echo "  PASS  the newest segment survived the size bound"; pass=$((pass + 1))
else
  echo "  FAIL  the size bound removed every segment, including the newest"; fail=$((fail + 1))
fi
if (( remaining < 10 )); then
  echo "  PASS  the size bound actually trimmed something"; pass=$((pass + 1))
else
  echo "  FAIL  the size bound removed nothing"; fail=$((fail + 1))
fi
newest="$(find "$ARCHIVE2" -maxdepth 1 -name '0000000*' -type f -printf '%T@ %p\n' 2>/dev/null | sort -n | tail -1 | cut -d' ' -f2-)"
check "the newest segment is still there" "000000010000000000000010" "$(basename "$newest")"

echo "=== the disk check: a tight disk is a non-zero exit, not a quiet success ==="
# A threshold of 0 means "always above it", which is how the test says "pretend
# the disk is full" without needing a full disk.
out="$(WAL_ARCHIVE_DIR="$TMP/none" \
       STAGING_WAL_ARCHIVE_DIR="$TMP/none" \
       WATCH_PATH="$TMP" \
       DISK_ALERT_THRESHOLD=0 \
       bash "$SCRIPT" 2>&1)"
rc=$?
check "the script exits non-zero when the disk is above the threshold" "1" "$rc"
check_contains "it says which path is tight" "$TMP" "$out"
check_contains "it says the threshold" "threshold 0%" "$out"

echo "=== a missing archive directory is not an error ==="
# The archive only exists once PITR is switched on. A box without it must not
# have a failing timer.
out="$(WAL_ARCHIVE_DIR="$TMP/does-not-exist" \
       STAGING_WAL_ARCHIVE_DIR="$TMP/also-missing" \
       WATCH_PATH="$TMP" \
       DISK_ALERT_THRESHOLD=100 \
       bash "$SCRIPT" 2>&1)"
rc=$?
check "the script exits 0" "0" "$rc"
check_contains "it says there was nothing to do" "nothing to do" "$out"

echo "=== the staging archive is trimmed too ==="
# They share the disk: a staging archive that fills the disk takes production down
# with it. So the script has to touch both.
ARCHIVE3="$TMP/prod"
ARCHIVE4="$TMP/staging"
mkdir -p "$ARCHIVE3" "$ARCHIVE4"
make_segment "$ARCHIVE3" "000000010000000000000001" 0 64
make_segment "$ARCHIVE4" "000000010000000000000001" 10 64
out="$(WAL_ARCHIVE_DIR="$ARCHIVE3" \
       STAGING_WAL_ARCHIVE_DIR="$ARCHIVE4" \
       WAL_RETENTION_DAYS=3 \
       WAL_MAX_BYTES=8589934592 \
       WATCH_PATH="$TMP" \
       DISK_ALERT_THRESHOLD=100 \
       bash "$SCRIPT" 2>&1)"
rc=$?
check "the script exits 0" "0" "$rc"
check "the staging archive's old segment was removed" "0" "$(count_segments "$ARCHIVE4")"
check "the production archive was left alone" "1" "$(count_segments "$ARCHIVE3")"

echo "=== the units and the installer are present ==="
for f in systemd/fuelflow-wal-prune.service systemd/fuelflow-wal-prune.timer install-wal-prune-schedule.sh; do
  if [[ -f "$SCRIPT_DIR/$f" ]]; then
    echo "  PASS  $f exists"; pass=$((pass + 1))
  else
    echo "  FAIL  $f is missing"; fail=$((fail + 1))
  fi
done

# A unit whose ExecStart points at a path that does not exist looks healthy until
# the timer fires. The installer checks this on the box; this checks the repo.
if grep -q "ExecStart=/root/FuelFlow/deploy/prune-wal-archive.sh" "$SCRIPT_DIR/systemd/fuelflow-wal-prune.service"; then
  echo "  PASS  the service runs the script from the path the installer expects"; pass=$((pass + 1))
else
  echo "  FAIL  the service's ExecStart does not point at /root/FuelFlow/deploy/prune-wal-archive.sh"; fail=$((fail + 1))
fi

# The whole point of the unit is that it runs when the backup does not. A timer
# that fires once a day is the thing this change exists to stop.
if grep -qE "^OnCalendar=.*(00|06|12|18)" "$SCRIPT_DIR/systemd/fuelflow-wal-prune.timer"; then
  echo "  PASS  the timer fires more than once a day"; pass=$((pass + 1))
else
  echo "  FAIL  the timer does not fire more than once a day"; fail=$((fail + 1))
fi

echo "=== the alert says WHICH unit failed ==="
# Two units share the alert, and a message that names the wrong one sends a human
# to the wrong place at 3am. So the notifier is run for real, with a fake `logger`
# on the PATH to capture what it would have written.
FAKEBIN="$TMP/bin"
mkdir -p "$FAKEBIN"
cat > "$FAKEBIN/logger" <<'FAKE'
#!/usr/bin/env bash
# APPEND, not overwrite: the notifier writes the alert to the journal and then, if
# Telegram is not configured, writes a second line saying so. Overwriting would
# throw the alert away and leave only the note.
cat >> "${FAKE_LOGGER_OUT:-/dev/null}"
FAKE
cat > "$FAKEBIN/journalctl" <<'FAKE'
#!/usr/bin/env bash
echo "(fake journal output)"
FAKE
cat > "$FAKEBIN/curl" <<'FAKE'
#!/usr/bin/env bash
exit 0
FAKE
chmod +x "$FAKEBIN/logger" "$FAKEBIN/journalctl" "$FAKEBIN/curl"

PATH="$FAKEBIN:$PATH" FAKE_LOGGER_OUT="$TMP/wal.log" \
  bash "$SCRIPT_DIR/notify-prune-failure.sh" fuelflow-wal-prune.service >/dev/null 2>&1
wal_msg="$(cat "$TMP/wal.log" 2>/dev/null || true)"
check_contains "the WAL trim's alert names the WAL trim" "WAL ARCHIVE TRIM FAILED" "$wal_msg"
check_not_contains "the WAL trim's alert does not call itself a Docker GC" "Docker GC FAILED" "$wal_msg"

PATH="$FAKEBIN:$PATH" FAKE_LOGGER_OUT="$TMP/docker.log" \
  bash "$SCRIPT_DIR/notify-prune-failure.sh" fuelflow-prune.service >/dev/null 2>&1
docker_msg="$(cat "$TMP/docker.log" 2>/dev/null || true)"
check_contains "the Docker GC's alert still says Docker GC" "Docker GC FAILED" "$docker_msg"
check_not_contains "the Docker GC's alert does not mention the WAL trim" "WAL ARCHIVE TRIM FAILED" "$docker_msg"

echo
echo "passed: $pass  failed: $fail"
[[ "$fail" -eq 0 ]] || exit 1
exit 0
