# FuelFlow — Deployment & Operations (Hetzner)

This is the single deploy/runbook document. It describes **what actually runs today**.

Production is **one Hetzner Cloud server** running the whole stack under Docker Compose.
Deploys are **automatic**: every green push to `main` is deployed by CI. There is no
staging environment, no image registry, and no other hosting.

---

## Where things run

**Server:** Hetzner Cloud CX22 — 4 GB RAM / 2 vCPU / 40 GB disk, Ubuntu 24.04 (LTS),
2 GB swap enabled. The code lives at `/root/FuelFlow`; secrets in `/root/FuelFlow/deploy/.env`
(`chmod 600`, git-ignored, never leaves the server).

**Stack** (`deploy/docker-compose.prod.yml`, six containers on one private Docker network):

| Container | What it does | Public? |
|---|---|---|
| `caddy` | HTTPS certificates (Let's Encrypt) + reverse proxy, routes by hostname | Yes — ports 80/443 |
| `dotnet-backend` | The .NET 10 API. Hangfire background jobs run inside it — there is no separate worker | No (only via Caddy) |
| `admin-frontend` | React admin dashboard on nginx; also proxies `/api` to the backend (same-origin) | No (only via Caddy) |
| `website-frontend` | palne.shop marketing site (Next.js static build) on nginx | No (only via Caddy) |
| `postgres` | Database, data in the named volume `postgres_data`; bound to `127.0.0.1` only | No |
| `redis` | Cache + device-signature nonces; bound to `127.0.0.1` only | No |

**Domains** (all via DNS A records on the same server IP, set in `deploy/.env`):

| Variable | Domain | Serves |
|---|---|---|
| `API_DOMAIN` | `api.palne.shop` | The API the mobile app, admin, and Monobank call |
| `ROOT_DOMAIN` | `app.palne.shop` | The admin dashboard |
| `MARKETING_DOMAIN` | `palne.shop` | The static marketing site; its support form POSTs cross-origin to the API (the compose file wires CORS/AllowedHosts from this variable) |

**Memory limits** are set per container in the compose file (dotnet-backend 896M,
postgres 512M, redis 192M, caddy 96M, admin 64M, website 32M — total 1792M, leaving
~2.2 GB on the 4 GB server). Without them a single memory spike (a large voucher PDF
import) hands the choice of what to kill to the kernel OOM killer — and the kernel picks
the largest RSS, usually Postgres. With them, the container that misbehaves is the
container that dies, and `restart: unless-stopped` brings it straight back. If a
legitimate import starts getting killed, raise `dotnet-backend` first (confirm with
`docker inspect fuelflow-backend --format '{{.State.OOMKilled}}'`).

---

## How deploys work (automatic)

`.github/workflows/ci.yml` runs on every push and PR: secrets scan (gitleaks), backend
test matrix + build, dependency audits, admin build, mobile typecheck + gates, website
build. After every **green merge to `main`**, the `deploy` job runs:

- on a **self-hosted GitHub Actions runner installed on the server itself** (it connects
  outbound to GitHub, so no inbound SSH from cloud runners needs to be allowed);
- `cd /root/FuelFlow && git pull --ff-only origin main`;
- `cd deploy && docker compose --env-file .env -f docker-compose.prod.yml build && up -d`
  — images are **built on the server**;
- smoke tests: `https://api.palne.shop/health`, `https://palne.shop/`, `https://palne.shop/support/`.

Properties of a deploy worth knowing:

- **Database migrations auto-apply when the API boots** (`RunMigrationsOnBoot=true`).
- Expect **~30–60 seconds of downtime** while the new containers start.
- A failed CI job blocks the deploy; `main` being red means production did not change.
- To roll back: `git checkout <previous-good-sha>` in `/root/FuelFlow`, then rerun the
  compose build + `up -d` (the same steps the deploy job runs). If a bad **migration**
  shipped too, restore a dump from before the deploy (see [Backups](#backups)).
  Rule of thumb: code-only rollback = checkout + rebuild; schema damage = restore + checkout.

---

## `deploy/.env` variables

Copy `deploy/.env.production.example` to `.env` on the server and fill every value —
the stack refuses to boot half-configured (`:?` guards in the compose file). Generation:

```bash
openssl rand -base64 36   # -> POSTGRES_PASSWORD
openssl rand -hex 32      # -> REDIS_PASSWORD  (hex on purpose — see below)
openssl rand -base64 36   # -> JWT_SECRET
```

| Variable | Notes |
|---|---|
| `ROOT_DOMAIN` / `API_DOMAIN` / `MARKETING_DOMAIN` | Bare hostnames, no `https://`, no trailing slash. DNS must point at the server before first boot (Caddy cannot get certificates otherwise) |
| `ACME_EMAIL` | Let's Encrypt expiry warnings |
| `POSTGRES_DB` / `POSTGRES_USER` / `POSTGRES_PASSWORD` | Self-hosted Postgres on the server |
| `REDIS_PASSWORD` | **Must be hex, not base64.** A `/` in the password breaks the app's Redis connection parsing (unguarded `new Uri()`) and the API crash-loops. `openssl rand -hex 32` avoids it |
| `JWT_SECRET` | At least 32 characters or the API refuses to start |
| `SMSCLUB_TOKEN` / `SMSCLUB_SENDER_NAME` | SMS Club (the UA SMS provider). The app validates credentials on boot but not the sender name — a typo surfaces as "codes never arrive", not a startup error |
| `MONOBANK_TOKEN` / `MONOBANK_PUBLIC_KEY` | LIVE payments. The API refuses to start if the key is missing/placeholder |
| `MONOBANK_REDIRECT_URL` | Where Monobank sends the customer after paying — the deep link `fuelflow://payment-result` reopens the mobile app |
| `SUPPORT_MAIL_*` | palne.shop/support contact form — see below |
| `VOUCHER_EXPIRATION_ENABLED` | Default `true` (correct for production); set `false` only on a disposable/staging box |

Two names you may remember from the old Render setup — **`QR_ENCRYPTION_KEY` and
`SESSION_SECRET` — are deliberately absent**: no code references them (verified), and the
`.env` template does not include them. Also note the backend's Postgres connection string
in the compose file **must keep `SSL Mode=Disable`** — without it the app auto-appends
`SSL Mode=Require`, which the local Postgres container does not offer, and boot fails.

### Support-form email (palne.shop/support)

Messages land in the `support_messages` table **before** any email is attempted, so they
survive SMTP outages. For Gmail delivery: enable 2FA on the account, create an **App
Password** (Google Account → Security → 2-Step Verification → App passwords) and put those
16 characters into `SUPPORT_MAIL_PASSWORD` — the real account password is always rejected.
Leaving username/password empty disables email delivery; submissions are still stored.

---

## Everyday operations

All of these run on the server. To save typing, set up a shortcut:

```bash
echo "alias ff='docker compose --env-file /root/FuelFlow/deploy/.env -f /root/FuelFlow/deploy/docker-compose.prod.yml'" >> ~/.bashrc
source ~/.bashrc
```

Now:

```bash
ff ps                          # what's running
ff logs -f dotnet-backend      # follow API logs
ff logs --tail=200 caddy       # certificate / routing problems
ff restart dotnet-backend      # restart just the API
ff down                        # stop everything (data volumes survive)
ff up -d                       # start everything again
```

There is no manual deploy step — merging to `main` deploys (see
[How deploys work](#how-deploys-work-automatic)). Only run `ff up -d --build` by hand for
a rollback or when CI cannot (e.g. you need to redeploy the current tree after a server
restart).

### Support form messages

To review submissions (including ones whose email failed):

```bash
docker exec -it fuelflow-postgres psql -U fuelflow -d fuelflow -c \
  "SELECT created_at_utc, email, left(message, 60) AS message, email_sent_at_utc, send_error
   FROM support_messages ORDER BY created_at_utc DESC LIMIT 20;"
```

`send_error` non-null with `email_sent_at_utc` null means the row is stored but the email
never went out (SMTP credentials wrong, Gmail blocking, etc.) — fix the cause and re-send
manually, or read the message right here.

### Make yourself an admin

The database seeds two *roles* — `Admin` (the dashboard) and `User` (mobile customers) — but
no admin *user*. Any phone that signs in through the app is created with the `User` role; to
gain dashboard access, promote your record to `Admin`:

```bash
docker exec -it fuelflow-postgres psql -U fuelflow -d fuelflow -c \
  "UPDATE users SET role_id = '0b6c503a-2086-4fe3-b617-b47385b474bd' WHERE phone_number = '+380671234567' AND is_deleted = false;"
```

`UPDATE 1` = done (log out and back in so the new token carries the role). `UPDATE 0` =
phone didn't match — check with
`SELECT id, phone_number, role_id FROM users ORDER BY created_at_utc DESC LIMIT 5;`

### Admin sign-in by email (avoid SMS cost)

Admins log in with the same phone + OTP flow as everyone else, but when the account
has `Role = "Admin"` **and** an email on file, the code is delivered by **email over the
`SUPPORT_MAIL_*` SMTP account instead of a paid SMS**. Customers and any admin without an
email still get an SMS, and if the email send fails the request falls back to SMS — so an
admin can never be locked out and an SMS provider stays mandatory.

Give your admin user an email:

```bash
docker exec -it fuelflow-postgres psql -U fuelflow -d fuelflow -c \
  "UPDATE users SET email = 'you@example.com' WHERE phone_number = '+380671234567' AND is_deleted = false;"
```

Requirements: `SUPPORT_MAIL_HOST/USERNAME/PASSWORD` set in `deploy/.env` (Gmail needs a
16-char App Password). To force SMS for everyone, set `Auth__AdminOtpViaEmail: "false"` on
`dotnet-backend` in `docker-compose.prod.yml`. Note: the code is sent to the address already
stored for that phone — a caller cannot redirect it.

### Admin dashboard access control

The dashboard is **admin-only, enforced in two layers**:

- **Server (authoritative):** every `/api/admin/*` endpoint requires `Roles = "Admin"`, and the
  global fallback policy rejects anonymous. A non-admin token gets 401/403 regardless of the UI,
  so no dashboard data is reachable without the role.
- **Admin SPA:** after the OTP succeeds it reads `/api/auth/user/me`; if the returned role is not
  `Admin` it clears the session and shows *"This account is not authorized to use the admin
  dashboard."* instead of the data views.

So a `User`-role phone can complete the code step but is bounced with that message; only `Admin`
accounts proceed, and they receive the code by email (see above).

### Check the database / server health

```bash
docker exec -it fuelflow-postgres psql -U fuelflow -d fuelflow
# then: \dt to list tables, \q to quit

free -h          # RAM and swap
df -h /          # disk space
docker stats     # per-container CPU/RAM (Ctrl+C to exit)
```

**Free up disk space** (old Docker images pile up after a few deploys):

```bash
docker system prune -af
```

That's safe — it removes unused images and build cache, never your named volumes.

---

## Backups

`deploy/backup.sh` produces **age-encrypted** `pg_dump` archives in
`/root/fuelflow-backups/` (the server holds only the public key), validates the archive
TOC, refuses dumps smaller than 1 KB, and prunes by retention.

The schedule is a **systemd timer** kept in version control under `deploy/systemd/`, not
a hand-typed crontab line. Install it once per box (idempotent — safe to re-run):

```bash
sudo /root/FuelFlow/deploy/install-backup-schedule.sh
```

That copies the units into `/etc/systemd/system/`, reloads systemd, and enables
`fuelflow-backup.timer` (fires nightly at **03:20** server-local, `Persistent=true` so a
run missed while the box was off happens at next boot). Confirm it armed:

```bash
systemctl list-timers fuelflow-backup.timer
```

To restore: `./restore.sh /root/fuelflow-backups/fuelflow_YYYY-MM-DD_HHMMSS.dump.age`
(it stops the API first and asks for confirmation). **Practice a restore once while
nothing is at stake** — an untested backup is a hope, not a backup. Repeat quarterly.

**Alert when backups stop happening.** A silent failure is worse than no backup, so the
service has `OnFailure=fuelflow-backup-alert@.service`: any failed run
(`notify-backup-failure.sh`) pushes a Telegram message to the same bot the observability
stack uses (`TELEGRAM_BOT_TOKEN`/`TELEGRAM_CHAT_ID` in `.env`) with the last log lines,
and always records the failure in the journal even when Telegram is unset. To spot-check
freshness by hand during the weekly ops pass:

```bash
find /root/fuelflow-backups -name 'fuelflow_*.dump.age' -mmin -1440 | grep -q . && echo OK || echo "NO BACKUP IN LAST 24h"
```

### Off-site backups (Cloudflare R2)

The dumps above sit on the same server as the database, so on their own they protect against
"I deleted the wrong rows" but not "the server died". `backup.sh` copies each encrypted dump to
an rclone remote when `BACKUP_REMOTE` is set — the off-site half of the backup. The remote is
**Cloudflare R2** (S3-compatible, no egress fees, EU jurisdiction available).

What leaves the box is only ever the **age ciphertext** — encryption happens before the upload,
so R2 (and anyone with the R2 token) never sees plaintext. The R2 token is therefore not a
key to the data; the age private key, which lives off the server, is.

One-time setup:

1. In the Cloudflare dashboard: **R2 → Create bucket** (e.g. `fuelflow-backups`). If you pick a
   **jurisdiction** (e.g. EU, for data residency) the bucket's S3 endpoint gains that segment —
   `https://<accountid>.eu.r2.cloudflarestorage.com` — and the plain
   `https://<accountid>.r2.cloudflarestorage.com` will reject it with `AccessDenied` (403). Use
   whichever endpoint the bucket's jurisdiction dictates in step 3.
2. **R2 → Manage API Tokens → Create Account API token** — an **Object Read & Write** token
   scoped to that one bucket ("Apply to specific buckets only"). Note the Access Key ID, Secret
   Access Key, and the S3 endpoint. An *Account* token (not a User token) keeps working even if
   your Cloudflare user later leaves the org.
3. On the server, install rclone and register the remote. `read -rs` takes the two credentials
   at a silent prompt, so the literal secret never lands in your shell history (only the
   variable names do); `unset` wipes them from the session afterwards:

```bash
apt-get update && apt-get install -y rclone
read -rsp 'R2 Access Key ID: '     R2_KEY;    echo
read -rsp 'R2 Secret Access Key: ' R2_SECRET; echo
rclone config create r2 s3 provider=Cloudflare \
  access_key_id="$R2_KEY" \
  secret_access_key="$R2_SECRET" \
  region=auto \
  endpoint=https://<accountid>.r2.cloudflarestorage.com \
  acl=private \
  no_check_bucket=true
unset R2_KEY R2_SECRET
```

`no_check_bucket=true` is not optional here: a bucket-scoped **Object Read & Write** token
cannot *create* buckets, so without it rclone's default "check/create the bucket" step before
the first upload fails with `AccessDenied` (403) even though writing objects is allowed. And use
the `.eu.` (or other jurisdiction) endpoint from step 1 if the bucket has one — the credentials
and scope can be perfect and a mismatched endpoint still returns 403.

4. Point the backup at it in `deploy/.env` (rclone stores the credentials in its own config,
   so only the remote name goes here):

```bash
BACKUP_REMOTE=r2:fuelflow-backups
```

5. Prove it end-to-end — run one backup now and confirm the object landed in R2:

```bash
systemctl start fuelflow-backup.service && journalctl -fu fuelflow-backup.service
rclone ls r2:fuelflow-backups
```

Retention is split on purpose: **14 days on-box** (`RETENTION_DAYS`), **30 days off-site**
(`BACKUP_REMOTE_RETENTION_DAYS`) since R2 is the disaster-recovery copy and storage for a
handful of small encrypted dumps is negligible. `backup.sh` prunes the remote itself after each
copy, so the bucket does not grow without bound. A second DR layer (Hetzner volume snapshots
from the console) is worth enabling alongside it.

---

## Staging (same box)

Staging is a second, always-on copy of the app on the **same server**, in its own compose
project `fuelflow-staging` (`deploy/docker-compose.staging.yml`): its own Postgres, Redis, JWT
secret and `staging.*` domains, fully isolated from prod. It pulls the **same GHCR images** as
prod (pinned by `IMAGE_TAG`), so what runs on staging is byte-for-byte what prod is about to
run. Once wired into CI, every merge deploys staging and smoke-tests it **before** prod.

Isolation and safety notes:
- **Separate everything**: distinct DB/Redis/JWT credentials in `deploy/.env.staging` (never
  reuse prod's), so a staging leak cannot touch prod and staging tokens are invalid on prod.
- **`ASPNETCORE_ENVIRONMENT=Staging`**, not Production. The prod-only startup guards in
  `Program.cs` (`ValidateSecurityConfiguration`) do **not** run, so staging can boot with
  **payments disabled** (`MONOBANK_ENABLED=false`) and **no SMS provider** (OTP codes go to
  the container log — `docker logs fuelflow-staging-backend`). This is only safe because the
  prod Caddy fronts every staging domain with **HTTP Basic auth** (added in the Caddy-wiring
  step). `Auth__DevBypass` stays **off** even so.
- **Memory**: staging is sized smaller than prod (backend 512M, pg 256M, redis 96M, admin 48M,
  website 32M ≈ 944M). With prod's ~1792M that fits the 4 GB box + 2 GB swap with headroom.

### Bring it up (one-time)

1. Create the shared Docker network the prod Caddy will later use to reach staging:
   ```bash
   docker network create fuelflow_shared 2>/dev/null || true
   ```
2. Copy the env template and fill it in (all `CHANGE_ME` values):
   ```bash
   cd /root/FuelFlow/deploy && cp .env.staging.example .env.staging && nano .env.staging
   ```
3. Make sure the box can pull the images, then start the stack:
   ```bash
   docker login ghcr.io          # once, with a GitHub token that has read:packages
   docker compose --env-file .env.staging -f docker-compose.staging.yml up -d
   ```
4. Verify over an SSH tunnel **before** it is public (staging publishes only on localhost):
   ```bash
   curl -fsS http://127.0.0.1:18080/health      # staging backend
   curl -fsS http://127.0.0.1:15000/ -o /dev/null   # staging admin
   ```

### Going public (Caddy + Basic auth) and the CI gate

The prod Caddy also terminates TLS for the three `staging.*` domains and reverse-proxies them
to the staging containers over `fuelflow_shared`. Every staging domain sits behind **HTTP Basic
auth**, which is what makes the relaxed-guard staging safe to expose.

**Do all of this on the box BEFORE the Caddy/gate change is deployed.** Once it's live, the
prod deploy runs `caddy validate` and a `deploy-staging` gate that both fail while staging is
unconfigured — by design that also holds prod deploys back (they fail safe; the running prod is
never touched, but nothing new ships until staging is set up).

1. **DNS** — add three A records pointing at this server's IP (same box as prod):
   `staging-app.palne.shop`, `staging-api.palne.shop`, `staging.palne.shop`. Caddy issues certs
   on first request, so these must resolve first.
2. **Basic-auth credential** — generate a bcrypt hash and pick a username:
   ```bash
   docker run --rm caddy:2-alpine caddy hash-password --plaintext 'a-strong-password'
   ```
3. **Add to `deploy/.env`** (the PROD env file — the prod Caddy reads these):
   ```bash
   STAGING_ROOT_DOMAIN=staging-app.palne.shop
   STAGING_API_DOMAIN=staging-api.palne.shop
   STAGING_MARKETING_DOMAIN=staging.palne.shop
   STAGING_BASICAUTH_USER=staging
   # DOUBLE every $ in the hash to $$ or docker compose eats it and login always fails:
   STAGING_BASICAUTH_HASH=$$2a$$14$$....................................................
   ```
   These have no `:?` guard: left unset the staging site blocks resolve to empty addresses and
   `caddy validate` fails the deploy loudly, instead of restarting Caddy with a broken config.

Once DNS + `.env.staging` + the shared network + these `deploy/.env` values are all in place,
the pipeline runs itself: **merge → build image → deploy staging → smoke-test staging (localhost)
→ `caddy validate` → deploy prod**. A staging failure stops the change before prod. To ship a
prod hotfix while staging is intentionally down, deploy prod by hand:
`cd /root/FuelFlow/deploy && IMAGE_TAG=$(git rev-parse HEAD) docker compose --env-file .env -f docker-compose.prod.yml up -d`.

---

## Secret rotation

Never edit `.env` values without redeploying the affected service afterwards — env vars
are read at container start.

| Secret | Where it lives | Rotation impact |
|---|---|---|
| `POSTGRES_PASSWORD` | `deploy/.env` | Update `.env`, then `ff up -d dotnet-backend postgres`; users stay logged in |
| `JWT_SECRET` | `deploy/.env` | Invalidates all access tokens; refresh tokens survive → users re-auth silently |
| `REDIS_PASSWORD` | `deploy/.env` | Cache flush only; sessions/caches rebuild. **Regenerate with hex** (see the variables table) |
| SMS Club / Monobank keys | provider dashboards + `.env` | Rotate in the dashboard first, then `.env`, then `ff up -d dotnet-backend` |

---

## Device signatures on checkout — do not disable casually

The backend requires a cryptographic signature on `/api/purchases` and `/api/purchases/bulk`,
and the current mobile app already sends one (`mobile/src/core/api/apiClient.ts` signs both
endpoints). Signatures stay **ON**, which is what you want on the endpoint that takes money.

One real failure mode: the app silently skips signing if the device has no keypair yet.
If checkout returns 401 on a freshly installed app, it's a key-generation problem in the
app, not a server misconfiguration.

**Emergency switch.** If signing breaks mid-pilot and you need customers buying again
while you debug, add this to the backend `environment:` block in
`deploy/docker-compose.prod.yml` and redeploy:

```yaml
DeviceAuth__Enabled: "false"
```

Treat it as a hotfix measured in hours, not days — it disables anti-tamper protection on
your payment endpoints.

What does **not** work, in case you find it suggested somewhere: overriding
`DeviceAuth__RequireSignatureForEndpoints__0` / `__1`. Those entries are defined as C#
list defaults, and .NET's configuration binder *appends* to a list rather than replacing
it — so the two real endpoints stay enforced no matter what you set. `DeviceAuth__Enabled`
is the only switch that actually works.

---

## Logging: what exists, where it lives

Three layers already exist; learn them — you do not need ELK at this scale:

| Layer | Written by | Lives in | Retention |
|---|---|---|---|
| HTTP access log | `RequestLoggingMiddleware` (every request: method/path/status/duration/IP) | container stdout → `docker logs` | 10 MB × 3 files per container (compose log caps) |
| App errors | `DatabaseLoggerProvider`: Error/Critical persisted asynchronously | Postgres table `error_logs` → admin UI → Error Logs tab | until cleaned |
| Domain/admin audit | handlers writing `audit_log` | Postgres table `audit_log` → admin UI → Audit tab | until cleaned |

Daily-driver commands (production logs are JSON — `Serilog` compact format, so grep on the `@l` level field):

```bash
docker logs fuelflow-backend --tail 100 -f                          # follow live traffic
docker logs fuelflow-backend 2>&1 | grep -E '"@l":"(Error|Fatal)"'  # recent errors
docker logs fuelflow-caddy --tail 50                                # edge/TLS issues
docker exec -it fuelflow-postgres psql -U fuelflow -d fuelflow \
  -c "SELECT created_at_utc, level, message FROM error_logs ORDER BY created_at_utc DESC LIMIT 20;"
```

Habits: after every deploy, `docker logs fuelflow-backend --since 10m` once; weekly, skim
the admin Error Logs tab. If a container crash-loops, `docker inspect fuelflow-backend
--format '{{.State.ExitCode}} {{.RestartCount}}'` plus full logs is the whole story —
startup exceptions print an unhandled stack trace (exit 139 = segfault; always read the
exception above it).

For metrics, dashboards and alerting, see [Logs and monitoring](#logs-and-monitoring-prometheus--grafana--loki) below.

---

## Logs and monitoring (Prometheus + Grafana + Loki)

External uptime is covered by **UptimeRobot** (5-minute checks of `https://api.palne.shop/health`
with alerts to the owner). For everything UptimeRobot cannot see — memory pressure, fulfillment
stalls, the voucher pool running dry, error bursts — the repo ships a metrics/logs/alerting stack
that runs **on the same server** with segmented Docker networks:

- `deploy/docker-compose.observability.yml` — Prometheus (scrapes `dotnet-backend:8080/metrics`),
  Loki (7-day log retention), an authenticated push-only gateway, and Grafana.
- `deploy/docker-compose.observability.telegram.yml` — optional overlay that delivers the
  provisioned alert rules to a Telegram group: `ServiceDown`, `HighErrorRate`, `HighRequestLatency`,
  `HighCpuUsage`, `HighMemoryUsage`, `LogErrorBurst`, `LogFatal`, `VoucherPoolLow`,
  `FulfillmentFailures`, `HangfireJobFailures`. Firing **and** resolved notifications arrive in the
  same group, so "app is down" and "app is back up" are one rule each.

Nothing in the stack is exposed to the internet: Grafana binds `127.0.0.1:3000` (SSH tunnel only),
and the other services publish no ports. Loki is reachable only on the private `observability`
network by Grafana and the gateway. The backend sends structured logs to `loki-gateway:8080`
with the dedicated `LOKI_PUSH_USERNAME` / `LOKI_PUSH_PASSWORD` credential; the gateway accepts
only authenticated `POST /loki/api/v1/push` and rejects query endpoints. Application containers
therefore cannot bypass Grafana to read retained logs. There is deliberately no Alloy container,
because its Docker-socket mount is root-equivalent on the host.

Generate the ingestion password with `openssl rand -base64 32`. Rotate it by updating
`LOKI_PUSH_PASSWORD` in `deploy/.env`, then recreate `dotnet-backend` and `loki-gateway` together.

**Start it:**

```bash
cd ~/FuelFlow/deploy
# one-time: add GRAFANA_ADMIN_PASSWORD (and TELEGRAM_* for alerts) to .env — see
# deploy/.env.production.example for the generation instructions
docker compose --env-file .env -f docker-compose.observability.yml up -d
# with Telegram alerts:
docker compose --env-file .env \
  -f docker-compose.observability.yml \
  -f docker-compose.observability.telegram.yml up -d
```

**Look at it:**

```bash
ssh -L 3000:127.0.0.1:3000 root@palne.shop
# then open http://localhost:3000  (admin / GRAFANA_ADMIN_PASSWORD)
```

Dashboards live in the **FuelFlow** folder: *Service Health* (availability, request rate, 5xx
ratio, latency), *Business* (voucher pool, orders, Monobank webhooks, job failures), *Logs*
(searchable log stream). The API emits `/metrics` unconditionally; Caddy denies `/metrics` and
`/hangfire` at the edge, so the scrape endpoint is reachable only inside the Docker network.

**Memory:** the stack is capped at ~1.2 GB worst case (prometheus 512M, loki 384M, grafana 256M,
gateway 32M) against ~2.5 GB of headroom. If the server ever feels tight, `docker stats` shows who eats what;
Loki retention is 7 days and Prometheus 15, both sized for the 40 GB disk.

Dashboard edits in the Grafana UI are transient — change dashboards by committing the JSON under
`backend/observability/grafana/provisioning/dashboards/`. The full details of the metrics layer
(naming rules, labels, in-app `IAlertNotifier`) are in [docs/OBSERVABILITY.md](OBSERVABILITY.md).

### Sentry (error tracking)

The Grafana/Loki stack answers "is the system healthy" (metrics, log search). Sentry answers a
different question — "what exactly threw, with the stack trace and the request that caused it" —
and groups recurring exceptions. It is **optional and off by default**: with no DSN the SDK is
never initialised and nothing is sent off-box. The backend, the admin SPA and the mobile app are
**three separate Sentry projects** (server .NET, browser JavaScript, React Native), each with its
own DSN.

#### Backend

To turn it on:

1. Create a project at [sentry.io](https://sentry.io) (the free Developer tier — 5k errors/month,
   one user — is plenty for current traffic). Pick **.NET / ASP.NET Core** as the platform.
2. Copy the project's **DSN** and put it in `deploy/.env` as `SENTRY_DSN=...`.
3. Restart just the backend:

```bash
cd /root/FuelFlow/deploy && docker compose -f docker-compose.prod.yml up -d dotnet-backend
```

What it sends is deliberately narrow: unhandled exceptions and their stack traces, tagged with the
`Observability:Environment` label. Request bodies and PII are **hard-disabled** in code
(`SendDefaultPii = false`, `MaxRequestBodySize = None`) because the API carries phone numbers and
voucher QR payloads — those must never reach a third party. Performance tracing is sampled at
`Observability:Sentry:TracesSampleRatio` (default `0.0` = errors only, no trace volume).

#### Admin SPA

The admin is a static bundle (nginx serving files), so it has **no runtime env** — its DSN is
baked in at *build* time by Vite. The key difference from the backend: a change takes effect on
the **next deploy/rebuild, not a restart**.

1. Create a *second* project at [sentry.io](https://sentry.io) — platform **React**.
2. Copy its DSN into `deploy/.env` as `ADMIN_SENTRY_DSN=...`.
3. Rebuild the admin image so the value is inlined (a plain restart will not pick it up):

```bash
cd /root/FuelFlow/deploy && docker compose -f docker-compose.prod.yml up -d --build admin-frontend
```

The admin sends errors only — no performance tracing and no session replay, and `sendDefaultPii`
is hard-disabled in code (`admin/src/lib/sentry.ts`) for the same reason as the backend (the
panel renders phone numbers and voucher data). A render crash shows a minimal reload screen
instead of a white page. The admin DSN is a write-only browser ingest key and is expected to be
visible in client-side JavaScript; it is not an account secret.

#### Mobile app

The mobile app links a **native** Sentry module, so its DSN is compiled into the app binary at
**EAS build time** — there is no server to restart and no OTA update that can flip it. A change
takes effect only on the **next native build submitted to the stores**.

1. Create a *third* project at [sentry.io](https://sentry.io) — platform **React Native**.
2. Put its DSN into `mobile/eas.json` under each build profile's `env` as
   `EXPO_PUBLIC_SENTRY_DSN` (this is what CI/EAS reads), or, for a local `expo run:` build, into
   `mobile/app.json` at `expo.extra.sentryDsn`. `mobile/src/core/observability/sentry.ts` reads the
   env var first and falls back to the app-config value.
3. Build and submit a new native binary (`eas build --profile production`); a JS-only OTA update
   cannot enable it.

Like the other two surfaces it sends **errors only** — no performance tracing and no session
replay, and `sendDefaultPii` is hard-disabled in code, because the app carries phone numbers and
voucher QR codes. React render crashes are caught by the app's error boundary and forwarded
explicitly (the boundary otherwise swallows them). The mobile DSN is a write-only ingest key that
ships inside every installed app — it is public by nature, not an account secret.

> **Verifiability:** unlike the backend and admin, the mobile wiring **cannot** be proven by CI.
> The JS builds and is unit-tested, but whether a crash actually reaches Sentry can only be
> confirmed from an EAS build running on a device or simulator — Expo Go cannot load the native
> module at all.

---

## The one-page mental model

```
Internet ──► :80/:443 CADDY (auto-TLS, routes by hostname)
                │ app.palne.shop ──► admin-frontend (nginx SPA)
                │                      └─ /api/* proxied ─┐
                └ api.palne.shop ──► dotnet-backend:8080 ◄┘ (same origin = no CORS pain)
                └ palne.shop ─────► website-frontend (static Next.js)
                                        ├─ postgres  (volume postgres_data, 127.0.0.1:5432)
                                        ├─ redis     (cache + nonces, 127.0.0.1:6379)
                                        └─ Hangfire jobs run in-process in dotnet-backend
Money path: POST /api/purchases → Monobank invoice → webhook (ECDSA-verified, fail-closed)
            → order PendingFulfillment → FulfillmentService assigns vouchers FEFO per minute
Config truth: deploy/.env (secrets) + appsettings.Production.json (behavior) — env beats file
Data truth: EF migrations on boot (RunMigrationsOnBoot=true); schema history in __EFMigrationsHistory
Deploys: merge to main → CI green → self-hosted runner builds & restarts the stack → smoke tests
Monitoring: UptimeRobot (external, 5-min /health) + Prometheus/Grafana/Loki on the server (SSH tunnel)
Backups: nightly age-encrypted pg_dump → /root/fuelflow-backups (systemd timer 03:20, OnFailure→Telegram); restore = deploy/restore.sh
```

**Where do I look when…**

| Symptom | First look |
|---|---|
| Site down | `ff ps` → `ff logs --tail=200 caddy` → backend logs |
| 502 from Caddy | backend crashed/hung: `ff logs --tail=200 dotnet-backend` |
| Checkout fails | backend logs around the request id; Monobank webhook lines; device-signature 401s |
| Slow | `docker stats`; import = known pdfium RAM hog |
| Disk filling | `docker system df`; `du -sh /var/lib/docker/volumes/postgres_data`; `docker image prune -f` |
| "Did my deploy land?" | `git log -1` in `/root/FuelFlow` vs GitHub; check the deploy job in Actions |

---

## Troubleshooting

| Symptom | Likely cause | Fix |
|---|---|---|
| Browser shows a certificate warning | DNS wasn't pointing at the server when Caddy tried to get a certificate | Confirm DNS resolves to the server IP, then `ff restart caddy` and watch `ff logs caddy` |
| `curl https://api.palne.shop/health` times out | Firewall or Caddy not running | Check ports 80/443 are allowed (Hetzner Cloud Firewall / host firewall), `ff ps` |
| Backend container keeps restarting | A missing or invalid setting — the app refuses to boot misconfigured | `ff logs dotnet-backend`; the exception names the setting. Usual suspects: `JWT_SECRET` under 32 chars, empty SMS values, placeholder Monobank key |
| Backend logs an SSL/connection error to Postgres | The connection string lost `SSL Mode=Disable` | It must stay in `Database__ConnectionString` in the compose file |
| Admin dashboard loads but every API call 404s or fails | Compose service names were renamed | The admin's nginx proxies to the name `dotnet-backend` — keep the service key exactly that |
| Login codes never arrive | SMS Club credentials, balance, or sender name | Check the provider console; remember credential validation happens at boot, not at send time |
| Payment succeeds, no voucher appears | Monobank webhook URL wrong in the Monobank merchant dashboard | Fix the URL to `https://api.palne.shop/api/monobank/webhook`, then use the app's reconciliation feature to settle missed callbacks |
| Checkout returns 401 | The device has no signing keypair, so the app sent an unsigned request | Reinstall the app fresh on a real device and retry. To unblock customers while you debug, set `DeviceAuth__Enabled: "false"` (see above) |
| Backend crash-loops right after you change the Redis password | A `/` in the password | Regenerate with `openssl rand -hex 32`, update `.env`, `ff up -d` |
| Build killed partway through | Out of memory | Confirm swap is on (`free -h`); if it persists, temporarily stop non-essential containers while building, or resize the server |
| PDF voucher import fails or the API dies during import | Memory | Check `docker inspect fuelflow-backend --format '{{.State.OOMKilled}}'`; raise the `dotnet-backend` mem_limit |
| Everything is slow, `df -h` near 100% | Old Docker images | `docker system prune -af` |

**The one command to run before asking for help:**

```bash
ff ps && ff logs --tail=100 dotnet-backend
```

Nearly every failure in this stack explains itself in those hundred lines.

---

## Server hardening checklist

Current state (2026-09): `unattended-upgrades` is active; the Hetzner firewall governs
exposed ports; the following are **not yet done**:

- [ ] `fail2ban` for SSH brute-force protection (`apt-get install -y fail2ban && systemctl enable --now fail2ban`)
- [ ] SSH lockdown — `PasswordAuthentication no`, `PermitRootLogin prohibit-password`
      (keep an existing SSH session open while confirming key login still works)
- [ ] Verify the attack surface: `ss -tlnp` — public listeners must be only sshd + docker (Caddy);
      everything else binds `127.0.0.1`
- [ ] `BACKUP_REMOTE` configured so dumps leave the server (see [Backups](#backups))
- [ ] One documented restore drill
