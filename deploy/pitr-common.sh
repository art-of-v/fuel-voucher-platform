#!/usr/bin/env bash
# Shared helpers for the point-in-time recovery scripts. Sourced, not executed.
#
# The one thing these scripts have in common is that they have to tell the truth about
# whether WAL archiving is working, because everything else - the base backup, the
# recovery point objective, the whole point of the exercise - depends on it.
#
# There is a counter in PostgreSQL for this: pg_stat_archiver.archived_count. It is
# tempting to gate on it, and it is the wrong thing to gate on, because it LAGS.
#
# Measured while writing this: a WAL segment was copied into the archive at 19:04:19 and
# the counter still read 0 for another 27 seconds, until the NEXT segment was archived.
# A check that reads the counter can therefore report "archiving is broken" while the
# archive is full of fresh segments - and, on a quiet database, that is most of the time.
#
# So the check looks at the ARCHIVE DIRECTORY, which cannot lag: if the newest segment
# on disk is recent, then archiving is working, whatever the counter says. The counter is
# still read, because it is the thing that says whether the archiver is ERRORING - which
# is the other half of the question.
#
# AND THE OTHER HALF, added on 2026-10-22: a database that writes nothing produces no
# WAL, and no WAL means no completed segment, and no completed segment means the archive
# does not grow. That is not a broken archive - it is a quiet database - and a check
# that cannot tell the two apart calls a quiet database a broken one. It did exactly
# that on 2026-10-22: the API had been down for nine hours, the database had nothing to
# write, the archive had not grown since 21:19, and the nightly backup failed at 03:21
# with "the WAL archive was not healthy" and paged whoever was on call - about a backup
# that was made, encrypted, shipped and replayable. The segments in the archive were
# contiguous; the history had no hole in it; the alarm was about a quiet database.
#
# The honest signal is the ARCHIVER'S QUEUE. PostgreSQL marks a completed WAL segment
# with a ".ready" file in <data directory>/pg_wal/archive_status, and removes the file
# when the archive command has succeeded. So:
#
#   - a ".ready" file that has been sitting there is work the archiver has not done,
#     and a restore cannot replay past it: that IS a hole in the history;
#   - no ".ready" file means the archiver has nothing to do: the archive holds every
#     segment that exists, and if the archive looks old it is because the database has
#     not written since.
#
# The two are told apart by facts, not by a guess: the age of the oldest pending
# segment, the age of the archiver's last success and of its last failure, and the age
# of the newest file in the archive directory.

# The newest segment in the archive, as an epoch timestamp. Prints nothing if there is
# none.
newest_archived_segment() {
  local dir="$1"
  [[ -d "$dir" ]] || return 1
  find "$dir" -maxdepth 1 -name '0000000*' -type f -printf '%T@\n' 2>/dev/null \
    | sort -n | tail -1 | cut -d. -f1
}

# The age in seconds of the OLDEST segment the archiver has not archived yet, or
# nothing when the archiver has nothing waiting.
#
# A completed segment leaves a "<segment>.ready" file in the archive_status directory;
# the archiver deletes it when the archive command has succeeded. An empty queue is
# the normal state of a healthy archiver on a quiet database - and the whole point of
# looking here rather than at the age of the newest archived file.
#
# Prints the age, or returns 1 when there is nothing pending.
oldest_pending_segment_age() {
  local container="$1" data_dir="$2"
  [[ -n "$data_dir" ]] || return 1

  # The path is passed as an argument, not interpolated into the script, so a data
  # directory with a quote in it cannot become a command.
  local pending
  pending="$(docker exec "$container" sh -c 'ls -1 "$1/archive_status" 2>/dev/null' sh "$data_dir" \
    | grep '\.ready$' || true)"
  [[ -n "$pending" ]] || return 1

  local now name mtime age oldest=0
  now="$(date +%s)"
  while read -r name; do
    [[ -n "$name" ]] || continue
    mtime="$(docker exec "$container" stat -c %Y "$data_dir/archive_status/$name" 2>/dev/null || true)"
    [[ -n "$mtime" ]] || continue
    age=$(( now - mtime ))
    if (( oldest == 0 || age > oldest )); then oldest=$age; fi
  done <<<"$pending"

  (( oldest > 0 )) || return 1
  echo "$oldest"
}

