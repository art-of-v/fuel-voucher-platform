#!/usr/bin/env bash
# Shared helpers for the off-box WAL archive. Sourced, not executed.
#
# The archive directory on the box is the thing PostgreSQL writes; the remote is the
# only copy that survives the box. The layout is deliberately simple:
#
#   <BACKUP_REMOTE>/wal/<segment>.age     one encrypted WAL segment
#   <BACKUP_REMOTE>/wal/status.txt        what shipped, when, and how late
#   <BACKUP_REMOTE>/fuelflow_base_*.tar.age  the nightly base backups (backup.sh)
#
# Every segment is encrypted on its own with age, to the same public key the nightly
# dump uses. That is more age invocations than one nightly tarball, and it is the whole
# point: a tarball is only as fresh as the night it was made, while a segment is off-box
# a minute after the archiver finishes it.
#
# The server still holds only the public key. Decryption happens where the private key
# lives - a laptop, or a restore host - never here.

# Accept either the bucket root or the wal/ prefix itself, so a restore command copied
# from the bucket listing cannot turn into .../wal/wal.
wal_remote_wal_dir() {
  local remote="${1%/}"
  if [[ "$remote" == */wal ]]; then
    printf '%s\n' "$remote"
  else
    printf '%s/wal\n' "$remote"
  fi
}

wal_object_is_segment() {
  [[ "$1" =~ ^[0-9A-F]{24}$ ]]
}

# The size of one WAL segment. PostgreSQL's default is 16 MiB and the box is built with
# it; a cluster created with --wal-segsize would differ, so it is a variable and not a
# constant in the code.
WAL_SEGMENT_SIZE="${WAL_SEGMENT_SIZE:-16777216}"

# Is this file a COMPLETE WAL segment?
#
# The archive directory is not a place where files appear atomically. PostgreSQL's
# archive_command is
#
#     test ! -f /var/lib/postgresql/wal/%f && cp %p /var/lib/postgresql/wal/%f
#
# and that cp writes the segment into the directory IN PLACE: the name exists from the
# first byte, and the file grows to 16 MiB over the next moments. A shipper that picks
# the file up in that window encrypts a TRUNCATED segment and uploads it as if it were
# whole.
#
# The damage is not visible at upload time. The off-box object decrypts, its name looks
# right, and the shipper marks the segment as shipped - so it is never uploaded again.
# But a restore that reaches that segment stops replaying there, silently, and
# everything after it is gone. A segment that is missing stops the replay too, but a
# missing segment is visible in the count; a half-written one looks complete.
#
# A finished WAL segment is exactly WAL_SEGMENT_SIZE bytes, so the size is the check.
wal_segment_is_complete() {
  local file="$1" size
  [[ -f "$file" ]] || return 1
  size="$(stat -c %s "$file" 2>/dev/null || echo 0)"
  [[ "$size" == "$WAL_SEGMENT_SIZE" ]]
}

wal_segment_size() {
  stat -c %s "$1" 2>/dev/null || echo 0
}

# Is this remote somewhere a DRILL may write?
#
# The drill writes a base backup and WAL segments to the same bucket the production
# shipper uses, so a wrong prefix would put drill objects where the restore reads
# production objects from. The prefix therefore has to carry a "/drill" path segment:
#
#     r2:fuelflow-backups/drill/695-20261023T090000Z   allowed
#     r2:fuelflow-backups                               refused (the production root)
#     r2:fuelflow-backups/wal                           refused (the production prefix)
#
# The check is on the PATH part, not the whole remote, so it works for every rclone
# backend ("r2:", "s3:", "b2:") and for a local directory ("/tmp/drill/remote").
#
# It refuses rather than warns: the alternative is a drill that quietly writes over the
# disaster-recovery copy of a payment database.
wal_drill_remote_is_safe() {
  local remote="${1:-}" path
  [[ -n "$remote" ]] || return 1
  # The part after the backend name: "r2:bucket/drill/x" -> "bucket/drill/x".
  path="${remote#*:}"
  case "$path" in
    */drill/*|*/drill) return 0 ;;
    *) return 1 ;;
  esac
}

# The newest base backup name on the remote, or nothing when there is none. Returns 2
# when the remote cannot be listed at all - the caller must not read that as "empty".
wal_newest_base_object() {
  local remote="$1" listing name
  listing="$("${RCLONE:-rclone}" lsf "$remote" --files-only 2>/dev/null)" || {
    echo "ERROR: could not list $remote with rclone." >&2
    return 2
  }
  # The assignment is deliberately inside `if !`: with `set -e`, a no-match pipeline
  # would otherwise exit the whole restore before the caller can print its error.
  if ! name="$(printf '%s\n' "$listing" \
    | grep -E '^fuelflow_base_[0-9]{4}-[0-9]{2}-[0-9]{2}_[0-9]{6}\.tar\.age$' \
    | sort | tail -1)"; then
    return 1
  fi
  [[ -n "$name" ]] || return 1
  printf '%s\n' "$name"
}

