#!/usr/bin/env bash
# Tests for the continuous off-box WAL shipping path (wal-offbox.sh and
# ship-wal-offbox.sh).
#
# The point of these tests is that they do NOT need the network or a real rclone remote.
# rclone and age are replaced by small stubs backed by a directory, so the assertions are
# about the bytes that end up "off-box" and the status the shipper writes - not about what
# the script says it would do.
#
# What is covered:
#   * a first run uploads every segment, encrypted, and writes the status object;
#   * a second run uploads nothing and does not fail;
#   * a quiet database (no finished segment) exits 0 - "nothing to ship" is not a failure;
#   * an upload failure exits non-zero (the unit's OnFailure alert depends on it);
#   * a shipment NEVER scans or prunes the remote - a full-prefix prune every minute is
#     what timed out runs whose shipment had already succeeded (2026-10-29);
#   * the nightly retention prune (wal_prune_remote) removes old segments and leaves
#     status.txt alone, and the same rule can be run by hand as `backup.sh --prune-only`;
#   * the restore library finds the newest base backup, fetches it, and decrypts a set of
#     per-segment objects;
#   * the drill refuses a remote that is not a drill prefix, so a drill can never write
#     over the production copy;
#   * the units run the shipper every minute, and restore-pitr.sh knows the off-box flags;
#   * the production ops script and its workflow exist, and the workflow runs on the box
#     without checking out any code.
#
# Run: bash deploy/wal-offbox.test.sh

set -uo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
TMP="$(mktemp -d "${TMPDIR:-/tmp}/wal-offbox-test.XXXXXX")"
chmod 700 "$TMP"
FAKEBIN="$TMP/bin"
mkdir -p "$FAKEBIN" "$TMP/archive" "$TMP/remote" "$TMP/state"
trap 'rm -rf "$TMP"' EXIT

PASS=0
FAIL=0
ok() { PASS=$((PASS + 1)); printf 'ok %d - %s\n' "$PASS" "$*"; }
bad() { FAIL=$((FAIL + 1)); printf 'not ok - %s\n' "$*" >&2; }
assert_eq() { local desc="$1" got="$2" want="$3"; if [[ "$got" == "$want" ]]; then ok "$desc"; else bad "$desc (got '$got', want '$want')"; fi; }
assert_file() { local desc="$1" path="$2"; if [[ -f "$path" ]]; then ok "$desc"; else bad "$desc ($path is missing)"; fi; }
assert_no_file() { local desc="$1" path="$2"; if [[ -f "$path" ]]; then bad "$desc ($path still exists)"; else ok "$desc"; fi; }
assert_status() { local desc="$1" want="$2"; if [[ "$STATUS" -eq "$want" ]]; then ok "$desc"; else bad "$desc (exit $STATUS, want $want): $OUTPUT"; fi; }
assert_contains() { local desc="$1" needle="$2"; if grep -qF -- "$needle" <<<"$OUTPUT"; then ok "$desc"; else bad "$desc (output does not contain '$needle'): $OUTPUT"; fi; }

