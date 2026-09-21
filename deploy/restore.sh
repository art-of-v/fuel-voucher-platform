#!/usr/bin/env bash
# ------------------------------------------------------------------------------
# FuelFlow database restore.
#
# Restores a .dump file (created by backup.sh, or by pg_dump -Fc anywhere else)
# into the server's Postgres. This OVERWRITES existing tables.
#
# Usage:
#     cd ~/FuelFlow/deploy
#     ./restore.sh /root/fuelflow-backups/fuelflow_2026-08-20_032001.dump
#
# backup.sh now writes ENCRYPTED dumps ending in .dump.age. Decrypt on the machine that
# holds the private key - normally your laptop, not this server - and copy the plaintext
# .dump across for the duration of the restore:
#     age -d -i fuelflow-backup.key fuelflow_2026-08-20_032001.dump.age > restore.dump
#     scp restore.dump root@DROPLET:/root/
#     ssh root@DROPLET 'cd ~/FuelFlow/deploy && ./restore.sh /root/restore.dump'
#     # then delete the plaintext from both machines:  shred -u restore.dump
#
# If you must decrypt here (private key temporarily on the server), set
# BACKUP_AGE_IDENTITY=/path/to/fuelflow-backup.key and pass the .age file directly.
# Delete the key file afterwards - a server that can decrypt its own backups gives
# up the main protection encrypting them bought.
#
# You can also use this to import an old Supabase database. Make the dump first,
# from your laptop or the server:
#     pg_dump "postgresql://USER:PASSWORD@HOST:5432/postgres" \
#         --no-owner --no-privileges --schema=public -Fc -f from-supabase.dump
# then copy it to the server and run this script on it.
# ------------------------------------------------------------------------------
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
CONTAINER="${CONTAINER:-fuelflow-postgres}"
DUMP="${1:-}"

if [[ -z "$DUMP" ]]; then
  echo "Usage: $0 /path/to/backup.dump" >&2
  exit 1
fi
if [[ ! -f "$DUMP" ]]; then
  echo "ERROR: file not found: $DUMP" >&2
  exit 1
fi
if [[ ! -f "$SCRIPT_DIR/.env" ]]; then
  echo "ERROR: $SCRIPT_DIR/.env not found." >&2
  exit 1
fi

set -a
# shellcheck disable=SC1091
source "$SCRIPT_DIR/.env"
set +a

# --- Decrypt, if handed an encrypted backup ------------------------------------------
PLAINTEXT_IS_TEMPORARY=0
if [[ "$DUMP" == *.age ]]; then
  if [[ -z "${BACKUP_AGE_IDENTITY:-}" ]]; then
    echo "ERROR: $DUMP is encrypted and BACKUP_AGE_IDENTITY is not set." >&2
    echo "       Preferred: decrypt on the machine holding the private key, then pass the .dump:" >&2
    echo "           age -d -i fuelflow-backup.key '$DUMP' > restore.dump" >&2
    echo "       Or set BACKUP_AGE_IDENTITY=/path/to/fuelflow-backup.key and re-run." >&2
    exit 1
  fi
  if ! command -v age >/dev/null 2>&1; then
    echo "ERROR: 'age' is not installed. apt-get install -y age" >&2
    exit 1
  fi

  DECRYPTED="$(mktemp "${TMPDIR:-/tmp}/fuelflow-restore.XXXXXX")"
  chmod 600 "$DECRYPTED"
  PLAINTEXT_IS_TEMPORARY=1
  trap '[[ "$PLAINTEXT_IS_TEMPORARY" == 1 ]] && { command -v shred >/dev/null 2>&1 && shred -u "$DECRYPTED" 2>/dev/null || rm -f "$DECRYPTED"; }' EXIT

  echo "Decrypting $DUMP ..."
  age -d -i "$BACKUP_AGE_IDENTITY" -o "$DECRYPTED" "$DUMP"
  DUMP="$DECRYPTED"
fi

echo "About to restore into database '${POSTGRES_DB}' from:"
echo "    $DUMP"
echo "Existing tables in that database WILL BE REPLACED."
read -r -p "Type 'yes' to continue: " CONFIRM
[[ "$CONFIRM" == "yes" ]] || { echo "Aborted."; exit 1; }

# Stop the API first so it isn't writing while tables are being swapped.
echo "[1/3] Stopping the API..."
docker compose --env-file "$SCRIPT_DIR/.env" -f "$SCRIPT_DIR/docker-compose.prod.yml" stop dotnet-backend

echo "[2/3] Restoring..."
# --clean --if-exists drops existing objects first; --no-owner/--no-privileges
# avoid errors about roles that don't exist on this server (e.g. Supabase roles).
# pg_restore exits non-zero on harmless notices, so don't let -e kill the script.
set +e
docker exec -i "$CONTAINER" pg_restore \
  -U "$POSTGRES_USER" \
  -d "$POSTGRES_DB" \
  --no-owner --no-privileges \
  --clean --if-exists \
  < "$DUMP"
RC=$?
set -e
if (( RC != 0 )); then
  echo "NOTE: pg_restore exited with code ${RC}. Some warnings are normal"
  echo "      (missing roles, extensions already present). Check the output above"
  echo "      for real 'ERROR' lines before trusting the restore."
fi

echo "[3/3] Starting the API again..."
docker compose --env-file "$SCRIPT_DIR/.env" -f "$SCRIPT_DIR/docker-compose.prod.yml" start dotnet-backend

echo
echo "Done. Verify with:"
echo "    curl -fsS https://\${API_DOMAIN}/health"
echo "    docker exec -it $CONTAINER psql -U $POSTGRES_USER -d $POSTGRES_DB -c '\\dt'"
