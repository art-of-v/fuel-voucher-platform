#!/usr/bin/env bash
# Local harness: exercise every combination of image-job results.
set -uo pipefail
cd "$(dirname "$0")"

pass=0; fail=0
for b in success failure; do
  for a in success failure; do
    for w in success failure; do
      out=$(mktemp)
      ( export SHA=deadbeef BACKEND_RESULT="$b" ADMIN_RESULT="$a" WEBSITE_RESULT="$w" GITHUB_OUTPUT="$out"
        ./resolve-image-tags.sh >/dev/null 2>&1 )
      code=$?
      tags=$(tr '\n' ' ' < "$out")
      rm -f "$out"

      nfail=0
      [ "$b" = success ] || nfail=$((nfail+1))
      [ "$a" = success ] || nfail=$((nfail+1))
      [ "$w" = success ] || nfail=$((nfail+1))

      # only a TOTAL failure (nothing built at all) is a failed release;
      # a partial failure must still deploy the components that did build
      if [ "$nfail" -eq 3 ]; then want_code=1; else want_code=0; fi

      # every non-success component must resolve to `main`, every success to the SHA
      want=""
      [ "$b" = success ] && want+="backend_tag=deadbeef " || want+="backend_tag=main "
      [ "$a" = success ] && want+="admin_tag=deadbeef "   || want+="admin_tag=main "
      [ "$w" = success ] && want+="website_tag=deadbeef " || want+="website_tag=main "
      got=$(echo "$tags" | sed 's/  */ /g')

      if [ "$code" = "$want_code" ] && echo "$got" | grep -qF "$want"; then
        pass=$((pass+1)); printf 'ok   B=%-7s A=%-7s W=%-7s exit=%s  %s\n' "$b" "$a" "$w" "$code" "$got"
      else
        fail=$((fail+1)); printf 'FAIL B=%-7s A=%-7s W=%-7s exit=%s (want %s)  got: %s\n' "$b" "$a" "$w" "$code" "$want_code" "$got"
      fi
    done
  done
done
echo "passed=$pass failed=$fail"
[ "$fail" -eq 0 ]