# --- the stubs -----------------------------------------------------------------------
cat > "$FAKEBIN/rclone" <<'RCLONE'
#!/usr/bin/env bash
set -uo pipefail
cmd="${1:-}"; shift || true
if [[ "$cmd" == "--version" || "$cmd" == "version" ]]; then echo "rclone fake"; exit 0; fi
case "$cmd" in
  lsf)
    dir="${1:-}"; shift || true
    [[ -d "$dir" ]] || { echo "directory not found" >&2; exit 3; }
    format=''
    while [[ $# -gt 0 ]]; do
      case "$1" in
        --format) format="$2"; shift 2 ;;
        *) shift ;;
      esac
    done
    if [[ "$format" == *s* ]]; then
      # path and size, the way the real rclone prints them - with rclone's default
      # separator, which is "; " and not a space
      ( cd "$dir" && for f in *; do [[ -f "$f" ]] && printf '%s; %s\n' "$f" "$(stat -c %s "$f")"; done )
    else
      ( cd "$dir" && ls -1 )
    fi
    ;;
  copyto)
    src="${1:-}"; dst="${2:-}"
    if [[ -f "${FAKE_RCLONE_FAIL:-/nonexistent}" ]]; then
      echo "fake rclone: upload refused" >&2
      exit 1
    fi
    mkdir -p "$(dirname "$dst")"
    cp "$src" "$dst"
    ;;
  copy)
    src="${1:-}"; dst="${2:-}"; shift 2 || true
    include='*'
    while [[ $# -gt 0 ]]; do
      case "$1" in
        --include) include="$2"; shift 2 ;;
        *) shift ;;
      esac
    done
    mkdir -p "$dst"
    for f in "$src"/$include; do
      [[ -e "$f" ]] && cp "$f" "$dst/"
    done
    ;;
  delete)
    dir=''; min_age=''; include='*'
    while [[ $# -gt 0 ]]; do
      case "$1" in
        --min-age) min_age="$2"; shift 2 ;;
        --include) include="$2"; shift 2 ;;
        *) dir="$1"; shift ;;
      esac
    done
    [[ -d "$dir" ]] || exit 0
    days="${min_age%d}"
    find "$dir" -maxdepth 1 -type f -name "$include" -mtime "+${days}" -delete
    ;;
  *)
    echo "fake rclone: unsupported command '$cmd'" >&2
    exit 1
    ;;
esac
RCLONE

cat > "$FAKEBIN/age" <<'AGE'
#!/usr/bin/env bash
set -uo pipefail
decrypt=0; out=''; infile=''
while [[ $# -gt 0 ]]; do
  case "$1" in
    -r) shift 2 ;;
    -o) out="$2"; shift 2 ;;
    -d) decrypt=1; shift ;;
    -i) shift 2 ;;
    --) shift ;;
    *) infile="$1"; shift ;;
  esac
done
[[ -n "$infile" ]] || { echo "fake age: no input" >&2; exit 1; }
if [[ "$decrypt" -eq 1 ]]; then
  cat "$infile"
else
  [[ -n "$out" ]] || { echo "fake age: no output" >&2; exit 1; }
  cp "$infile" "$out"
fi
AGE
chmod +x "$FAKEBIN/rclone" "$FAKEBIN/age"

cat > "$TMP/env" <<EOF
BACKUP_REMOTE=$TMP/remote
BACKUP_AGE_RECIPIENT=age1testrecipient
EOF

run_ship() {
  OUTPUT="$(env \
    BACKUP_REMOTE= \
    BACKUP_AGE_RECIPIENT= \
    WAL_ARCHIVE_DIR="$TMP/archive" \
    WAL_SHIP_STATE_DIR="$TMP/state" \
    WAL_SEGMENT_SIZE="$SEGMENT_SIZE" \
    ENV_FILE="$TMP/env" \
    RCLONE_BIN="$FAKEBIN/rclone" \
    AGE_BIN="$FAKEBIN/age" \
    FAKE_RCLONE_FAIL="$TMP/fail-copy" \
    bash "$SCRIPT_DIR/ship-wal-offbox.sh" "$@" 2>&1)"
  STATUS=$?
}

SEG1=000000010000000000000001
SEG2=000000010000000000000002
printf 'segment one\n' > "$TMP/archive/$SEG1"
printf 'segment two\n' > "$TMP/archive/$SEG2"
# The shipper only ships a segment that is WHOLE - a finished WAL segment is exactly
# WAL_SEGMENT_SIZE bytes. The stub segments here are tiny, so the size the shipper is
# told about is the size of the stub segment. The real 16 MiB default is checked
# separately, below.
SEGMENT_SIZE="$(stat -c %s "$TMP/archive/$SEG1")"
# PostgreSQL also writes the backup history here. It is not a WAL segment: it must not
# be shipped as one, and it must not become newest_archived_segment.
printf 'history\n' > "$TMP/archive/$SEG2.backup"
touch -d '2 minutes ago' "$TMP/archive/$SEG1"
touch -d '1 minute ago' "$TMP/archive/$SEG2"

