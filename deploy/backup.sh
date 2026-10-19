#!/usr/bin/env bash
# ------------------------------------------------------------------------------
# FuelFlow database backup.
#
# Dumps the Postgres database, verifies the dump is readable, encrypts it to a key
# this server cannot decrypt, optionally copies it off-box, and prunes old files.
#
# One-off run:
#     cd ~/FuelFlow/deploy && ./backup.sh
#
# Nightly at 03:20 (run `crontab -e` and add this line):
#     20 3 * * * cd /root/FuelFlow/deploy && ./backup.sh >> /var/log/fuelflow-backup.log 2>&1
#
# ------------------------------------------------------------------------------
# WHY THE DUMPS ARE ENCRYPTED
#
# A pg_dump of this database is the single highest-value file on the server. It
# contains every customer phone number, every order, and - critically - the QR
# payloads of unredeemed fuel vouchers. Those QRs are bearer instruments: they are
# redeemed at a WOG/OKKO/KLO pump, so FuelFlow cannot revoke one that leaks. It also
# contains the refresh-token table.
#
# Plaintext dumps sitting in /root turn any read-only exposure - a stolen server
# snapshot, a mis-scoped rclone remote, a support engineer with shell access, a
# resold disk - into total compromise of everything the platform holds.
#
# Encryption is ASYMMETRIC on purpose. The server holds only the public key, so a
# full compromise of the server still cannot decrypt yesterday's backup. Keep the
# private key OFF this machine (password manager, or a laptop you control).
#
# One-time setup, on your laptop and NOT on the server:
#     age-keygen -o fuelflow-backup.key      # store this file in your password manager
#     # it prints: Public key: age1xxxxxxxxxxxxxxxxxxxxxxxxxxxxxx
# then add to deploy/.env on the server:
#     BACKUP_AGE_RECIPIENT=age1xxxxxxxxxxxxxxxxxxxxxxxxxxxxxx
#
# Install age on the server:  apt-get install -y age
#
# To restore, decrypt on the machine that has the private key, then copy the .dump
# over and run ./restore.sh:
#     age -d -i fuelflow-backup.key fuelflow_2026-08-21_032001.dump.age > restore.dump
# ------------------------------------------------------------------------------
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
BACKUP_DIR="${BACKUP_DIR:-/root/fuelflow-backups}"
RETENTION_DAYS="${RETENTION_DAYS:-14}"
# The off-site (rclone) copy is the disaster-recovery copy, so it is kept longer than the
# on-box window. R2 storage for a handful of small encrypted dumps is negligible.
BACKUP_REMOTE_RETENTION_DAYS="${BACKUP_REMOTE_RETENTION_DAYS:-30}"
CONTAINER="${CONTAINER:-fuelflow-postgres}"

# --- Point-in-time recovery ----------------------------------------------------------
# The dump above is a snapshot: it is the database as of 03:23, and everything that happened
# between the previous snapshot and it is gone. On a payment system that is a day of orders,
# so the recovery point objective is a full day - and a day is not a number anybody in this
# business is comfortable with.
#
# WAL archiving fixes that: PostgreSQL copies the write-ahead log off the server as it
# fills, and a restore can replay it to any moment in time. A base backup is what the
# replay starts from, so both are needed - the base is the starting point, the WAL is the
# way to move forward from it.
#
# The base backup is taken every night, alongside the dump, for the same reason the dump
# is: one schedule, one set of files, one restore procedure. The database is small enough
# that this costs almost nothing, and it means the base is never more than a day old, so
# the WAL that has to be replayed on top of it is short.
WAL_ARCHIVE_DIR="${WAL_ARCHIVE_DIR:-/root/fuelflow-wal}"
# The archive path is literal in the compose file, and it has to match this one. It cannot
# be a compose variable: the parse gate resolves every referenced variable against a dummy
# value, and a dummy value is not a path - compose would read it as the name of a volume
# that does not exist, and the whole file would fail to load.
# The WAL is kept on-box for as long as the dumps, and off-box for as long as the dumps.
# A base backup and a WAL archive that does not overlap are a restore that cannot start.
WAL_RETENTION_DAYS="${WAL_RETENTION_DAYS:-14}"
WAL_REMOTE_RETENTION_DAYS="${WAL_REMOTE_RETENTION_DAYS:-30}"

