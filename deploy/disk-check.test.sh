#!/usr/bin/env bash
# Local harness + CI gate for deploy/disk-check.sh - the disk pressure check added for #263.
#
# WHY THIS NEEDS A TEST
# This script runs from a systemd timer on a box nobody is watching, and the failure modes are all
# silent by construction:
#
#   - It exits 0 and prints nothing on a healthy disk, so a check that never measures anything looks
#     exactly like a healthy box. The 2026-09-27 outage had no signal at all; shipping a check
#     that cannot report its own blindness would be the same failure wearing a new hat.
#   - Its whole value is the alert, and an alert that fires on every 15-minute tick is an alert
#     people mute. The level-change dedup is the part most likely to be "simplified" away later.
#   - The usage percentage comes out of `df` output by column, on a filesystem whose device name
#     may be long enough to make that fragile. It runs where the disk is already in trouble, which
#     is the worst possible time to discover it printed a blank.
#
# None of that is observable from a healthy production box, which is why it is checked here: every
# branch is driven with a stubbed `df`, a throwaway state file and no Telegram anywhere.
#
# Run: bash deploy/disk-check.test.sh
set -uo pipefail
cd "$(dirname "$0")"

pass=0; fail=0
ok()   { pass=$((pass + 1)); printf '  ok    %s\n' "$1"; }
bad()  { fail=$((fail + 1)); printf '  FAIL  %s\n' "$1"; }

WORK="$(mktemp -d)"
trap 'rm -rf "$WORK"' EXIT

# A stub `df`. FAKE_PCT drives the percentage. FAKE_BLANK makes it print nothing at all and
# FAKE_FAILS make it exit non-zero - the two ways a real `df` leaves the check with no number to
# read, neither of which can be produced by breaking the filesystem this runs on.
FAKE_DF="$WORK/df"
cat > "$FAKE_DF" <<'STUB'
#!/usr/bin/env bash
if [[ -n "${FAKE_FAILS:-}" ]]; then
  echo "df: cannot read table of mounted file systems" >&2
  exit 1
fi
if [[ -n "${FAKE_BLANK:-}" ]]; then
  exit 0
fi
printf 'Filesystem     1024-blocks      Used Available Capacity Mounted on\n'
printf '/dev/vg1-ubuntu-lv-ubuntu    41153856  33964256   6771824   %s%% /\n' "${FAKE_PCT:-0}"
STUB
chmod +x "$FAKE_DF"

STATE="$WORK/state"
OUT="$WORK/out"

# Runs disk-check.sh against the stubs. Echoes the exit code, leaves the output in $OUT.
run_check() {
  env DF_BIN="$FAKE_DF" \
      PG_SIZE_CMD="${PG_SIZE_CMD:-echo 123456789}" \
      STATE_FILE="$STATE" \
      WATCH_PATH="/" \
      "$@" \
      bash ./disk-check.sh > "$OUT" 2>&1
  echo $?
}

level() { cat "$STATE" 2>/dev/null || echo "<no state file>"; }

# ── 1. Healthy disk: silence, and a baseline for the next run to compare against ──────
code="$(run_check FAKE_PCT=40)"
if [[ "$code" == "0" ]] && ! grep -qE "WARNING|CRITICAL|RECOVERED" "$OUT"; then
  ok "a healthy disk is silent (exit 0)"
else
  bad "a healthy disk should be silent: exit=$code out=$(head -3 "$OUT")"
fi
if [[ "$(level)" == "ok" ]]; then
  ok "the first healthy run records a baseline"
else
  bad "the first healthy run should record 'ok', got '$(level)'"
fi

# ── 2. Crossing the warning line reports exactly once ───────────────────────────────
code="$(run_check FAKE_PCT=81)"
if [[ "$code" == "1" ]] && grep -q "WARNING" "$OUT" && grep -q "81%" "$OUT"; then
  ok "crossing into warning exits 1 and says so"
else
  bad "81% should warn: exit=$code out=$(head -3 "$OUT")"
fi
if [[ "$(level)" == "warn" ]]; then
  ok "the warning level is recorded"
else
  bad "state should be 'warn' after a warning, got '$(level)'"
fi

# The dedup, which is the part a future cleanup would quietly delete: same warning 15 minutes
# later must not page again.
code="$(run_check FAKE_PCT=82)"
if [[ "$code" == "0" ]] && ! grep -qE "WARNING|CRITICAL|RECOVERED" "$OUT"; then
  ok "an unchanged warning does NOT re-alert on every tick"
else
  bad "a still-warn disk must stay quiet: exit=$code out=$(head -3 "$OUT")"
fi

