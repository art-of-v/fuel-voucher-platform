#!/usr/bin/env bash
# ------------------------------------------------------------------------------
# Rebuilds the production Postgres cluster with a Ukrainian collation.
#
#     cd ~/FuelFlow/deploy && ./rebuild-collation.sh
#
# WHY THIS EXISTS
#
# The cluster was initialised with en_US.utf8, which is what initdb falls back to on the
# Alpine image - it ships no usable system locale at all, so "no usable system locales were
# found" is in the log and the default is taken silently. glibc's en_US collation defines no
# ordering for Cyrillic, so Postgres compares those strings by code point. Measured on
# production 2026-10-19:
#
#     en_US.utf8:  Є І Ї А Б В Г Д Е Ж З И Й К Л М Н О П Р С Т У Ф Х Ц Ч Ш Щ Ь Ю Я Ґ
#     uk-UA (ICU): А Б В Г Ґ Д Е Є Ж З И І Ї Й К Л М Н О П Р С Т У Ф Х Ц Ч Ш Щ Ь Ю Я
#
# Ґ at the end and Є/І/Ї at the front is not the Ukrainian alphabet, and every station list,
# fuel list and admin dropdown ordered by name shows it.
#
# PostgreSQL 16 cannot ALTER DATABASE ... LC_COLLATE. The cluster has to be recreated, which
# means a dump and a restore. That is this script. The compose file now carries
# POSTGRES_INITDB_ARGS, so a NEW volume is created with the right locale; this script is
# what changes the existing one.
#
# WHAT IT DOES
#
#   1. dumps the database (application schema AND the hangfire schema)
#   2. stops the backend, so nothing writes during the window
#   3. stops postgres, moves its data volume aside - never deletes it
#   4. brings postgres up, which initialises a fresh cluster with the ICU uk-UA locale
#   5. restores the dump
#   6. starts the backend and waits for health
#
# WHAT IT DOES NOT DO
#
# It does not delete the old volume. It is copied aside under a dated name, so the change is
# reversible by hand. If the restore fails, stop and fix it - do NOT re-run this script,
# which would take a second dump of a half-restored database.
#
# THE WINDOW
#
# Steps 2 to 6 are the downtime: roughly 2-5 minutes for a database this size (30 MB on
# 2026-10-19). Take it when no admin is mid-import and no background job is running. The
# script checks the second of those and refuses to continue if a job is in flight.
#
# THE LOCALE IS NOT THE WHOLE STORY
#
# ICU's uk-UA collation is linguistic, not numeric: "OKKO №1", "OKKO №10", "OKKO №2" still
# sort lexically, because the numbers inside the names are text. That is a query-level fix
# (a natural sort in the ORDER BY), not a cluster setting, and it is not addressed here.
# ------------------------------------------------------------------------------
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$SCRIPT_DIR"

# Which stack. `--staging` rebuilds the STAGING cluster, which is a second, fully
# isolated stack on the same box (its own database, its own volume, its own compose
# project) - and it was still on the old libc collation, so the two environments
# disagreed about every ORDER BY on a Cyrillic column.
#
# Staging matters for more than tidiness: the migration dry run restores PRODUCTION's
# schema into a container, and the staging box is what a second dry run is done
# against. If the two sort differently, the two environments are not the same shape.
TARGET="production"
while [[ $# -gt 0 ]]; do
  case "$1" in
    --staging) TARGET="staging"; shift ;;
    -h|--help) sed -n '2,52p' "$0"; exit 0 ;;
    *) printf '\nERROR: unknown argument: %s\n' "$1" >&2; exit 2 ;;
  esac
done

