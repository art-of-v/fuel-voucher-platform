#!/usr/bin/env bash
# Turn on point-in-time recovery, and prove that it works.
#
# WHY THIS IS A SCRIPT AND NOT A LINE IN THE COMPOSE FILE
# Enabling WAL archiving is one line of configuration. Making it WORK is not, because the
# archive directory has to be writable by the postgres user inside the container, and the
# way it is normally created - as root, on the host - means it is not.
#
# When that is wrong, the failure is quiet in the worst possible way:
#
#   * the database runs perfectly
#   * the backup runs perfectly and reports success
#   * the archive is empty, and nobody finds out until the day it is needed
#
# PostgreSQL logs the failed archive command, and then keeps retrying it forever, and
# the WAL segments pile up in pg_wal until the disk fills. So the failure is not just
# "no point-in-time recovery" - it is eventually "the database stops accepting writes".
#
# This script does the whole thing and checks the result:
#
#   1. create the archive directory, owned by the postgres user
#   2. start the stack with archiving on
#   3. force a WAL switch and wait for the segment to appear in the archive
#   4. read pg_stat_archiver and report what actually happened
#
# Step 3 is the one that matters. It is the difference between "archiving is enabled"
# and "archiving works", and only the second one is worth anything.
#
# USAGE
#   enable-pitr.sh [--staging] [--container <name>] [--archive-dir <path>]
#
#   --staging  do it to the staging stack instead of production
#
# It does NOT restore anything, and it does not change the data. The only thing it
# changes is the database's configuration, which means the container is recreated - a
# restart of a few seconds.

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
COMPOSE_FILE="${COMPOSE_FILE:-$SCRIPT_DIR/docker-compose.prod.yml}"
ENV_FILE="${ENV_FILE:-$SCRIPT_DIR/.env}"
CONTAINER="${CONTAINER:-fuelflow-postgres}"
ARCHIVE_DIR="${WAL_ARCHIVE_DIR:-/root/fuelflow-wal}"
# This path is literal in the compose file and has to match. It cannot be a compose
# variable: the parse gate resolves every referenced variable against a dummy value, and
# a dummy value is not a path - compose would read it as the name of a volume that does
# not exist, and the whole file would fail to load.
PG_IMAGE="${PG_IMAGE:-postgres:16-alpine}"
STAGING=0

while [[ $# -gt 0 ]]; do
  case "$1" in
    --staging)     STAGING=1; shift ;;
    --container)   CONTAINER="$2"; shift 2 ;;
    --archive-dir) ARCHIVE_DIR="$2"; shift 2 ;;
    *) echo "unknown argument: $1" >&2; exit 1 ;;
  esac
done

if [[ "$STAGING" -eq 1 ]]; then
  COMPOSE_FILE="${COMPOSE_FILE/.prod.yml/.staging.yml}"
  ENV_FILE="${ENV_FILE/.env/.env.staging}"
  CONTAINER="${CONTAINER/fuelflow-postgres/fuelflow-staging-postgres}"
  ARCHIVE_DIR="${ARCHIVE_DIR/fuelflow-wal/fuelflow-wal-staging}"
fi

echo "Point-in-time recovery setup"
echo "  compose : $COMPOSE_FILE"
echo "  env     : $ENV_FILE"
echo "  database: $CONTAINER"
echo "  archive : $ARCHIVE_DIR"
echo

# Load POSTGRES_USER / POSTGRES_DB from the same .env the stack uses. The role is NOT
# "postgres" - it is whatever the deployment was created with (fuelflow), and connecting
# as postgres fails with "role postgres does not exist".
#
# load_env PARSES the file instead of sourcing it, so a secret containing a space or a
# shell metacharacter cannot break or inject into this run.
# shellcheck source=load-env.sh
source "$SCRIPT_DIR/load-env.sh"
load_env "$ENV_FILE"
# shellcheck source=pitr-common.sh
source "$SCRIPT_DIR/pitr-common.sh"
: "${POSTGRES_USER:?POSTGRES_USER is not set in $ENV_FILE}"
: "${POSTGRES_DB:?POSTGRES_DB is not set in $ENV_FILE}"
echo "  role    : $POSTGRES_USER"
echo# The postgres user inside the image. Not hardcoded: the uid is a property of the image,
# and the image is allowed to change.
PG_UID="$(docker run --rm --entrypoint id "$PG_IMAGE" -u postgres | tr -d ' ')"
PG_GID="$(docker run --rm --entrypoint id "$PG_IMAGE" -g postgres | tr -d ' ')"
echo "[$(date -Is)] The postgres user in ${PG_IMAGE} is ${PG_UID}:${PG_GID}."