# wal_archive_health <newest_age> <pending_age> <success_age> <failure_age> [max_age]
#
# The verdict on the archive, as a function of the facts about it. Split out from
# check_wal_archive so it can be tested without a database: the facts are numbers.
#
#   newest_age   seconds since the newest archived segment was written, or -1 when the
#                archive holds no segments at all.
#   pending_age  seconds since the archiver's OLDEST pending segment was completed, or
#                -1 when the archiver has nothing waiting.
#   success_age  seconds since the archiver last archived something, or -1 if it never has.
#   failure_age  seconds since the archiver last failed, or -1 if it never has.
#
# Prints why when the archive is NOT healthy. Returns 0 when it is healthy.
wal_archive_health() {
  local newest_age="$1" pending_age="$2" success_age="$3" failure_age="$4" max_age="${5:-1800}"

  if (( newest_age < 0 )); then
    echo "ERROR: the archive holds no WAL segments at all, so nothing has ever been archived." >&2
    echo "       Point-in-time recovery is not available. The usual cause is the directory" >&2
    echo "       not being writable by the postgres user - see deploy/enable-pitr.sh." >&2
    return 1
  fi

  # Work the archiver has not done. A completed segment that is not archived is a hole
  # in the history: a restore cannot replay past it.
  if (( pending_age >= 0 && pending_age > max_age )); then
    echo "ERROR: the archiver has a segment waiting for ${pending_age}s (limit ${max_age}s)." >&2
    echo "       A completed segment that is not archived is a hole in the history - a" >&2
    echo "       restore cannot replay past it. The server's log says why:" >&2
    return 1
  fi

  # A failure that is OLDER than the last success is a failure that has been fixed -
  # the archiver moved on. A failure that is NEWER means the archiver is failing now.
  if (( failure_age >= 0 && (success_age < 0 || failure_age < success_age) )); then
    echo "ERROR: the archiver's last failure (${failure_age}s ago) is more recent than its" >&2
    echo "       last success, so archiving is failing now. The server's log says why:" >&2
    return 1
  fi

  # The archiver says it archived something, and the archive directory does not have
  # it: its output is not landing where the restore will look for it.
  #
  # The slack is a second of arithmetic: the file's mtime is written when the copy
  # finishes and the archiver records its success when the command returns, and the two
  # are read as whole seconds, so they can differ by one. Sixty seconds is slack; a
  # misconfigured archive command is minutes away.
  if (( success_age >= 0 && success_age + 60 < newest_age )); then
    echo "ERROR: the archiver archived something ${success_age}s ago, but the newest file" >&2
    echo "       in the archive is ${newest_age}s old - its output is not landing in the" >&2
    echo "       archive directory, or something is deleting what it writes." >&2
    return 1
  fi

  return 0
}

# check_wal_archive <container> <archive-dir> <user> <database> [max-age-seconds]
#
# Prints what it found. Returns 0 when archiving is healthy, 1 when it is not.
check_wal_archive() {
  local container="$1" dir="$2" user="$3" database="$4" max_age="${5:-1800}"
  local newest newest_age now row archived failed last_success last_failure
  local last_wal current_segment data_dir pending_age

  now="$(date +%s)"

  # --- the archive directory, which cannot lie -----------------------------------------
  if [[ ! -d "$dir" ]]; then
    echo "ERROR: the archive directory does not exist: $dir" >&2
    return 1
  fi
  newest="$(newest_archived_segment "$dir" || true)"
  if [[ -z "$newest" ]]; then
    echo "ERROR: the archive directory $dir contains no WAL segments at all." >&2
    echo "       Nothing has ever been archived, so point-in-time recovery is not" >&2
    echo "       available. The usual cause is the directory not being writable by the" >&2
    echo "       postgres user - see deploy/enable-pitr.sh." >&2
    return 1
  fi
  newest_age=$(( now - newest ))

  # --- what the server thinks ----------------------------------------------------------
  # One query for all of it: the counters, the age of the last success and of the last
  # failure, the last segment the archiver archived, the segment the WAL is in now, and
  # the data directory (where the archiver's queue lives).
  row="$(docker exec "$container" psql -U "$user" -d "$database" -tAX -F '|' -c \
    "SELECT archived_count, failed_count, \
            COALESCE(EXTRACT(epoch FROM (now() - last_archived_time))::bigint, -1), \
            COALESCE(EXTRACT(epoch FROM (now() - last_failed_time))::bigint, -1), \
            last_archived_wal, pg_walfile_name(pg_current_wal_lsn()), \
            current_setting('data_directory') \
     FROM pg_stat_archiver" 2>/dev/null || true)"
  if [[ -z "$row" ]]; then
    echo "WARNING: could not read pg_stat_archiver - the archive looks right on disk, but" >&2
    echo "         the server's own view of it is unavailable." >&2
    return 1
  fi
  IFS='|' read -r archived failed last_success last_failure last_wal current_segment data_dir <<<"$row"
  echo "[$(date -Is)] Server's view: ${archived:-?} segments archived, ${failed:-?} failed."

  # --- the archiver's queue -------------------------------------------------------------
  pending_age="$(oldest_pending_segment_age "$container" "$data_dir" || true)"
  pending_age="${pending_age:--1}"

  # --- the verdict ------------------------------------------------------------------------
  if ! wal_archive_health "$newest_age" "$pending_age" "$last_success" "$last_failure" "$max_age"; then
    docker logs "$container" 2>&1 | grep -i 'archive command failed' | tail -3 >&2
    return 1
  fi

  echo "[$(date -Is)] WAL archive on disk: the newest segment is ${newest_age}s old" \
       "(${last_wal:-?}), the WAL is in ${current_segment:-?}."
  if (( pending_age >= 0 )); then
    echo "[$(date -Is)] The archiver is ${pending_age}s behind with one segment."
  elif (( newest_age > max_age )); then
    # The case that used to be reported as a broken archive: a database that has not
    # written since. It is not a failure, and saying so is the point - a quiet
    # database and a broken archiver look the same from the directory alone.
    echo "[$(date -Is)] That age is not a failure: the archiver has nothing waiting, so the" \
         "archive holds every segment that exists. The database has written no WAL since" \
         "- the WAL is still in ${current_segment:-?}."
  fi
  return 0
}