if [[ "$TARGET" == "staging" ]]; then
  ENV_FILE="${ENV_FILE:-.env.staging}"
  COMPOSE_FILE="${COMPOSE_FILE:-docker-compose.staging.yml}"
  CONTAINER="${CONTAINER:-fuelflow-staging-postgres}"
  BACKEND="${BACKEND:-fuelflow-staging-backend}"
  # Staging has no caddy of its own - it is fronted by the PRODUCTION caddy, which
  # serves the staging domains. The probe still goes through that caddy, and it asks
  # for the backend by its CONTAINER name (`fuelflow-staging-backend`) - which is
  # container name (`fuelflow-staging-backend`).
  #
  # The distinction is not cosmetic: the backend's AllowedHosts list contains
  # `fuelflow-staging-backend` and NOT any other name, so a probe to a name it does
  # answered with 400 Bad Request - a probe that looks like a broken backend and is
  # not. Measured on 2026-10-20.
  CADDY="${CADDY:-fuelflow-caddy}"
  HEALTH_URL="${HEALTH_URL:-http://fuelflow-backend:8080/health}"
  WORK="${WORK:-/root/collation-rebuild-staging}"
else
  ENV_FILE="${ENV_FILE:-.env}"
  COMPOSE_FILE="${COMPOSE_FILE:-docker-compose.prod.yml}"
  CONTAINER="${CONTAINER:-fuelflow-postgres}"
  BACKEND="${BACKEND:-fuelflow-backend}"
  CADDY="${CADDY:-fuelflow-caddy}"
  HEALTH_URL="${HEALTH_URL:-http://fuelflow-backend:8080/health}"
  WORK="${WORK:-/root/collation-rebuild}"
fi

STAMP="$(date +%Y%m%d-%H%M%S)"

say()  { printf '\n=== %s\n' "$*"; }
die()  { printf '\nERROR: %s\n' "$*" >&2; exit 1; }

[[ -f "$SCRIPT_DIR/$ENV_FILE" ]] || die "no $ENV_FILE next to this script - run it from ~/FuelFlow/deploy"

# The database name and user are read from the CONTAINER, not from .env. Sourcing .env is not
# safe: it holds a value with spaces in it (a mail app password) that a shell would split into
# words and try to run. Every other deploy script here uses `docker compose --env-file` for
# exactly that reason, and this one does the same - it only needs two names, and the container
# already has them.
POSTGRES_USER="$(docker exec "$CONTAINER" printenv POSTGRES_USER)"
POSTGRES_DB="$(docker exec "$CONTAINER" printenv POSTGRES_DB)"
[[ -n "$POSTGRES_USER" && -n "$POSTGRES_DB" ]] || die "could not read the database name and user from $CONTAINER"

compose() { docker compose --env-file "$SCRIPT_DIR/$ENV_FILE" -f "$SCRIPT_DIR/$COMPOSE_FILE" "$@"; }
COMPOSE_HINT="docker compose --env-file $ENV_FILE -f $COMPOSE_FILE"

# The volume's real name is project-scoped ("fuelflow_postgres_data"), so it is read off the
# running container rather than guessed. A wrong name here would recreate an empty cluster and
# restore into it, which is recoverable but noisy.
VOLUME="$(docker inspect -f \
  '{{range .Mounts}}{{if eq .Destination "/var/lib/postgresql/data"}}{{.Name}}{{end}}{{end}}' \
  "$CONTAINER")"
[[ -n "$VOLUME" ]] || die "could not find the postgres data volume on $CONTAINER"

say "the volume is: $VOLUME"
say "the dump will be: $WORK/before-$STAMP.dump"

# 1/8: THE BACKEND GOES FIRST.
#
# The order of these steps is the whole point of this rewrite. The script used to
# dump FIRST and stop the backend at step 3 - so the dump was taken while the
# backend was still running and could still start a job, and the "no job in
# flight" check that came after it was checking a moving target.
#
# Stopping the backend first closes that window completely: no new job can be
# enqueued, so "is a job running" is a stable question for the first time.
#
# It also means the dump is taken from a quiescent database, which is the only
# kind of dump worth restoring over a rebuilt cluster.
say "1/8  stopping the backend, so no new job can start"
compose stop fuelflow-backend || true
echo "the backend is stopped."

