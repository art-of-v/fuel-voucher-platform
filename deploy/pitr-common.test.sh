#!/usr/bin/env bash
# Tests deploy/pitr-common.sh - the checks and the pruning that keep the WAL archive
# from filling the disk.
#
# The pruning is the important one. It is the guard that stands between a full disk and a
# database that stops accepting writes, and it was written after a measurement showed the
# archive growing at 4.6 GiB a day on a disk with 21 GiB free. A guard that has never
# been exercised is a belief.
#
# No database and no network: the pruning works on a directory of files, so the test is
# a directory of files.

set -uo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=pitr-common.sh
source "$SCRIPT_DIR/pitr-common.sh"

TMP="$(mktemp -d "${TMPDIR:-/tmp}/pitr-common-test.XXXXXX")"
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

# A segment file, with a controlled age. PostgreSQL's segment names are 24 hex
# characters; the name does not matter to the pruning, the mtime does.
make_segment() {
  local dir="$1" name="$2" days_old="$3" size_kb="$4"
  dd if=/dev/zero of="$dir/$name" bs=1024 count="$size_kb" >/dev/null 2>&1
  touch -d "$days_old days ago" "$dir/$name" 2>/dev/null || touch -t 202001010000 "$dir/$name"
}

echo "=== the size bound ==="
ARCHIVE="$TMP/size"
mkdir -p "$ARCHIVE"
# Ten segments of 1 MiB. The OLDEST is segment 01 and the NEWEST is segment 10, by
# mtime - the pruning goes by age, not by name, so the names and the ages have to
# disagree here or the test proves nothing.
for i in $(seq 1 10); do
  printf -v name '00000001000000000000%04d' "$i"
  make_segment "$ARCHIVE" "$name" "$((10 - i))" 1024
done
BEFORE="$(find "$ARCHIVE" -maxdepth 1 -name '0000000*' -type f | wc -l | tr -d ' ')"
# A 4 MiB cap on ten 1 MiB segments.
prune_wal_archive "$ARCHIVE" 3650 4194304 >/dev/null
LEFT="$(find "$ARCHIVE" -maxdepth 1 -name '0000000*' -type f | wc -l | tr -d ' ')"
TOTAL="$(du -sb "$ARCHIVE" | cut -f1)"

if [[ "$LEFT" -lt "$BEFORE" ]]; then
  echo "  PASS  the size bound removed segments ($BEFORE -> $LEFT)"; pass=$((pass + 1))
else
  echo "  FAIL  the size bound removed nothing ($BEFORE -> $LEFT)"; fail=$((fail + 1))
fi
if [[ "$TOTAL" -le 4194304 ]]; then
  echo "  PASS  the archive is within the cap ($TOTAL bytes)"; pass=$((pass + 1))
else
  echo "  FAIL  the archive is still over the cap ($TOTAL bytes)"; fail=$((fail + 1))
fi
check "the newest segment survived" "yes" \
  "$([[ -f "$ARCHIVE/000000010000000000000010" ]] && echo yes || echo no)"
check "the oldest segment is gone" "yes" \
  "$([[ -f "$ARCHIVE/000000010000000000000001" ]] && echo no || echo yes)"

echo "=== the age bound ==="
ARCHIVE="$TMP/age"
mkdir -p "$ARCHIVE"
make_segment "$ARCHIVE" "000000010000000000000001" 40 64   # 40 days old
make_segment "$ARCHIVE" "000000010000000000000002" 1 64    # yesterday
prune_wal_archive "$ARCHIVE" 30 8589934592 >/dev/null
check "the segment past the age bound was removed" "yes" \
  "$([[ -f "$ARCHIVE/000000010000000000000001" ]] && echo no || echo yes)"
check "the recent segment stayed" "yes" \
  "$([[ -f "$ARCHIVE/000000010000000000000002" ]] && echo yes || echo no)"

echo "=== an empty archive, and a missing one ==="
ARCHIVE="$TMP/empty"
mkdir -p "$ARCHIVE"
prune_wal_archive "$ARCHIVE" 3 4194304 >/dev/null
check "an empty archive survives the prune" "0" \
  "$(find "$ARCHIVE" -maxdepth 1 -name '0000000*' -type f | wc -l | tr -d ' ')"
prune_wal_archive "$TMP/does-not-exist" 3 4194304 >/dev/null
check "a missing archive is not an error" "0" "$?"

echo "=== the newest segment is never removed ==="
ARCHIVE="$TMP/newest"
mkdir -p "$ARCHIVE"
# One segment, and a cap of zero bytes: the only thing there is the newest.
make_segment "$ARCHIVE" "000000010000000000000001" 0 64
prune_wal_archive "$ARCHIVE" 3650 0 >/dev/null
check "the only segment survived a zero-byte cap" "yes" \
  "$([[ -f "$ARCHIVE/000000010000000000000001" ]] && echo yes || echo no)"

echo
echo "  $pass passed, $fail failed"
[[ "$fail" -eq 0 ]]