# --- 1. The archive directory --------------------------------------------------------
# Owned by the postgres user, and mode 750 so that the WAL segments - which contain the
# business's own data - are not world-readable. The directory is outside the container's
# data volume on purpose: the archive has to survive the container being recreated, and
# the backup script ships it off-box from the host.
if [[ ! -d "$ARCHIVE_DIR" ]]; then
  echo "[$(date -Is)] Creating the archive directory."
  install -d -m 750 -o "$PG_UID" -g "$PG_GID" "$ARCHIVE_DIR"
else
  OWNER="$(stat -c '%u:%g' "$ARCHIVE_DIR")"
  if [[ "$OWNER" != "${PG_UID}:${PG_GID}" ]]; then
    echo "[$(date -Is)] Fixing the ownership of $ARCHIVE_DIR (was ${OWNER})."
    chown "$PG_UID:$PG_GID" "$ARCHIVE_DIR"
  fi
  chmod 750 "$ARCHIVE_DIR"
fi
echo "[$(date -Is)] Archive directory ready: $(ls -ld "$ARCHIVE_DIR")"

# --- 2. Start the stack with archiving on --------------------------------------------
# The compose file carries the archiving settings; this recreates the container so they
# take effect. The data volume is untouched, so the database comes back with its data.
echo "[$(date -Is)] Recreating the database container with archiving on ..."
docker compose -f "$COMPOSE_FILE" --env-file "$ENV_FILE" up -d postgres

echo "[$(date -Is)] Waiting for the database ..."
for _ in $(seq 1 60); do
  if docker exec "$CONTAINER" pg_isready -U "$POSTGRES_USER" >/dev/null 2>&1; then break; fi
  sleep 2
done
if ! docker exec "$CONTAINER" pg_isready -U "$POSTGRES_USER" >/dev/null 2>&1; then
  echo "ERROR: the database did not come back." >&2
  docker logs "$CONTAINER" 2>&1 | tail -30 >&2
  exit 1
fi

# --- 3. Prove that a segment is actually archived -------------------------------------
# A WAL switch forces the current segment to be closed and shipped. If archiving works,
# a NEW file appears in the archive directory within a second or two. If it does not, the
# archive is broken and the operator needs to know now.
#
# The proof is a new FILE in the archive directory, not a counter: pg_stat_archiver
# lags behind the files on disk by up to an archive_timeout interval, and on a quiet
# database that lag is most of the time. A file cannot lag.
echo "[$(date -Is)] Forcing a WAL switch to see whether anything is archived ..."
BEFORE="$(newest_archived_segment "$ARCHIVE_DIR" || echo 0)"
BEFORE_COUNT="$(find "$ARCHIVE_DIR" -maxdepth 1 -name '0000000*' -type f 2>/dev/null | wc -l)"
docker exec "$CONTAINER" psql -U "$POSTGRES_USER" -q -c "SELECT pg_switch_wal()" >/dev/null 2>&1 || true

ARCHIVED=0
for _ in $(seq 1 30); do
  AFTER_COUNT="$(find "$ARCHIVE_DIR" -maxdepth 1 -name '0000000*' -type f 2>/dev/null | wc -l)"
  if [[ "${AFTER_COUNT:-0}" -gt "${BEFORE_COUNT:-0}" ]]; then ARCHIVED=1; break; fi
  sleep 2
done

if [[ "$ARCHIVED" -ne 1 ]]; then
  echo >&2
  echo "ERROR: no new WAL segment appeared in $ARCHIVE_DIR, so point-in-time recovery" >&2
  echo "       is NOT working. The database is running fine, but the archive is empty" >&2
  echo "       and the WAL segments are piling up in pg_wal." >&2
  echo >&2
  echo "       The server's own log says why:" >&2
  docker logs "$CONTAINER" 2>&1 | grep -i 'archive command failed' | tail -5 >&2
  echo >&2
  echo "       Most likely: the archive directory is not writable by uid ${PG_UID}." >&2
  echo "       Check:  ls -ld ${ARCHIVE_DIR}" >&2
  exit 1
fi

# --- 4. What the server thinks happened ------------------------------------------------
echo
echo "[$(date -Is)] Archiving is working."
docker exec "$CONTAINER" psql -U "$POSTGRES_USER" -x -c \
  "SELECT archived_count, last_archived_wal, last_archived_time, failed_count, last_failed_wal FROM pg_stat_archiver"
echo
echo "  Archive directory: $ARCHIVE_DIR"
echo "  Segments so far  : $(ls -1 "$ARCHIVE_DIR" 2>/dev/null | wc -l)"
echo
echo "  Next: take a base backup, or let the nightly run do it."
echo "        deploy/backup.sh   takes the dump, the base backup, and ships both off-box."
echo "        deploy/restore-pitr.sh  restores to a point in time - run it as a drill."
echo
