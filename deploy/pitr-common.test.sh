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
# a directory of files - and the verdict on the archive is a function of numbers, so the
# test is numbers too.

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

echo "=== the size bound survives a broken pipe ==="
# `find | sort | head -1` is the trap. `head` exits after its first line, `sort` gets
# SIGPIPE the moment it writes the rest, and under `set -o pipefail` that non-zero
# status kills the whole script. It did exactly that on the production box: the
# nightly trim died with "sort: write failed: Broken pipe" on 2026-10-21 and again on
# 2026-10-22, so the size cap - the bound that stands between the archive and a full
# disk - was never enforced.
#
# The stub below makes `find` print more than a pipe can hold (1 MiB is the kernel's
# maximum), so `sort` cannot write it all before the reader has gone. The prune has to
# survive that: it is run under `set -euo pipefail`, and a dead trim is a trim that
# does not trim.
ARCHIVE="$TMP/broken-pipe"
mkdir -p "$ARCHIVE"
make_segment "$ARCHIVE" "000000010000000000000001" 0 1024
mkdir -p "$TMP/bin"
cat > "$TMP/bin/find" <<'STUB'
#!/usr/bin/env bash
# A `find` that prints more than a pipe can hold. The prune calls find twice: once for
# the age bound (which deletes) and once for the oldest/newest lookup (which prints).
# Only the second one gets the huge output - that is the one the broken pipe was in.
#
# The lines are long, so that the output is big without being a million lines: a pipe
# holds 64 KiB by default and the kernel's maximum is 1 MiB, so 12 MiB cannot fit.
for arg in "$@"; do
  if [[ "$arg" == "-printf" ]]; then
    pad="$(printf 'x%.0s' {1..4000})"
    yes "1792639263.3682665350 $pad $FAKE_SEGMENT" | head -n 3000
    exit 0
  fi
done
exit 0
STUB
chmod +x "$TMP/bin/find"
( set -euo pipefail
  export FAKE_SEGMENT="$ARCHIVE/000000010000000000000001"
  export PATH="$TMP/bin:$PATH"
  prune_wal_archive "$ARCHIVE" 3650 0 >/dev/null 2>&1 )
check "the trim does not die on a broken pipe" "0" "$?"

echo "=== the verdict on the archive ==="
# The verdict, as a status: 0 when the archive is healthy, 1 when it is not.
verdict() { wal_archive_health "$@" >/dev/null 2>&1; echo $?; }

# A quiet database. The archiver has nothing waiting, the archive holds every segment
# that exists, and the last archive is as old as the last segment.
#
# This is what the production box looked like at 03:21 on 2026-10-22: the API had been
# down for nine hours, the database had nothing to write, the archive had not grown
# since 21:19 - and the check called it a broken archive, failed the backup and paged
# whoever was on call. The history had no hole in it.
check "a quiet database is not a broken archive" "0" \
  "$(verdict 21600 -1 21600 -1 1800)"

check "an archive with no segments at all is broken" "1" \
  "$(verdict -1 -1 -1 -1 1800)"
check "a segment waiting for the archiver is broken" "1" \
  "$(verdict 10 3600 10 -1 1800)"
check "a segment the archiver is about to take is not broken" "0" \
  "$(verdict 10 60 10 -1 1800)"
check "an archiver that is failing now is broken" "1" \
  "$(verdict 10 -1 3600 10 1800)"
check "a failure the archiver recovered from is not broken" "0" \
  "$(verdict 10 -1 10 3600 1800)"
check "an archiver writing somewhere else is broken" "1" \
  "$(verdict 3600 -1 10 -1 1800)"
check "an archiver a second ahead of the file is not broken" "0" \
  "$(verdict 296 -1 295 -1 1800)"
check "everything in order is healthy" "0" \
  "$(verdict 10 -1 10 -1 1800)"

# The reason has to say WHAT is wrong, not only that something is: an operator reading
# the log has to know whether to look at the directory, the archive command or the disk.
REASON="$(wal_archive_health 10 3600 10 -1 1800 2>&1 || true)"
case "$REASON" in
  *"3600s"*) echo "  PASS  the reason says how long the segment has been waiting"; pass=$((pass + 1)) ;;
  *) echo "  FAIL  the reason does not say how long the segment has been waiting"; fail=$((fail + 1)) ;;
esac

echo
echo "  $pass passed, $fail failed"
[[ "$fail" -eq 0 ]]
