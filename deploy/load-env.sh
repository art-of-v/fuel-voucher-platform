#!/usr/bin/env bash
# ------------------------------------------------------------------------------
# Safe .env loader, shared by backup.sh / restore.sh / notify-backup-failure.sh.
#
# WHY THIS EXISTS
# These scripts used to load secrets with `set -a; source .env`. `source` EXECUTES
# the file as shell, so a value that is fine for Docker Compose becomes a landmine:
#
#   SUPPORT_MAIL_PASSWORD=abcd efgh ijkl mnop   # a Gmail app password, shown in
#                                               # space-separated groups
#
# Sourcing that runs `SUPPORT_MAIL_PASSWORD=abcd` as an assignment and then tries to
# execute `efgh` as a command. Under `set -e` (backup.sh) it aborts the whole run with
# exit 127 — which is exactly how the nightly backup failed silently for weeks. A value
# containing `$(...)` or backticks is worse: sourcing would EXECUTE it. Docker Compose is
# unaffected because it parses .env literally; these scripts must do the same.
#
# load_env parses KEY=VALUE line by line and assigns the FULL literal value. It never
# executes the file, so no secret can break a backup or inject a command again.
#
# It intentionally mirrors Compose's reading: it strips one layer of matching surrounding
# quotes, ignores blank lines and `#` comments, and skips anything that is not a valid
# shell variable assignment. It does NOT support `export KEY=...` prefixes, line
# continuations, or interpolation — deploy/.env has none of those, and keeping the parser
# dumb is the point.
# ------------------------------------------------------------------------------

load_env() {
  local file="$1" line key val
  if [[ ! -f "$file" ]]; then
    echo "ERROR: $file not found. Copy .env.production.example to .env first." >&2
    return 1
  fi

  # `|| [[ -n "$line" ]]` so a final line with no trailing newline is still read.
  while IFS= read -r line || [[ -n "$line" ]]; do
    line="${line%$'\r'}"                              # tolerate CRLF endings
    [[ "$line" =~ ^[[:space:]]*(#.*)?$ ]] && continue # blank line or comment
    [[ "$line" != *=* ]] && continue                  # not an assignment

    key="${line%%=*}"
    val="${line#*=}"
    key="${key#"${key%%[![:space:]]*}"}"              # ltrim key
    key="${key%"${key##*[![:space:]]}"}"              # rtrim key

    # Only accept real shell variable names; ignore anything malformed rather than
    # risk assigning something surprising.
    [[ "$key" =~ ^[A-Za-z_][A-Za-z0-9_]*$ ]] || continue

    # Strip ONE layer of matching surrounding quotes, like Compose does.
    if [[ ${#val} -ge 2 && "$val" == \"*\" ]]; then
      val="${val:1:${#val}-2}"
    elif [[ ${#val} -ge 2 && "$val" == \'*\' ]]; then
      val="${val:1:${#val}-2}"
    fi

    export "$key=$val"
  done < "$file"
}
