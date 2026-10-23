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