say "2/8  checking that no background job is RUNNING"
# THE QUESTION, NOT A PROXY FOR IT.
#
# The check used to be `SELECT count(*) FROM hangfire.job WHERE statename <> 'Succeeded'`.
# That is not "is a job running" - it is "how many job rows have ever existed that do
# not say Succeeded", and Hangfire keeps those rows. Measured on the production box on
# 2026-10-20: 4454. On staging: 4120. The number only ever goes up, so the check could
# never be zero, and the script could never run on a box that had ever run a recurring
# job. It was a guard that always refused.
#
# A non-empty jobqueue is not that either: it is work WAITING to be picked up, and
# with the backend stopped nothing will pick it up. It was counted in an intermediate
# version of this check and it refused a perfectly safe window - measured 6 on staging
# with the backend stopped and nothing running.
#
# What "in flight" means is one thing: a hangfire.state row named 'Processing' created
# in the last two minutes. That is a job EXECUTING right now.
#
# AND WHY THIS WAITS RATHER THAN REFUSING
#
# Stopping the backend kills whatever job was running at that moment, and its
# 'Processing' row stays in the table - Hangfire's reaper re-queues the job later, but
# the row itself does not change. So right after a stop, the count is the number of jobs
# that were running a moment ago, and refusing there would refuse every rebuild on
# every box with recurring jobs. Which is what happened: the first attempt at this
# measured 5 and stopped, and the 5 were the jobs the stop had just killed.
#
# Two minutes later those rows are outside the window and the count is 0 - not because
# the rows changed, but because they aged out. So the script WAITS for the window to
# pass, and says what it is waiting for. Waiting is the right move here: the jobs are
# already dead, and a job that died mid-write leaves a consistent snapshot behind
# (pg_dump is transactional) and is re-queued by the reaper afterwards.
#
# With the backend stopped, the count is stable, so this is a real gate rather than a
# race.
RUNNING=0
for _ in $(seq 1 40); do
  RUNNING="$(docker exec -i "$CONTAINER" psql -U "$POSTGRES_USER" -d "$POSTGRES_DB" -tAX \
    -c "SELECT count(*) FROM hangfire.state
         WHERE name = 'Processing' AND createdat > now() - interval '2 minutes';")"
  RUNNING="${RUNNING:-1}"
  [[ "$RUNNING" == "0" ]] && break
  echo "  $RUNNING job row(s) still inside the two-minute window - waiting for them to age out."
  sleep 5
done
echo "jobs running right now: $RUNNING"
[[ "$RUNNING" == "0" ]] \
  || die "a job is still running after two minutes. Wait for it to finish, then re-run."

say "3/8  dumping the database"
mkdir -p "$WORK"; chmod 700 "$WORK"
# -Fc so pg_restore can select; both schemas, because hangfire's tables live in this
# database too and the reaper needs them.
docker exec "$CONTAINER" pg_dump -U "$POSTGRES_USER" -d "$POSTGRES_DB" -Fc \
  > "$WORK/before-$STAMP.dump"
ls -lh "$WORK/before-$STAMP.dump"
# A dump that cannot be read back is not a backup. Verify it parses before touching anything.
docker exec -i "$CONTAINER" pg_restore --list < "$WORK/before-$STAMP.dump" > /dev/null \
  || die "the dump does not parse - stopping before anything is changed"

say "4/8  stopping the database"
compose stop postgres || true
# The container has to be gone, not merely stopped, before its volume can be detached.
compose rm -f postgres || true

say "5/8  moving the data volume aside (not deleting it)"
OLD_VOLUME="$VOLUME-recollation-$STAMP"
docker volume create "$OLD_VOLUME" > /dev/null
# PostgreSQL was shut down cleanly by `compose stop`, so copying the data directory is safe.
docker run --rm -v "$VOLUME":/from -v "$OLD_VOLUME":/to alpine:3 \
  sh -c 'cp -a /from/. /to/ && rm -rf /from/*'
docker volume rm "$VOLUME"
docker volume create "$VOLUME" > /dev/null
echo "the previous data is kept as: $OLD_VOLUME"

