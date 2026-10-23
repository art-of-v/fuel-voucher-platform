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
#   * the remote retention prune removes old segments and leaves status.txt alone;
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
    ( cd "$dir" && ls -1 )
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
    WAL_REMOTE_RETENTION_DAYS=31 \
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

# Retention: an old segment object is pruned; the status object is not.
OLD=0000000100000000000000FF
printf 'old\n' > "$TMP/remote/wal/$OLD.age"
touch -d '40 days ago' "$TMP/remote/wal/$OLD.age"
run_ship
assert_status "the prune run succeeds" 0
assert_no_file "an old remote segment is pruned" "$TMP/remote/wal/$OLD.age"
assert_file "the status object survives the prune" "$TMP/remote/wal/status.txt"

# --- the restore helpers -------------------------------------------------------------
RCLONE="$FAKEBIN/rclone"
AGE="$FAKEBIN/age"
# shellcheck source=wal-offbox.sh
source "$SCRIPT_DIR/wal-offbox.sh"

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
for cmd in preflight install ship-once lag alert-drill drill report all; do
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
