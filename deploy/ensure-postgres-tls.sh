#!/usr/bin/env bash
# ------------------------------------------------------------------------------
# Creates the certificate the database server presents, and the CA that signs it.
#
#     ensure-postgres-tls.sh [--dir <path>] [--host <dns name>] [--uid <n>]
#
# Exit 0 = the certificate is in place and usable. Exit 1 = it could not be made.
#
# WHY THIS EXISTS
#
# The production connection string said "SSL Mode=Disable;Trust Server Certificate=true", and
# the reason was practical: the local Postgres had no certificate, so the two options were an
# encrypted-but-unauthenticated connection and no encryption at all. Neither is a choice worth
# making for a connection that carries refresh-token hashes, phone numbers, order amounts and
# encrypted voucher payloads - the session is readable to anything that can reach the Docker
# network, and "Trust Server Certificate" means the other side does not even have to prove who
# it is.
#
# The third option was always there: give the server a certificate. This script is that option.
# It creates a small CA, signs a server certificate for the name the client connects by, and
# leaves the CA certificate where the application can verify it. The application then says
# "SSL Mode=Verify Full;Root Certificate=<ca.crt>", and the startup guard refuses to boot a
# production build whose connection string still trusts any certificate.
#
# IDEMPOTENT, ON PURPOSE
#
# This runs on every deploy, so it must not replace a working certificate: the CA is the trust
# anchor, and a new one would invalidate the certificate the application is configured with
# until the next deploy picked it up. So the CA is created once and never touched, and only the
# server certificate is renewed - and only when it is close to expiring.
#
# THE PERMISSIONS ARE NOT COSMETIC
#
# PostgreSQL refuses to start if the private key is readable by group or others, and it wants
# the key owned by the database user (or root). The alpine image's postgres user is uid 70, and
# the key is written 0600 and chowned to it, so the server can read it and nothing else can.
# ------------------------------------------------------------------------------
set -uo pipefail

CERT_DIR="${CERT_DIR:-certs/postgres}"
CERT_HOST="${CERT_HOST:-postgres}"
CERT_UID="${CERT_UID:-70}"
CA_DAYS="${CA_DAYS:-3650}"
CERT_DAYS="${CERT_DAYS:-365}"
RENEW_WITHIN_DAYS="${RENEW_WITHIN_DAYS:-30}"

fail() { echo "ERROR: $*" >&2; exit 1; }

while [[ $# -gt 0 ]]; do
  case "$1" in
    --dir)  CERT_DIR="${2:-}"; shift 2 ;;
    --host) CERT_HOST="${2:-}"; shift 2 ;;
    --uid)  CERT_UID="${2:-}"; shift 2 ;;
    -h|--help) sed -n '2,45p' "$0"; exit 0 ;;
    *) fail "unknown argument: $1" ;;
  esac
done

command -v openssl > /dev/null 2>&1 || fail "openssl is not on this host"

mkdir -p "$CERT_DIR" || fail "cannot create the certificate directory: $CERT_DIR"
CERT_DIR="$(cd "$CERT_DIR" && pwd)"

CA_KEY="$CERT_DIR/ca.key"
CA_CRT="$CERT_DIR/ca.crt"
SRV_KEY="$CERT_DIR/server.key"
SRV_CRT="$CERT_DIR/server.crt"

# --- 1. the CA, created once and then left alone --------------------------------
if [[ ! -f "$CA_CRT" || ! -f "$CA_KEY" ]]; then
  echo "creating the certificate authority in $CERT_DIR"
  openssl req -x509 -newkey rsa:4096 -nodes \
    -keyout "$CA_KEY" -out "$CA_CRT" \
    -days "$CA_DAYS" -sha256 \
    -subj "/CN=FuelFlow database CA" \
    -addext "basicConstraints=critical,CA:TRUE" \
    -addext "keyUsage=critical,keyCertSign,cRLSign" \
    > /dev/null 2>&1 || fail "could not create the certificate authority"
else
  echo "reusing the certificate authority in $CERT_DIR"
fi

# --- 2. the server certificate, renewed only when it is close to expiring --------
renew=0
if [[ ! -f "$SRV_CRT" || ! -f "$SRV_KEY" ]]; then
  renew=1
  echo "the server certificate is missing - creating it"
elif ! openssl x509 -checkend $((RENEW_WITHIN_DAYS * 24 * 3600)) -noout -in "$SRV_CRT" > /dev/null 2>&1; then
  renew=1
  echo "the server certificate expires within $RENEW_WITHIN_DAYS days - renewing it"
else
  echo "reusing the server certificate in $CERT_DIR"
fi

if [[ "$renew" -eq 1 ]]; then
  # The name the client connects by is the SAN, not the CN: a modern client ignores the CN.
  # "localhost" and the loopback address are there so a manual run against the container works.
  openssl req -newkey rsa:2048 -nodes \
    -keyout "$SRV_KEY" -out "$CERT_DIR/server.csr" \
    -subj "/CN=$CERT_HOST" \
    > /dev/null 2>&1 || fail "could not create the server key"

  cat > "$CERT_DIR/server.ext" <<EOF
basicConstraints=CA:FALSE
keyUsage=critical,digitalSignature,keyEncipherment
extendedKeyUsage=serverAuth
subjectAltName=DNS:$CERT_HOST,DNS:localhost,IP:127.0.0.1
EOF

  openssl x509 -req -in "$CERT_DIR/server.csr" \
    -CA "$CA_CRT" -CAkey "$CA_KEY" -CAcreateserial \
    -out "$SRV_CRT" -days "$CERT_DAYS" -sha256 \
    -extfile "$CERT_DIR/server.ext" \
    > /dev/null 2>&1 || fail "could not sign the server certificate"

  rm -f "$CERT_DIR/server.csr" "$CERT_DIR/server.ext"
fi

# --- 3. the permissions the server insists on ------------------------------------
# The key: 0600, owned by the database user. The certificate: world-readable, because a
# certificate is public and the CA certificate is what the client verifies against.
chmod 600 "$CA_KEY" 2>/dev/null || fail "cannot set the permissions of $CA_KEY"
chmod 644 "$CA_CRT" 2>/dev/null || fail "cannot set the permissions of $CA_CRT"
chmod 600 "$SRV_KEY" 2>/dev/null || fail "cannot set the permissions of $SRV_KEY"
chmod 644 "$SRV_CRT" 2>/dev/null || fail "cannot set the permissions of $SRV_CRT"

if [[ "$(id -u)" -eq 0 ]] && [[ -n "$CERT_UID" ]]; then
  chown "$CERT_UID:$CERT_UID" "$SRV_KEY" 2>/dev/null \
    || fail "cannot give the server key to uid $CERT_UID (the database user)"
fi

# --- 4. prove it, rather than assume it -----------------------------------------
openssl verify -CAfile "$CA_CRT" "$SRV_CRT" > /dev/null 2>&1 \
  || fail "the server certificate does not verify against the CA"

echo
echo "certificate authority: $CA_CRT"
openssl x509 -in "$CA_CRT" -noout -subject -fingerprint -sha256 | sed 's/^/  /'
echo "server certificate:   $SRV_CRT (for $CERT_HOST)"
openssl x509 -in "$SRV_CRT" -noout -subject -dates | sed 's/^/  /'
echo
echo "The application connects with: SSL Mode=Verify Full;Root Certificate=$CA_CRT"
exit 0