# Load POSTGRES_USER / POSTGRES_DB / BACKUP_AGE_RECIPIENT from the same .env the stack uses.
# load_env PARSES .env instead of sourcing it, so a secret containing a space (e.g. a
# Gmail app password) or shell metacharacters can't break or inject into this run.
# shellcheck source=load-env.sh
source "$SCRIPT_DIR/load-env.sh"
load_env "$SCRIPT_DIR/.env"

# --- Refuse to write a plaintext dump ----------------------------------------------
# This check is deliberately fatal. A backup script that quietly degrades to plaintext
# when a tool is missing is worse than no backup, because the operator believes the
# data is protected.
if ! command -v age >/dev/null 2>&1; then
  echo "ERROR: 'age' is not installed, so this dump cannot be encrypted." >&2
  echo "       Install it:  apt-get update && apt-get install -y age" >&2
  echo "       Refusing to write an unencrypted database dump." >&2
  exit 1
fi
if [[ -z "${BACKUP_AGE_RECIPIENT:-}" ]]; then
  echo "ERROR: BACKUP_AGE_RECIPIENT is not set in $SCRIPT_DIR/.env." >&2
  echo "       Generate a keypair on your LAPTOP (not here):  age-keygen -o fuelflow-backup.key" >&2
  echo "       Put the printed public key in .env as BACKUP_AGE_RECIPIENT=age1..." >&2
  echo "       Refusing to write an unencrypted database dump." >&2
  exit 1
fi

# 0700: even the directory listing of backup filenames is not interesting to anyone else.
mkdir -p "$BACKUP_DIR"
chmod 700 "$BACKUP_DIR"

STAMP="$(date +%Y-%m-%d_%H%M%S)"
OUT="$BACKUP_DIR/fuelflow_${STAMP}.dump.age"

# The plaintext dump exists only inside this run, only in a 0600 file, and only long
# enough to be validated and encrypted.
TMP_PLAIN="$(mktemp "${TMPDIR:-/tmp}/fuelflow-dump.XXXXXX")"
chmod 600 "$TMP_PLAIN"
cleanup() {
  # shred if available (overwrites before unlinking), plain rm otherwise.
  if command -v shred >/dev/null 2>&1; then
    shred -u "$TMP_PLAIN" 2>/dev/null || rm -f "$TMP_PLAIN"
  else
    rm -f "$TMP_PLAIN"
  fi
}
trap cleanup EXIT

echo "[$(date -Is)] Backing up '${POSTGRES_DB}' -> ${OUT}"

# --- Is the WAL archive actually working? ---------------------------------------------
# `archive_mode=on` is a setting, not a guarantee.
#
# The normal way this breaks: the archive directory is created on the host as root and
# bind-mounted into the container, and the container's postgres user (uid 70) cannot write
# to it. Every archive attempt then fails, the database carries on running perfectly, and
# the backup looks completely healthy - while there is no archive at all. It was found
# exactly this way, by watching the server's log complain about it.
#
# The check is made again at the end of the run, and the run FAILS then if the archive is
# still broken: a backup that cannot be replayed to a point in time is not a backup, it
# is a snapshot with extra steps.
#
# shellcheck source=pitr-common.sh
source "$SCRIPT_DIR/pitr-common.sh"

ARCHIVER_OK=0
check_wal_archive "$CONTAINER" "$WAL_ARCHIVE_DIR" "$POSTGRES_USER" "$POSTGRES_DB" 1800 || ARCHIVER_OK=1

# -Fc = compressed custom format, which restore.sh expects.
docker exec "$CONTAINER" pg_dump \
  -U "$POSTGRES_USER" \
  -d "$POSTGRES_DB" \
  --no-owner --no-privileges \
  -Fc > "$TMP_PLAIN"

# A dump smaller than ~1 KB means something went wrong — don't keep it silently.
SIZE="$(stat -c%s "$TMP_PLAIN")"
if (( SIZE < 1024 )); then
  echo "ERROR: dump is only ${SIZE} bytes — treating as failed." >&2
  exit 1
fi

