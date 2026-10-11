# Rotating a secret

Written after rotating ten of them in one session on 2026-10-11, during which the
production API was taken down twice. Both outages were avoidable with the checks
below, and both traps below cost real time rather than being hypothetical.

## Before you start

- **Know which services consume the secret.** Rotating `.env` changes nothing until
  those containers are recreated. The table below is the list; `Telegram__BotToken`,
  for example, is read by the backend *and* by Grafana, and missing Grafana means
  alerts fail silently.
- **Never put a value in a chat, a ticket, or a command line.** `read -rs` keeps it
  out of shell history and off the screen.
- **One secret at a time.** Verify `/health` after each.

## Writing a new value

```bash
read -rs -p "New JWT_SECRET: " TOK; echo
awk -v t="$TOK" '/^JWT_SECRET=/{print "JWT_SECRET=" t; next}{print}' \
  /root/FuelFlow/deploy/.env > /tmp/e && mv /tmp/e /root/FuelFlow/deploy/.env
unset TOK
chmod 600 /root/FuelFlow/deploy/.env
```

`awk -v` rather than `sed` so a value containing `/` cannot break the expression,
and `read -rs` rather than `nano` so nothing lands in history or in an editor
buffer that can be recovered. Check the length without printing the value:

```bash
grep '^JWT_SECRET=' /root/FuelFlow/deploy/.env | cut -d= -f2 | wc -c
```

## Restarting safely

```bash
cd /root/FuelFlow/deploy && ./restart.sh
```

**Do not run `docker compose up -d` by hand.** Without `*_IMAGE_TAG` exported it
falls back to the mutable `:main` tag, which on this box held an image older than
the one running - that is how the backend crash-looped on a startup guard the
current code no longer contains. `restart.sh` pins the tags off the running
containers, runs `./preflight.sh` first (env files, Caddy config, upstream
ambiguity), and waits for `/health`.

## What consumes what

| Secret | Where it is | Recreate | Blast radius |
|---|---|---|---|
| `JWT_SECRET` | `.env` | `dotnet-backend` | **everyone is logged out** |
| `POSTGRES_PASSWORD` | `.env` + inside Postgres | `dotnet-backend` | app cannot reach the DB until both change |
| `REDIS_PASSWORD` | `.env` + inside Redis (`CONFIG SET requirepass`) | `dotnet-backend` | caches and sessions cleared |
| `SMSCLUB_TOKEN` | `.env` | `dotnet-backend` | SMS to customers stops |
| `SUPPORT_MAIL_PASSWORD` | `.env` | `dotnet-backend` | customer mail stops |
| `TELEGRAM_BOT_TOKEN` | `.env` | `dotnet-backend` **and** `grafana` | alerts go nowhere |
| `GRAFANA_ADMIN_PASSWORD` | `.env` | `grafana` | dashboards only |
| `MONOBANK_SANDBOX_TOKEN` | `.env` **and** `.env.staging` | both stacks | QA payments stop |
| `MONOBANK_TOKEN` | `.env` | `dotnet-backend` | **no real payments can be taken** |
| `STAGING_BASICAUTH_HASH` | `.env` | `caddy` | staging unreachable |

`notify-backup-failure.sh` and `notify-prune-failure.sh` read `.env` at every run,
so a Telegram rotation needs no restart for them.

## Traps, each of which was hit

**Postgres has `trust` on loopback.** `pg_hba.conf` grants `trust` to `127.0.0.1`,
so testing a new password with `psql -h 127.0.0.1` proves nothing - *any* password
succeeds. The app connects to `postgres:5432` over the docker network, which matches
rule 128 (`scram-sha-256`). Test the path the app actually takes:

```bash
docker run --rm --network fuelflow_default -e PGPASSWORD="$NEW" postgres:16-alpine \
  psql -h postgres -U fuelflow -d fuelflow -t -A -c "SELECT 1"
```

**Doubling the `$` in a bcrypt hash is not the trap it looks like.** Compose
collapses `$$` to `$` and passes a lone `$` through unchanged. Measured on the
box: `.env` held 63 characters with **six** `$`, caddy received 60 with
**three** - and because a bcrypt hash is `$2a$<cost>$<53 chars>`, a value with
*four* dollars still collapses into a well-formed hash and **still works**.
Doubling the first one only was never the silent 401-everything failure this
file used to claim it was, and an earlier preflight rejected exactly that
working configuration.

The real trap is a value that is **not a hash at all**: caddy then reads it as
base64-encoded plaintext and refuses to start, taking prod's only public
ingress down with it. That is the state the box was in - a random 63-character
string, and a staging site that had never had a working credential.

Doubling every `$` stays the rule (see the Caddyfile); it just is not the
hazard. Verify the hash against the plaintext *before* recreating Caddy:

```bash
printf '%s' "$HASH_AS_CONTAINER_SEES_IT" > /tmp/h
docker run --rm -v /tmp/h:/tmp/h:ro httpd:2-alpine sh -c \
  'printf "user:%s\n" "$(cat /tmp/h)" > /tmp/ht; htpasswd -vb /tmp/ht user "$P"'
```

**A `STAGING_BASIC_AUTH_HASH` that is not a hash takes caddy down.** Caddy reads a
value it cannot recognise as base64-encoded plaintext and refuses to start:

```
base64-decoding password: illegal base64 data at input byte 76
```

That is prod's only public ingress. `./restart.sh` runs `caddy validate` first so
this can never reach a container.

**Rotating a Monobank token does not move the public key.** `api.monobank.ua` issues
sandbox tokens for the same company as the live cabinet, and both return the same
key, so after rotating a token re-fetch it and compare before assuming anything:

```bash
curl -s -H "X-Token: $NEW" https://api.monobank.ua/api/merchant/pubkey
```

A *new* token also does not invalidate the old one. Ask the merchant to revoke it
explicitly - this is the step that gets forgotten.

**A test package must not be priced at 1 kopeck.** Monobank has a minimum payment
amount and will reject a 0.01 invoice, which reads as a bug in our code. Use 1.00 UAH.

## Afterwards

```bash
find /root/FuelFlow/deploy -maxdepth 1 -name '.env.bak.*' -exec shred -u {} +
```

Every backup taken during the rotation still holds the value you just replaced -
including a live Monobank token, which stays valid until its owner revokes it.
`shred`, not `rm`: the point is that it cannot be recovered from the filesystem.

## What we cannot rotate

Secrets belonging to a merchant we do not control. The live Monobank token belongs
to a third party's account and can only be issued in their cabinet - see
`planning#221`. Everything in this document assumes you can reach the provider.