# prune_wal_archive <dir> <max-age-days> <max-bytes>
#
# Keeps the WAL archive inside two bounds, and the second one is the important one.
#
# The age bound is policy: how long the archive stays on the box.
#
# The size bound is a guard, and it exists because of a measurement. An idle database
# produces one 16 MiB segment per archive_timeout, and almost all of it is zeros, so the
# archive grows at a rate set by the TIMEOUT rather than by the traffic. Measured on the
# production box on 2026-10-19: archive_timeout=300 gave 288 segments a day, 4.6 GiB a
# day. A 14-day window would be 63 GiB on a disk with 21 GiB free, and a full disk is
# how the database stops accepting writes - so the age bound alone would have filled the
# disk in four days, and the nightly run that would have noticed was itself the thing
# that could not keep up.
#
# The oldest segment goes first, and the newest never goes: PostgreSQL is writing to it.
prune_wal_archive() {
  local dir="$1" max_age_days="$2" max_bytes="$3"
  [[ -d "$dir" ]] || return 0

  find "$dir" -maxdepth 1 -name '0000000*' -type f -mtime "+${max_age_days}" -print -delete 2>/dev/null || true

  local total oldest newest
  while true; do
    total="$(du -sb "$dir" 2>/dev/null | cut -f1)"
    [[ -z "$total" ]] && break
    (( total <= max_bytes )) && break

    # The oldest and the newest segment, in ONE pass, and WITHOUT a reader that closes
    # the pipe under `sort`.
    #
    # `find | sort | head -1` is the natural way to write this, and it is a trap:
    # `head` exits after its first line, `sort` gets SIGPIPE the moment it writes the
    # rest, and under `set -o pipefail` that non-zero status kills the whole script.
    # That is not a theory: the nightly trim died with "sort: write failed: Broken
    # pipe" on 2026-10-21 and again on 2026-10-22, so the size cap - the bound that
    # stands between the archive and a full disk - was never enforced. The loop below
    # reads the sorted list to the end, so nothing closes the pipe under `sort`.
    local line
    oldest=""
    newest=""
    while IFS= read -r line; do
      [[ -n "$oldest" ]] || oldest="$line"
      newest="$line"
    done < <(find "$dir" -maxdepth 1 -name '0000000*' -type f -printf '%T@ %p\n' 2>/dev/null | sort -n)
    # Drop the timestamp; the path may contain spaces, so only the first field goes.
    oldest="${oldest#* }"
    newest="${newest#* }"
    [[ -z "$oldest" || -z "$newest" ]] && break
    # Never remove the newest segment: it is the one being written to.
    [[ "$oldest" == "$newest" ]] && break
    echo "  removing the oldest segment: $(basename "$oldest")"
    rm -f "$oldest"
  done
}
