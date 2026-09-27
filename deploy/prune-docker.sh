#!/usr/bin/env bash
# ------------------------------------------------------------------------------
# Nightly Docker disk reclamation ("GC").
#
# WHY THIS EXISTS
# On 2026-09-27 the / filesystem hit 100% and Postgres crash-looped with
#   FATAL: could not write lock file "postmaster.pid": No space left on device
# taking the whole API down (planning #55). Nothing on this box ever reclaimed disk:
# every merge to main pushes fresh ghcr.io/art-of-v/fuelflow-{backend,admin,website}
# images, each deploy pulls the new tag, and the OLD tags are never removed. On a 38 GB
# disk running prod + staging + the observability stack, that is a slow fill to 0 bytes
# with nothing evicting anything.
#
# WHAT IT DOES (and deliberately does NOT)
#   - Removes images not used by any container (old deploy tags) + build cache.
#   - NEVER touches volumes (postgres_data, Loki, Grafana, Prometheus). A
#     `docker system prune --volumes` here would delete the database. This script scopes
#     to images + build cache only — that is the accumulator that filled the disk.
#   - After reclaiming, it checks disk usage. If / is STILL at/above DISK_ALERT_THRESHOLD
#     it prints the top consumers and EXITS NON-ZERO, so the systemd unit fails and fires
#     the Telegram alert (fuelflow-prune-alert@). That turns "disk quietly refilling from
#     container logs / Loki / DB growth" from a silent outage-in-waiting into a proactive
#     page — the signal that was missing on 2026-09-27.
#
# It does NOT truncate the logs of RUNNING containers — Docker's json-file logs are
# capped by /etc/docker/daemon.json (log-opts max-size/max-file), applied separately.
# See docs/DEPLOYMENT.md ("Disk hygiene").
#
# One-off run:  cd /root/FuelFlow/deploy && ./prune-docker.sh
# Nightly:      installed as fuelflow-prune.timer by install-prune-schedule.sh
# ------------------------------------------------------------------------------
set -euo pipefail

# Percentage use of the watched filesystem at or above which we page AFTER pruning.
# Override via the unit/environment if 85 is too tight or too loose for this box.
DISK_ALERT_THRESHOLD="${DISK_ALERT_THRESHOLD:-85}"
# The filesystem to watch — the one Docker's data root lives on.
WATCH_PATH="${WATCH_PATH:-/}"

log() { echo "[$(date -Is)] $*"; }

# `df -P` forces portable single-line output; column 5 is "Use%" like "83%".
disk_use_pct() { df -P "$WATCH_PATH" | awk 'NR==2 { gsub(/%/,"",$5); print $5 }'; }

before="$(disk_use_pct)"
[[ -n "$before" ]] || { log "ERROR: could not read disk usage for ${WATCH_PATH}."; exit 1; }
log "Docker GC starting. ${WATCH_PATH} at ${before}% used."

# 1. Images not referenced by any container: the old per-deploy tags. -a so tagged-but-
#    unused images go too, not just dangling layers. Running containers keep their image.
log "Pruning unused images ..."
docker image prune -af

# 2. Build cache. This box pulls prebuilt images from GHCR rather than building, so this
#    is usually near-zero — harmless to run, and non-zero on any box that ever built.
log "Pruning build cache ..."
docker builder prune -af

after="$(disk_use_pct)"
[[ -n "$after" ]] || { log "ERROR: could not read disk usage after prune."; exit 1; }
log "Docker GC done. ${WATCH_PATH} ${before}% -> ${after}% used."

# 3. Proactive page if the disk is STILL high. Prune only reclaims images/cache; if the
#    disk stays full the culprit is something this script cannot touch (runaway container
#    logs, Loki retention, DB growth) and a human must look BEFORE Postgres dies again.
if (( after >= DISK_ALERT_THRESHOLD )); then
  log "WARNING: ${WATCH_PATH} still at ${after}% (>= ${DISK_ALERT_THRESHOLD}%) after GC."
  echo "--- docker disk usage ---" >&2
  docker system df >&2 || true
  echo "--- largest dirs under /var/lib/docker ---" >&2
  du -xhd1 /var/lib/docker 2>/dev/null | sort -h | tail >&2 || true
  echo "--- largest container logs ---" >&2
  du -sh /var/lib/docker/containers/*/*-json.log 2>/dev/null | sort -h | tail -5 >&2 || true
  # Non-zero -> systemd marks the unit failed -> OnFailure fires the Telegram alert.
  exit 1
fi

log "OK: ${WATCH_PATH} at ${after}% is below the ${DISK_ALERT_THRESHOLD}% alert threshold."