run_ship
assert_status "a first run succeeds" 0
assert_file "the first segment is off-box" "$TMP/remote/wal/$SEG1.age"
assert_file "the second segment is off-box" "$TMP/remote/wal/$SEG2.age"
assert_file "the status object is off-box" "$TMP/remote/wal/status.txt"
if grep -q '^uploaded_now=2$' "$TMP/remote/wal/status.txt"; then ok "the status counts two uploads"; else bad "uploaded_now is not 2"; fi
if grep -q "^last_upload_segment=$SEG2$" "$TMP/remote/wal/status.txt"; then ok "the status names the newest upload"; else bad "the status does not name $SEG2"; fi
if grep -q "^newest_archived_segment=$SEG2$" "$TMP/remote/wal/status.txt"; then ok "a .backup history file is not the newest segment"; else bad "the status counted the .backup history file"; fi
assert_no_file "the .backup history file is not shipped" "$TMP/remote/wal/$SEG2.backup.age"
if grep -Eq '^last_upload_lag_seconds=[0-9]+$' "$TMP/remote/wal/status.txt"; then ok "the status records a numeric lag"; else bad "the lag is not numeric"; fi

run_ship
assert_status "a second run succeeds" 0
assert_contains "a second run ships nothing" "nothing new to ship"
if grep -q '^uploaded_now=0$' "$TMP/remote/wal/status.txt"; then ok "the second status counts no uploads"; else bad "the second status still counts uploads"; fi

# A quiet database: no finished segment is not a failure. The status is still rewritten,
# which is the shipper's heartbeat.
mv "$TMP/archive" "$TMP/archive.keep"
mkdir -p "$TMP/archive"
run_ship
assert_status "a quiet database exits 0" 0
if grep -q '^newest_archived_segment=-$' "$TMP/remote/wal/status.txt"; then ok "the quiet status has no newest segment"; else bad "the quiet status names a segment"; fi
rmdir "$TMP/archive"
mv "$TMP/archive.keep" "$TMP/archive"

# An upload failure must be a non-zero exit - that is what fires OnFailure.
rm -f "$TMP/remote/wal/$SEG2.age"
: > "$TMP/fail-copy"
run_ship
if [[ "$STATUS" -ne 0 ]]; then ok "an upload failure exits non-zero"; else bad "an upload failure exited 0"; fi
assert_contains "the failure names the segment" "shipping stopped at $SEG2"
rm -f "$TMP/fail-copy"
run_ship
assert_status "the next run recovers and ships" 0
assert_file "the retried segment is off-box" "$TMP/remote/wal/$SEG2.age"

# Retention is NOT the shipper's job. A full-prefix scan costs about a tenth of a second
# per object, so running it every minute is what timed out shipments that had already
# succeeded on 2026-10-29 (2.5-10 minutes inside a 600 s unit timeout).
RCLONE="$FAKEBIN/rclone"
AGE="$FAKEBIN/age"
# shellcheck source=wal-offbox.sh
source "$SCRIPT_DIR/wal-offbox.sh"

OLD=0000000100000000000000FF
printf 'old\n' > "$TMP/remote/wal/$OLD.age"
touch -d '40 days ago' "$TMP/remote/wal/$OLD.age"
run_ship
assert_status "a shipment with an old object in the prefix succeeds" 0
assert_file "a shipment does not prune the remote" "$TMP/remote/wal/$OLD.age"
assert_file "the status object is still written" "$TMP/remote/wal/status.txt"

# The nightly backup prunes the prefix instead, through the shared helper.
wal_prune_remote "$TMP/remote/wal" 31
assert_no_file "the nightly prune removes the old segment" "$TMP/remote/wal/$OLD.age"
assert_file "the nightly prune leaves the status object" "$TMP/remote/wal/status.txt"
assert_file "the nightly prune leaves a young segment" "$TMP/remote/wal/$SEG1.age"