# ── 3. Escalation and recovery ─────────────────────────────────────────────────────
code="$(run_check FAKE_PCT=95)"
if [[ "$code" == "1" ]] && grep -q "CRITICAL" "$OUT"; then
  ok "crossing into critical escalates rather than repeating the warning"
else
  bad "95% should be critical: exit=$code out=$(head -3 "$OUT")"
fi

code="$(run_check FAKE_PCT=96)"
if [[ "$code" == "0" ]] && ! grep -qE "WARNING|CRITICAL|RECOVERED" "$OUT"; then
  ok "a disk that stays critical does not re-page every 15 minutes"
else
  bad "a still-critical disk must stay quiet: exit=$code out=$(head -3 "$OUT")"
fi

code="$(run_check FAKE_PCT=70)"
if [[ "$code" == "1" ]] && grep -q "RECOVERED" "$OUT"; then
  ok "coming back down says so, once"
else
  bad "recovering should report once: exit=$code out=$(head -3 "$OUT")"
fi

code="$(run_check FAKE_PCT=71)"
if [[ "$code" == "0" ]] && ! grep -q "RECOVERED" "$OUT"; then
  ok "an unchanged recovery does not repeat"
else
  bad "a recovered disk must stay quiet: exit=$code out=$(head -3 "$OUT")"
fi

# ── 4. A check that cannot measure must say that, not report "healthy" ─────────────
cp "$STATE" "$WORK/state.before"
for broken in FAKE_BLANK=1 FAKE_FAILS=1; do
  code="$(run_check $broken)"
  if [[ "$code" == "2" ]] && grep -q "CANNOT MEASURE" "$OUT"; then
    ok "df that yields no percentage ($broken) is reported, not silently treated as healthy"
  else
    bad "df with $broken should exit 2: exit=$code out=$(head -3 "$OUT")"
  fi
  if cmp -s "$STATE" "$WORK/state.before"; then
    ok "a failed measurement ($broken) leaves the recorded level alone"
  else
    bad "a failed measurement ($broken) must not overwrite the level (a later recovery still has to be seen)"
  fi
done

code="$(run_check DISK_WARN_THRESHOLD=90 DISK_CRITICAL_THRESHOLD=80)"
if [[ "$code" == "2" ]] && grep -q "MISCONFIGURED" "$OUT"; then
  ok "thresholds that contradict each other are refused instead of mis-comparing"
else
  bad "critical<=warn should exit 2: exit=$code out=$(head -3 "$OUT")"
fi

# ── 5. The percentage has to survive real df output ────────────────────────────────
# The stub's device name is longer than df's default 7-character column, which is the shape that
# breaks a naive "grab column 5" parser. 84% with the default thresholds must classify as warn,
# which only happens if the number was read correctly.
code="$(run_check FAKE_PCT=84)"
if [[ "$code" == "1" ]] && grep -q "84%" "$OUT" && [[ "$(level)" == "warn" ]]; then
  ok "the usage percentage is read correctly from df -P output"
else
  bad "84% should parse and classify as warn: exit=$code state=$(level) out=$(head -3 "$OUT")"
fi

# ── 6. Database size is context, never the trigger ─────────────────────────────────
# If the Postgres container is stopped, `docker exec` fails. The disk still needs watching, so the
# size has to degrade to "unavailable" instead of taking the check down with it. Driven at a
# warning level on purpose: a healthy disk is silent whether or not it can read the database, so a
# healthy disk would prove nothing here.
rm -f "$STATE"
code="$(run_check FAKE_PCT=81 PG_SIZE_CMD="exit 1")"
if [[ "$code" == "1" ]] && grep -q "WARNING" "$OUT" && grep -q "unavailable" "$OUT"; then
  ok "an unreadable database size does not stop the disk check, and is reported as unavailable"
else
  bad "a stopped database must not blind the check: exit=$code out=$(head -5 "$OUT")"
fi

# ── 7. The state file must not be a message to send ───────────────────────────────
# Telegram truncates at 4096 chars. The alert carries a command block an operator copies during an
# incident, so it has to stay well clear of that or the commands get cut off first. Forced from a
# clean slate so this measures the alert itself, not a repeat run that stayed silent.
rm -f "$STATE"
run_check FAKE_PCT=95 PG_SIZE_CMD="echo 999999999999" > /dev/null
longest="$(wc -c < "$OUT")"
if grep -q "CRITICAL" "$OUT" && (( longest < 3000 )); then
  ok "the critical alert fits in a Telegram message (${longest} chars)"
else
  bad "the critical alert is ${longest} chars - it would be truncated in transit"
fi

printf '\n%s passed, %s failed\n' "$pass" "$fail"
[[ "$fail" -eq 0 ]]