# Fetch the newest base backup to <dest>. Prints the object name it fetched.
wal_fetch_base() {
  local remote="$1" dest="$2" name rc
  name="$(wal_newest_base_object "$remote")"; rc=$?
  if (( rc == 1 )); then
    echo "ERROR: no fuelflow_base_*.tar.age in $remote." >&2
  fi
  (( rc == 0 )) || return "$rc"
  "${RCLONE:-rclone}" copyto "$remote/$name" "$dest" || {
    echo "ERROR: could not download $remote/$name." >&2
    return 1
  }
  printf '%s\n' "$name"
}

# Download every *.age object from <remote_dir>, decrypt each with the age key, and
# write the plaintext segments into <dest_dir>. Prints how many it recovered.
#
# The decryption is per object rather than one tar, so a missing or corrupt segment is
# named instead of failing the whole restore with "tar: unexpected EOF".
wal_fetch_offbox() {
  local remote_dir="$1" dest_dir="$2" key_file="${3:-}" tmp count=0 f base out
  tmp="$(mktemp -d "${TMPDIR:-/tmp}/wal-offbox.XXXXXX")" || return 1
  chmod 700 "$tmp"
  mkdir -p "$dest_dir"
  if ! "${RCLONE:-rclone}" copy "$remote_dir" "$tmp" --include '*.age' >/dev/null 2>&1; then
    echo "ERROR: could not download $remote_dir." >&2
    rm -rf "$tmp"
    return 1
  fi
  for f in "$tmp"/*.age; do
    [[ -e "$f" ]] || continue
    base="$(basename "$f" .age)"
    if ! wal_object_is_segment "$base"; then
      echo "ERROR: unexpected object in $remote_dir: $(basename "$f")" >&2
      rm -rf "$tmp"
      return 1
    fi
    out="$dest_dir/$base"
    if [[ -n "$key_file" ]]; then
      if ! "${AGE:-age}" -d -i "$key_file" "$f" > "$out"; then
        echo "ERROR: could not decrypt $(basename "$f")." >&2
        rm -f "$out"; rm -rf "$tmp"
        return 1
      fi
    else
      if ! "${AGE:-age}" -d "$f" > "$out"; then
        echo "ERROR: could not decrypt $(basename "$f")." >&2
        rm -f "$out"; rm -rf "$tmp"
        return 1
      fi
    fi
    count=$((count + 1))
  done
  rm -rf "$tmp"
  printf '%s\n' "$count"
}

# Encrypt one segment and upload it. The plaintext never leaves this function's
# temporary directory, and the temporary file is removed even when the upload fails.
wal_ship_segment() {
  local segment="$1" remote_dir="$2" recipient="$3" tmp_dir="$4" name enc
  name="$(basename "$segment")"
  if ! wal_object_is_segment "$name"; then
    echo "ERROR: not a WAL segment name: $name" >&2
    return 1
  fi
  mkdir -p "$tmp_dir"
  enc="$tmp_dir/$name.age"
  if ! ( umask 077; "${AGE:-age}" -r "$recipient" -o "$enc" "$segment" ); then
    echo "ERROR: could not encrypt $name." >&2
    rm -f "$enc"
    return 1
  fi
  if ! "${RCLONE:-rclone}" copyto "$enc" "$remote_dir/$name.age"; then
    echo "ERROR: could not upload $name.age." >&2
    rm -f "$enc"
    return 1
  fi
  rm -f "$enc"
}

# The sizes of the objects in a remote directory, one "<name> <size>" pair per line.
#
# `rclone lsf --format ps` prints the path and the size, but it separates the fields
# with "; " (and rclone's separator is configurable), so the line is split on anything
# that could be a separator. The two fields are also told apart by their shape: a size
# is digits, a name is not.
#
# The listing is best effort. If the rclone on the box is too old for --format, this
# returns nothing, and the caller must then treat the sizes as UNKNOWN - never as
# "the object is missing" or "the object is short".
wal_remote_object_sizes() {
  local remote_dir="$1"
  "${RCLONE:-rclone}" lsf "$remote_dir" --files-only --format "ps" 2>/dev/null \
    | awk '{
        n = split($0, a, /[;,\t ]+/)
        if (n < 2) next
        name = a[1]; size = a[2]
        if (name ~ /^[0-9]+$/ && size !~ /^[0-9]+$/) { t = name; name = size; size = t }
        if (size !~ /^[0-9]+$/) next
        print name, size
      }'
}