# ...and it can be run by hand, without a dump and without an upload: `--prune-only` is
# the same two deletes the 03:20 run makes, so this executes the retention rule instead of
# describing it. Measured on the production box on 2026-10-29, the per-minute version of
# this scan cost 60-105 s over 707 objects and timed the shipper out; nobody had ever
# watched the nightly one run.
mkdir -p "$TMP/deploy-copy"
cp "$SCRIPT_DIR"/*.sh "$TMP/deploy-copy"/
printf 'BACKUP_REMOTE=%s\nPOSTGRES_DB=fuelflow\n' "$TMP/remote" > "$TMP/deploy-copy/.env.production"
OLD2=0000000100000000000000FE
printf 'old two\n' > "$TMP/remote/wal/$OLD2.age"
touch -d '40 days ago' "$TMP/remote/wal/$OLD2.age"
OUTPUT="$(PATH="$FAKEBIN:$PATH" BACKUP_DIR="$TMP/backups" \
  bash "$TMP/deploy-copy/backup.sh" --prune-only 2>&1)"
STATUS=$?
assert_status "backup.sh --prune-only succeeds" 0
assert_contains "it says it is doing only the prune" "Prune-only run"
assert_contains "it reports the segment prune" "Remote segment prune done"
assert_no_file "the hand-run prune removes the old segment" "$TMP/remote/wal/$OLD2.age"
assert_file "the hand-run prune leaves a young segment" "$TMP/remote/wal/$SEG2.age"
assert_file "the hand-run prune leaves the status object" "$TMP/remote/wal/status.txt"
if [[ -d "$TMP/backups" ]]; then bad "prune-only created a backup directory (it must not touch the disk)"; else ok "prune-only writes no dump"; fi
OUTPUT="$(PATH="$FAKEBIN:$PATH" bash "$TMP/deploy-copy/backup.sh" --nonsense 2>&1)"
STATUS=$?
if [[ "$STATUS" -ne 0 ]]; then ok "an unknown flag is refused"; else bad "an unknown flag was accepted"; fi

# --- a half-written segment must not reach the bucket ------------------------------------
# The archiver's cp writes a segment into the archive directory IN PLACE, so the file is
# briefly shorter than a segment. Uploading that would put a hole in the off-box chain:
# a restore stops replaying at the hole, silently, and everything after it is gone.
SEG3=000000010000000000000003
printf 'half' > "$TMP/archive/$SEG3"
run_ship
assert_status "a half-written segment is not a failure" 0
assert_no_file "a half-written segment is not shipped" "$TMP/remote/wal/$SEG3.age"
assert_contains "the shipper says why it left the segment" "still being written"
rm -f "$TMP/archive/$SEG3"

# And an off-box object that is SHORTER than the segment is a half-written upload that
# was never replaced. The shipper has to notice it and put it right.
printf 'x' > "$TMP/remote/wal/$SEG1.age"
run_ship
assert_status "the repair run succeeds" 0
assert_contains "the short off-box object is repaired" "re-uploading it"
assert_eq "the repaired object is whole again" \
  "$(stat -c %s "$TMP/remote/wal/$SEG1.age")" "$SEGMENT_SIZE"
if grep -q '^repaired_now=1$' "$TMP/remote/wal/status.txt"; then
  ok "the status counts the repair"
else
  bad "the status does not count the repair"
fi

# A restore that meets a half-written segment must FAIL, not report success and hand over
# a database that is older than the archive it was given. The server's own log says so
# ("invalid record length"), so the restore reads those words and refuses.
if grep -q 'invalid record length' "$SCRIPT_DIR/restore-pitr.sh"; then
  ok "the restore refuses an archive with an unreadable segment"
else
  bad "the restore does not notice a damaged WAL segment"
fi
if grep -q 'wal_segment_is_complete' "$SCRIPT_DIR/drill-wal-offbox.sh"; then
  ok "the drill waits for a WHOLE segment, not just a file with the right name"
else
  bad "the drill waits for the file name only"
fi
if grep -q 'chmod -R a+rX "\$WAL_DIR"' "$SCRIPT_DIR/restore-pitr.sh"; then
  ok "the restore makes the fetched WAL readable for the container's postgres user"
else
  bad "the restore depends on the caller's umask (a 600 WAL file stops the replay)"
fi
if grep -q '^  umask 077$' "$SCRIPT_DIR/wal-ship-ops.sh"; then
  bad "the ops script sets the umask for the whole process (it leaks into the drill)"
else
  ok "the ops script does not set a process-wide umask"
fi
if grep -q 'WAL_SEGMENT_SIZE:-16777216' "$SCRIPT_DIR/wal-offbox.sh"; then
  ok "a finished WAL segment is 16 MiB by default"
else
  bad "the default WAL segment size is not 16 MiB"
fi

# --- the restore helpers -------------------------------------------------------------
# The off-box sizes are read with rclone's own separator ("; "), not a space: the first
# version of this parser split on whitespace only and therefore read NO sizes at all,
# which made the whole verification say "everything is missing".
assert_eq "the off-box object sizes are read" \
  "$(wal_remote_object_sizes "$TMP/remote/wal" | awk -v n="$SEG1.age" '$1 == n { print $2; exit }')" \
  "$SEGMENT_SIZE"

assert_eq "the bucket root gains /wal" "$(wal_remote_wal_dir "$TMP/remote")" "$TMP/remote/wal"
assert_eq "a wal/ prefix does not gain another" "$(wal_remote_wal_dir "$TMP/remote/wal")" "$TMP/remote/wal"

mkdir -p "$TMP/bases" "$TMP/empty-bases"
: > "$TMP/bases/fuelflow_base_2026-01-01_000000.tar.age"
: > "$TMP/bases/fuelflow_base_2026-02-02_000000.tar.age"
assert_eq "the newest base backup is chosen" \
  "$(wal_newest_base_object "$TMP/bases")" "fuelflow_base_2026-02-02_000000.tar.age"
assert_eq "the base backup is fetched" \
  "$(wal_fetch_base "$TMP/bases" "$TMP/fetched.tar.age")" "fuelflow_base_2026-02-02_000000.tar.age"
assert_file "the fetched base backup exists" "$TMP/fetched.tar.age"
OUTPUT="$(wal_fetch_base "$TMP/empty-bases" "$TMP/no-base.tar.age" 2>&1)"; STATUS=$?
if [[ "$STATUS" -ne 0 ]]; then ok "an empty remote is a failure, not an empty base"; else bad "an empty remote returned success"; fi
assert_contains "the empty remote names the missing base" "no fuelflow_base_*.tar.age"

mkdir -p "$TMP/walout"
COUNT="$(wal_fetch_offbox "$TMP/remote/wal" "$TMP/walout" "")"
assert_eq "both segments decrypt" "$COUNT" "2"
assert_file "the first decrypted segment exists" "$TMP/walout/$SEG1"
assert_file "the second decrypted segment exists" "$TMP/walout/$SEG2"
assert_no_file "the status object is not treated as a segment" "$TMP/walout/status.txt"

# --- the drill's remote guard ---------------------------------------------------------
# The real-R2 drill writes into the same bucket the production shipper uses, so the
# prefix it writes to has to be provably not the production one. The guard is a function
# so it can be checked here without a bucket and without docker.
assert_eq "a /drill prefix is allowed" \
  "$(wal_drill_remote_is_safe 'r2:fuelflow-backups/drill/695-20261023T090000Z' && echo yes || echo no)" "yes"
assert_eq "a /drill prefix on another backend is allowed" \
  "$(wal_drill_remote_is_safe 's3:bucket/drill/x' && echo yes || echo no)" "yes"
assert_eq "the bucket root is refused" \
  "$(wal_drill_remote_is_safe 'r2:fuelflow-backups' && echo yes || echo no)" "no"
assert_eq "the production wal/ prefix is refused" \
  "$(wal_drill_remote_is_safe 'r2:fuelflow-backups/wal' && echo yes || echo no)" "no"
assert_eq "a drill word that is not a path segment is refused" \
  "$(wal_drill_remote_is_safe 'r2:fuelflow-backups/drill-run' && echo yes || echo no)" "no"
assert_eq "an empty remote is refused" \
  "$(wal_drill_remote_is_safe '' && echo yes || echo no)" "no"

# And the drill itself refuses, before it starts a container or touches the bucket.
OUTPUT="$(DRILL_REMOTE="r2:fuelflow-backups" ENV_FILE="$TMP/no-such.env" \
  bash "$SCRIPT_DIR/drill-wal-offbox.sh" 2>&1)"
STATUS=$?
if [[ "$STATUS" -ne 0 ]]; then ok "the drill refuses a non-drill prefix"; else bad "the drill accepted a non-drill prefix"; fi
assert_contains "the refusal explains the /drill rule" "/drill"

# The drill has to use the remote it was given everywhere: the env file the shipper
# reads, the base backup upload, the shipment and the restore.
if grep -q 'BACKUP_REMOTE=$REMOTE' "$SCRIPT_DIR/drill-wal-offbox.sh"; then
  ok "the drill's env file points at the drill remote"
else
  bad "the drill's env file does not point at the drill remote"
fi
if grep -q 'copyto "$TMP/base.tar.age" "$REMOTE/$BASE_NAME"' "$SCRIPT_DIR/drill-wal-offbox.sh"; then
  ok "the drill uploads the base backup to the drill remote"
else
  bad "the drill does not upload the base backup to the drill remote"
fi
if grep -q -- '--base-remote "$REMOTE"' "$SCRIPT_DIR/drill-wal-offbox.sh"; then
  ok "the drill restores from the drill remote"
else
  bad "the drill does not restore from the drill remote"
fi

# --- the production ops script --------------------------------------------------------
assert_file "the ops script exists" "$SCRIPT_DIR/wal-ship-ops.sh"
for cmd in preflight install ship-once lag prune alert-drill drill report all; do
  if grep -qE "^[[:space:]]+$cmd\)" "$SCRIPT_DIR/wal-ship-ops.sh"; then
    ok "the ops script has the '$cmd' command"
  else
    bad "the ops script has no '$cmd' command"
  fi
done
if grep -q 'trap end_injection EXIT' "$SCRIPT_DIR/wal-ship-ops.sh" \
   && grep -q 'systemctl unset-environment ENV_FILE' "$SCRIPT_DIR/wal-ship-ops.sh"; then
  ok "the alert drill removes its injected environment even if the run dies"
else
  bad "the alert drill does not reliably remove its injected environment"
fi
if grep -q 'no-such-bucket' "$SCRIPT_DIR/wal-ship-ops.sh"; then
  ok "the alert drill injects a remote that cannot exist (no request reaches the network)"
else
  bad "the alert drill does not inject an impossible remote"
fi
if grep -q 'fn="\$(ops_function "\$step")"' "$SCRIPT_DIR/wal-ship-ops.sh" \
   && grep -q '^ops_function()' "$SCRIPT_DIR/wal-ship-ops.sh"; then
  ok "the campaign and the dispatcher share one command map"
else
  bad "the campaign builds function names by hand (this is what broke 'all')"
fi
if grep -q 'config file' "$SCRIPT_DIR/wal-ship-ops.sh" \
   && grep -q 'RCLONE_CONFIG\*' "$SCRIPT_DIR/wal-ship-ops.sh"; then
  ok "the preflight reports how rclone is configured"
else
  bad "the preflight does not report the rclone configuration"
fi
if grep -q 'bash "\$SCRIPT_DIR/backup.sh" --prune-only' "$SCRIPT_DIR/wal-ship-ops.sh"; then
  ok "the 'prune' command runs the nightly backup's own prune, not a second copy of it"
else
  bad "the 'prune' command does not call backup.sh --prune-only"
fi
if grep -q 'for step in preflight install ship-once lag verify alert-drill drill;' "$SCRIPT_DIR/wal-ship-ops.sh"; then
  ok "the campaign does not include a deletion step"
else
  bad "the campaign's step list changed (a deletion must not ride along in 'all')"
fi
if grep -q 'rclone_lsf()' "$SCRIPT_DIR/drill-wal-offbox.sh" \
   && grep -q 'rclone_lsf "\$REMOTE" --recursive' "$SCRIPT_DIR/drill-wal-offbox.sh"; then
  ok "the drill retries its object listing (R2 rejects the first request on a fresh prefix)"
else
  bad "the drill trusts a one-shot object listing"
fi
if grep -q 'wal_drill_remote_is_safe' "$SCRIPT_DIR/drill-wal-offbox.sh"; then
  ok "the drill uses the shared remote guard"
else
  bad "the drill does not use the shared remote guard"
fi

# --- static wiring -------------------------------------------------------------------
for unit in fuelflow-wal-ship.service fuelflow-wal-ship.timer fuelflow-wal-ship-alert@.service; do
  assert_file "the unit $unit exists" "$SCRIPT_DIR/systemd/$unit"
done
if grep -q '^OnCalendar=\*:0/1$' "$SCRIPT_DIR/systemd/fuelflow-wal-ship.timer"; then
  ok "the timer fires every minute"
else
  bad "the timer is not every minute"
fi
if grep -q 'OnFailure=fuelflow-wal-ship-alert@%n.service' "$SCRIPT_DIR/systemd/fuelflow-wal-ship.service"; then
  ok "a failed shipment starts the alert unit"
else
  bad "the service has no OnFailure hook"
fi
if grep -q 'BACKUP_AGE_RECIPIENT' "$SCRIPT_DIR/install-wal-ship-schedule.sh" \
   && grep -q 'BACKUP_REMOTE' "$SCRIPT_DIR/install-wal-ship-schedule.sh"; then
  ok "the installer refuses an unconfigured remote"
else
  bad "the installer does not check the remote settings"
fi

# The production ops workflow. It runs ON the box, so it must not check out code: the
# box's checkout is reset to main by the job itself, and a second checkout is a second
# copy to get out of step with the first.
REPO_ROOT="$(git -C "$SCRIPT_DIR" rev-parse --show-toplevel 2>/dev/null || echo "$SCRIPT_DIR/..")"
OPS_WORKFLOW="$REPO_ROOT/.github/workflows/wal-ship-ops.yml"
assert_file "the production ops workflow exists" "$OPS_WORKFLOW"
if grep -q 'workflow_dispatch' "$OPS_WORKFLOW"; then
  ok "the production ops workflow is dispatched by hand"
else
  bad "the production ops workflow has no workflow_dispatch"
fi
if grep -q 'runs-on: \[self-hosted, linux, x64\]' "$OPS_WORKFLOW"; then
  ok "the production ops workflow runs on the self-hosted runner"
else
  bad "the production ops workflow does not run on the self-hosted runner"
fi
if grep -qE '^[[:space:]]*-?[[:space:]]*uses:[[:space:]]*actions/checkout' "$OPS_WORKFLOW"; then
  bad "the production ops workflow checks out code on the production box"
else
  ok "the production ops workflow checks out no code on the production box"
fi
if grep -q 'wal-ship-ops.sh' "$OPS_WORKFLOW"; then
  ok "the production ops workflow runs the ops script"
else
  bad "the production ops workflow does not run the ops script"
fi

OUTPUT="$(bash "$SCRIPT_DIR/restore-pitr.sh" --help 2>&1)"
STATUS=$?
assert_eq "restore-pitr.sh --help exits 1" "$STATUS" 1
assert_contains "the help names --base-remote" "--base-remote"
assert_contains "the help names --wal-remote" "--wal-remote"

for f in wal-offbox.sh ship-wal-offbox.sh notify-wal-ship-failure.sh install-wal-ship-schedule.sh \
         drill-wal-offbox.sh wal-ship-ops.sh; do
  if bash -n "$SCRIPT_DIR/$f"; then ok "bash -n $f"; else bad "bash -n $f"; fi
done

# The ciphertext must be written before the upload, not after it.
if awk '/-r "\$recipient"/{r=NR} /copyto "\$enc"/{c=NR} END{exit !(r && c && r < c)}' \
     "$SCRIPT_DIR/wal-offbox.sh"; then
  ok "a segment is encrypted before it is uploaded"
else
  bad "the upload is not ordered after encryption"
fi

echo
if [[ "$FAIL" -gt 0 ]]; then
  echo "FAILED: $FAIL of $((PASS + FAIL)) checks." >&2
  exit 1
fi
echo "All $PASS checks passed."