# Prove the archive is structurally readable before it becomes the thing we rely on.
# `pg_restore --list` parses the whole custom-format table of contents without touching
# any database. A dump that fails here is truncated or corrupt, and finding that out now
# is the entire point - the alternative is finding out during an outage.
#
# Done BEFORE encryption on purpose: the server has no private key, so this is the only
# place the check can happen without shipping the key here.
if ! docker exec -i "$CONTAINER" pg_restore --list > /dev/null < "$TMP_PLAIN"; then
  echo "ERROR: pg_restore could not read the dump's table of contents — it is corrupt." >&2
  exit 1
fi
TABLES="$(docker exec -i "$CONTAINER" pg_restore --list < "$TMP_PLAIN" | grep -c '^[0-9;].*TABLE DATA' || true)"
echo "[$(date -Is)] Dump verified readable: ${TABLES} tables with data."
if (( TABLES < 1 )); then
  echo "ERROR: the dump contains no table data. Treating as failed." >&2
  exit 1
fi

# Encrypt to the off-host public key. umask so the ciphertext is 0600 from birth.
( umask 077; age -r "$BACKUP_AGE_RECIPIENT" -o "$OUT" "$TMP_PLAIN" )

echo "[$(date -Is)] OK: $(du -h "$OUT" | cut -f1) encrypted to ${BACKUP_AGE_RECIPIENT}"

# --- Base backup (the starting point for a point-in-time restore) --------------------
# pg_basebackup copies the data directory itself, plus the WAL needed to make it
# consistent. Unlike pg_dump it is a PHYSICAL copy, so a restore replays the WAL on top
# of it rather than replaying SQL - which is what makes "restore to 14:37" possible.
#
# -Ft is the plain tar format: a single stream that can be piped straight out of the
# container and encrypted on the host.
# -X fetch pulls the WAL needed for a consistent snapshot into the backup itself. (-X
# stream would be nicer, but PostgreSQL refuses to stream WAL in tar mode to stdout:
# "cannot stream write-ahead logs in tar mode to stdout".)
# --checkpoint=fast does not wait for a scheduled checkpoint; on a busy server that wait
# is the difference between a backup that takes a minute and one that takes an hour.
#
# The manifest is left ON (no --no-manifest): pg_verifybackup needs it, and without it
# there is no way to prove the backup is not corrupt.
BASE_TMP="$(mktemp -d "${TMPDIR:-/tmp}/fuelflow-base.XXXXXX")"
chmod 700 "$BASE_TMP"
BASE_OUT="$BACKUP_DIR/fuelflow_base_${STAMP}.tar.age"

echo "[$(date -Is)] Taking base backup -> ${BASE_OUT}"
if docker exec "$CONTAINER" pg_basebackup \
      -U "$POSTGRES_USER" \
      -D - -Ft -X fetch --checkpoint=fast \
      > "$BASE_TMP/base.tar" 2>"$BASE_TMP/base.err"; then
  # Verify it before it becomes the thing we rely on. pg_verifybackup walks the manifest
  # and checks every file's checksum - a base backup that fails here is corrupt, and
  # finding that out now is the entire point.
  #
  # It needs a DIRECTORY, so the tar is unpacked into the container's /tmp first. If the
  # check cannot be performed, that is a failure, not a pass: a backup nobody verified is
  # a belief, not a backup.
  if docker exec -i "$CONTAINER" sh -c \
      'rm -rf /tmp/bbcheck && mkdir -p /tmp/bbcheck && tar -xf - -C /tmp/bbcheck && pg_verifybackup /tmp/bbcheck; rc=$?; rm -rf /tmp/bbcheck; exit $rc' \
      < "$BASE_TMP/base.tar" >/dev/null 2>&1; then
    echo "[$(date -Is)] Base backup verified by pg_verifybackup."
  else
    echo "ERROR: pg_verifybackup could not verify the base backup - treating as failed." >&2
    exit 1
  fi
  ( umask 077; age -r "$BACKUP_AGE_RECIPIENT" -o "$BASE_OUT" "$BASE_TMP/base.tar" )
  echo "[$(date -Is)] OK: base backup $(du -h "$BASE_OUT" | cut -f1) encrypted."
else
  echo "ERROR: pg_basebackup failed:" >&2
  cat "$BASE_TMP/base.err" >&2
  exit 1