say "6/8  creating the cluster with the ICU uk-UA locale"
# POSTGRES_INITDB_ARGS comes from the compose file, so the new cluster is created with the
# same settings a fresh install would get.
compose up -d postgres
for _ in $(seq 1 90); do
  if docker exec "$CONTAINER" pg_isready -U "$POSTGRES_USER" -d "$POSTGRES_DB" > /dev/null 2>&1; then break; fi
  sleep 1
done
docker exec "$CONTAINER" pg_isready -U "$POSTGRES_USER" -d "$POSTGRES_DB" \
  || { docker logs "$CONTAINER" | tail -30; die "postgres did not come up"; }

say "    verifying the locale is the one we asked for"
docker exec -i "$CONTAINER" psql -U "$POSTGRES_USER" -d "$POSTGRES_DB" -tAX \
  -c "SELECT datlocprovider, daticulocale FROM pg_database WHERE datname = current_database();" \
  | tee "$WORK/locale-$STAMP.txt"
grep -q 'uk-UA' "$WORK/locale-$STAMP.txt" \
  || die "the database was created without the uk-UA ICU locale - stop here and read the initdb log"

say "7/8  restoring the dump"
docker exec -i "$CONTAINER" pg_restore -U "$POSTGRES_USER" -d "$POSTGRES_DB" \
  --no-owner --no-privileges --exit-on-error < "$WORK/before-$STAMP.dump"

say "    checking the row counts came back"
docker exec -i "$CONTAINER" psql -U "$POSTGRES_USER" -d "$POSTGRES_DB" -tAX \
  -c "SELECT 'fuel_types=' || (SELECT count(*) FROM fuel_types) || ' brands=' || (SELECT count(*) FROM brands) || ' brand_stations=' || (SELECT count(*) FROM brand_stations) || ' orders=' || (SELECT count(*) FROM orders);"

say "8/8  starting the backend and waiting for health"
  compose up -d fuelflow-backend
  # The probe goes through caddy, not into the backend container: the backend image has no wget
  # (a distroless-ish .NET image), and caddy's is the path the outside world uses anyway.
  for _ in $(seq 1 60); do
    if docker exec "$CADDY" wget -qO- "$HEALTH_URL" > /dev/null 2>&1; then break; fi
    sleep 2
  done
  docker exec "$CADDY" wget -qO- "$HEALTH_URL" \
    || echo "(the health endpoint did not answer - check the logs)"

  say "the Ukrainian order, from the restored database"
  docker exec -i "$CONTAINER" psql -U "$POSTGRES_USER" -d "$POSTGRES_DB" -tAX \
    -c "SELECT string_agg(v, ' ' ORDER BY v) FROM (VALUES ('Їжак'),('Ганок'),('Яблуко'),('Бджола'),('Ґедзь'),('Єва'),('Іван'),('Їжа')) w(v);"

  # The last word on the matter, from the same script that will be run against the
  # cluster next time: does it sort the way a Ukrainian reader expects? The columns
  # above said it does; this says what it actually does.
  say "the behavioural check, which is the one that counts"
  bash "$SCRIPT_DIR/assert-collation.sh" --container "$CONTAINER" --user "$POSTGRES_USER" --db "$POSTGRES_DB" \
    || die "the restored database does not sort by the Ukrainian alphabet - STOP and read the output above"

  cat <<EOF

  Done. The database now collates in Ukrainian.

    target:        $TARGET
    dump kept at:  $WORK/before-$STAMP.dump
    old volume:    $OLD_VOLUME

  If the sort order above is the Ukrainian alphabet, this worked. If anything looks
  wrong, stop the backend, drop the new volume, copy the old one back and start
  postgres:

    $COMPOSE_HINT stop fuelflow-backend postgres
    docker volume rm $VOLUME
    docker volume create $VOLUME
    docker run --rm -v $OLD_VOLUME:/from -v $VOLUME:/to alpine:3 sh -c 'cp -a /from/. /to/'
    $COMPOSE_HINT up -d postgres


Do not re-run this script to "fix" a bad restore: it would take a second dump of a
half-restored database.
EOF
