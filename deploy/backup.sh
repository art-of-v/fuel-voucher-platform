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

# --- Off-host copy -----------------------------------------------------------------
# Encryption protects the contents. It does nothing about the server being destroyed,
# which is the other half of the problem: a backup stored only on the machine it backs
# up is not a backup. Set BACKUP_REMOTE in .env to an rclone remote. The remote is
# Cloudflare R2 (S3-compatible), configured once with `rclone config`, e.g.
#     BACKUP_REMOTE=r2:fuelflow-backups
# See docs/DEPLOYMENT.md "Off-site backups (Cloudflare R2)" for the one-time setup.
if [[ -n "${BACKUP_REMOTE:-}" ]]; then
  if command -v rclone >/dev/null 2>&1; then
    echo "[$(date -Is)] Copying to ${BACKUP_REMOTE} ..."
    rclone copy "$OUT" "$BACKUP_REMOTE"
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
  else
    echo "WARNING: BACKUP_REMOTE is set but rclone is not installed. Backup is ON-SERVER ONLY." >&2
  fi
else
  echo "WARNING: BACKUP_REMOTE is not set. This backup exists ONLY on this server," >&2
  echo "         so it does not survive the server being destroyed. Either set" >&2
  echo "         BACKUP_REMOTE to an rclone remote or enable Hetzner volume snapshots." >&2
fi

# Prune old dumps. Both extensions: .dump.age is current, .dump catches any plaintext
# file left over from before this script encrypted anything.
find "$BACKUP_DIR" -name 'fuelflow_*.dump.age' -type f -mtime "+${RETENTION_DAYS}" -print -delete
find "$BACKUP_DIR" -name 'fuelflow_*.dump' -type f -mtime "+${RETENTION_DAYS}" -print -delete

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