fi

# --- Off-host copy -----------------------------------------------------------------
# Encryption protects the contents. It does nothing about the server being destroyed,
# which is the other half of the problem: a backup stored only on the machine it backs
# up is not a backup. Set BACKUP_REMOTE in .env to an rclone remote. The remote is
# Cloudflare R2 (S3-compatible), configured once with `rclone config`, e.g.
#     BACKUP_REMOTE=r2:fuelflow-backups
# See docs/DEPLOYMENT.md (private docs repo) "Off-site backups (Cloudflare R2)" for the one-time setup.
if [[ -n "${BACKUP_REMOTE:-}" ]]; then
  if command -v rclone >/dev/null 2>&1; then
    echo "[$(date -Is)] Copying to ${BACKUP_REMOTE} ..."
    rclone copy "$OUT" "$BACKUP_REMOTE"
    # The base backup goes off-box too, and it matters as much as the dump does: without
    # it, the WAL archive on the remote is a log nobody can replay.
    rclone copy "$BASE_OUT" "$BACKUP_REMOTE"
    echo "[$(date -Is)] Off-host copy done."

    # Prune the REMOTE too. Without this, `rclone copy` adds a dump every night and never
    # removes one, so the bucket — and the R2 bill — grow without bound. --include scopes
    # the delete to our own dumps, so a shared bucket/prefix is never touched. This is NOT
    # fatal: the copy above already succeeded, and a transient remote hiccup on cleanup
    # should not page anyone via the OnFailure alert or discard tonight's good backup.
    echo "[$(date -Is)] Pruning remote dumps older than ${BACKUP_REMOTE_RETENTION_DAYS} days ..."
    if rclone delete --min-age "${BACKUP_REMOTE_RETENTION_DAYS}d" \
         --include 'fuelflow_*.dump.age' "$BACKUP_REMOTE"; then
      echo "[$(date -Is)] Remote prune done."
    else
      echo "WARNING: remote prune failed (the copy above still succeeded). The remote may" >&2
      echo "         accumulate old dumps until the next successful run." >&2
    fi
    if rclone delete --min-age "${BACKUP_REMOTE_RETENTION_DAYS}d" \
         --include 'fuelflow_base_*.tar.age' "$BACKUP_REMOTE"; then
      echo "[$(date -Is)] Remote base backup prune done."
    else
      echo "WARNING: remote base backup prune failed (the copy above still succeeded)." >&2
    fi
  else
    echo "WARNING: BACKUP_REMOTE is set but rclone is not installed. Backup is ON-SERVER ONLY." >&2
  fi
else
  echo "WARNING: BACKUP_REMOTE is not set. This backup exists ONLY on this server," >&2
  echo "         so it does not survive the server being destroyed. Either set" >&2
  echo "         BACKUP_REMOTE to an rclone remote or enable Hetzner volume snapshots." >&2
fi

# --- The WAL archive ----------------------------------------------------------------
# The archive is what makes a point-in-time restore possible, so it has to leave the box
# as well - a base backup on its own is a snapshot with a very precise, very unhelpful
# timestamp.
#
# The archive is NOT encrypted file by file: it is thousands of small segments, and
# encrypting each one separately means thousands of age invocations and a restore that has
# to decrypt them one at a time. Instead the whole archive is tarred and encrypted as one
# blob, and re-encrypted nightly. The cost is that the newest segment is only off-box
# once a night - which is the same granularity the dump has, and still an enormous
# improvement on "the archive is only on the disk that died".
if [[ -n "${BACKUP_REMOTE:-}" ]] && command -v rclone >/dev/null 2>&1 && [[ -d "$WAL_ARCHIVE_DIR" ]]; then
  SEGMENTS="$(find "$WAL_ARCHIVE_DIR" -maxdepth 1 -name '0000000*' -type f | wc -l)"
  echo "[$(date -Is)] Shipping the WAL archive (${SEGMENTS} segments) off-box ..."
  WAL_TMP="$(mktemp -d "${TMPDIR:-/tmp}/fuelflow-wal.XXXXXX")"
  chmod 700 "$WAL_TMP"
  # Only the segments that are actually complete: a segment still being written is
  # truncated, and shipping a truncated segment means a restore that stops in the middle
  # of it. PostgreSQL writes each segment to a temporary name and renames it when it is
  # complete, so anything matching the segment pattern is complete.
  tar --force-local -C "$WAL_ARCHIVE_DIR" -czf "$WAL_TMP/wal.tar.gz" --wildcards '0000000*' 2>/dev/null || true
  if [[ -s "$WAL_TMP/wal.tar.gz" ]]; then
    ( umask 077; age -r "$BACKUP_AGE_RECIPIENT" -o "$BACKUP_DIR/fuelflow_wal.tar.gz.age" "$WAL_TMP/wal.tar.gz" )
    rclone copy "$BACKUP_DIR/fuelflow_wal.tar.gz.age" "$BACKUP_REMOTE"
    echo "[$(date -Is)] WAL archive shipped: $(du -h "$BACKUP_DIR/fuelflow_wal.tar.gz.age" | cut -f1)."
  else
    echo "WARNING: the WAL archive is empty - no segments have been archived yet." >&2
    echo "         Point-in-time recovery is not yet possible; check that archive_mode is on." >&2
  fi
  rm -rf "$WAL_TMP"
