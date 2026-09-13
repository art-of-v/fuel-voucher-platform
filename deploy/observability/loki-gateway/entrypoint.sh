#!/bin/sh
set -eu

: "${LOKI_PUSH_USERNAME:?set LOKI_PUSH_USERNAME}"
: "${LOKI_PUSH_PASSWORD:?set LOKI_PUSH_PASSWORD}"

BASIC_TOKEN="$(printf '%s:%s' "$LOKI_PUSH_USERNAME" "$LOKI_PUSH_PASSWORD" | base64 | tr -d '\n')"
sed "s|__LOKI_BASIC_TOKEN__|$BASIC_TOKEN|g" \
    /opt/loki-gateway/nginx.conf.template > /tmp/nginx.conf
chmod 600 /tmp/nginx.conf

exec nginx -c /tmp/nginx.conf -g 'daemon off;'