else
  echo "WARNING: the WAL archive was NOT shipped off-box (remote=${BACKUP_REMOTE:-unset}," >&2
  echo "         rclone=$(command -v rclone >/dev/null 2>&1 && echo yes || echo no)," >&2
  echo "         dir=${WAL_ARCHIVE_DIR}). Point-in-time recovery still works from this" >&2
  echo "         server alone, but not if the server is destroyed." >&2
fi

# Prune the local WAL archive. The oldest segment is the one whose age matters, and the
# newest is always kept - PostgreSQL is writing to it right now. Pruning by mtime is
# right here: a segment's age IS its age, and the archive is append-only.
if [[ -d "$WAL_ARCHIVE_DIR" ]]; then
  find "$WAL_ARCHIVE_DIR" -maxdepth 1 -name '0000000*' -type f -mtime "+${WAL_RETENTION_DAYS}" -print -delete
fi

# Prune old dumps. Both extensions: .dump.age is current, .dump catches any plaintext
# file left over from before this script encrypted anything.
find "$BACKUP_DIR" -name 'fuelflow_*.dump.age' -type f -mtime "+${RETENTION_DAYS}" -print -delete
find "$BACKUP_DIR" -name 'fuelflow_*.dump' -type f -mtime "+${RETENTION_DAYS}" -print -delete
find "$BACKUP_DIR" -name 'fuelflow_base_*.tar.age' -type f -mtime "+${RETENTION_DAYS}" -print -delete

echo "[$(date -Is)] Done. Kept the last ${RETENTION_DAYS} days:"
ls -lh "$BACKUP_DIR" | tail -n 5

# Nag about any leftover plaintext dumps from before encryption was added.
LEGACY="$(find "$BACKUP_DIR" -name 'fuelflow_*.dump' -type f | wc -l)"
if (( LEGACY > 0 )); then
  echo >&2
  echo "WARNING: ${LEGACY} UNENCRYPTED dump(s) are still in $BACKUP_DIR." >&2
  echo "         They predate encryption and are full plaintext copies of the database." >&2
  echo "         Remove them once you have an encrypted backup you trust:" >&2
  echo "             find $BACKUP_DIR -name 'fuelflow_*.dump' -type f -exec shred -u {} +" >&2
fi

# The last word on the archive. The check at the start of the run may have found the
# archive broken; this one says whether it is still broken NOW, after the base backup was
# taken and shipped. A run that produced a dump but cannot produce a replayable history
# has not succeeded, and says so with a non-zero exit - which is what the OnFailure
# alert is for.
if [[ "$ARCHIVER_OK" -ne 0 ]]; then
  echo >&2
  echo "ERROR: the WAL archive was not healthy, so this backup cannot be replayed to a" >&2
  echo "       point in time. The dump itself was made and shipped, but the recovery" >&2
  echo "       point objective is still a full day until archiving is fixed." >&2
  exit 1
fi
check_wal_archive "$CONTAINER" "$WAL_ARCHIVE_DIR" "$POSTGRES_USER" "$POSTGRES_DB" 1800 || exit 1
