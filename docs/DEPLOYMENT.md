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
| `SMSCLUB_TOKEN` / `SMSCLUB_SENDER_NAME` | SMS Club (primary UA SMS provider). The app validates credentials on boot but not the sender name — a typo surfaces as "codes never arrive", not a startup error |
| `TWILIO_ACCOUNT_SID` / `TWILIO_AUTH_TOKEN` / `TWILIO_PHONE_NUMBER` | SMS fallback |
| `MONOBANK_TOKEN` / `MONOBANK_PUBLIC_KEY` | LIVE payments. The API refuses to start if the key is missing/placeholder |
| `MONOBANK_REDIRECT_URL` | Where Monobank sends the customer after paying — the deep link `fuelflow://payment-result` reopens the mobile app |
| `SUPPORT_MAIL_*` | palne.shop/support contact form — see below |
| `AUTH_TEST_PHONES` | App Review / QA test phone — see below |
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

### App Review / QA test phone (`AUTH_TEST_PHONES`)

The app has no username/password login — users sign in with a phone number and a 6-digit
code. Apple's reviewers must be able to sign in without receiving an SMS, so any phone
listed in `AUTH_TEST_PHONES` gets a fixed code and no SMS is ever sent:

```
AUTH_TEST_PHONES=+380991234567=427135     # phone=fixed-6-digit-code, comma-separated for more
```

Phone = international format with a leading `+` and no spaces (it must match the number
exactly as stored after normalization). Code = exactly six digits. It never expires —
treat it as a permanent password for that account: pick a random one, use a dedicated
number rather than a real user's, and rotate it if it leaks.

Hand both values to Apple in App Review Information → Sign-In Information (username =
phone number, password = code) and add a note that no SMS will arrive — the reviewer just
types the code. The backend logs `TEST PHONE: OTP issued for allowlisted test number` on
every such login, so review logins are visible in the backend logs.

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

The database seeds the `Admin` *role*, but no admin *user*. Log in through the app or the
admin dashboard once with your real phone number to create your user record, then promote it:

```bash
docker exec -it fuelflow-postgres psql -U fuelflow -d fuelflow -c \
  "UPDATE users SET role_id = 'a0000000-0000-0000-0000-000000000001' WHERE phone_number = '+380671234567' AND is_deleted = false;"
```

`UPDATE 1` = done (log out and back in so the new token carries the role). `UPDATE 0` =
phone didn't match — check with
`SELECT id, phone_number, role_id FROM users ORDER BY created_at_utc DESC LIMIT 5;`

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
TOC, refuses dumps smaller than 1 KB, and prunes by retention. The nightly cron is
installed and active:

```
20 3 * * * cd /root/FuelFlow/deploy && ./backup.sh >> /var/log/fuelflow-backup.log 2>&1
```

To restore: `./restore.sh /root/fuelflow-backups/fuelflow_YYYY-MM-DD_HHMMSS.dump.age`
(it stops the API first and asks for confirmation). **Practice a restore once while
nothing is at stake** — an untested backup is a hope, not a backup. Repeat quarterly.

Two gaps still open:

- **Off-server copy is NOT configured.** The dumps sit on the same server as the
  database, so they protect against "I deleted the wrong rows" but not "the server died".
  `backup.sh` supports `BACKUP_REMOTE` in `.env` (rclone remote, e.g. Hetzner Object
  Storage) — set it and dumps are copied off-box automatically.
- A second disaster-recovery layer (Hetzner volume snapshots or a second off-box copy)
  is worth enabling; snapshots can be taken from the Hetzner console.

**Alert when backups stop happening** — a silent cron failure is worse than no backup.
Either set up mail on the server, or check `/root/fuelflow-backups/` freshness during
the weekly ops pass:

```bash
find /root/fuelflow-backups -name 'fuelflow_*.dump.age' -mmin -1440 | grep -q . && echo OK || echo "NO BACKUP IN LAST 24h"
```

---

## Secret rotation

Never edit `.env` values without redeploying the affected service afterwards — env vars
are read at container start.

| Secret | Where it lives | Rotation impact |
|---|---|---|
| `POSTGRES_PASSWORD` | `deploy/.env` | Update `.env`, then `ff up -d dotnet-backend postgres`; users stay logged in |
| `JWT_SECRET` | `deploy/.env` | Invalidates all access tokens; refresh tokens survive → users re-auth silently |
| `REDIS_PASSWORD` | `deploy/.env` | Cache flush only; sessions/caches rebuild. **Regenerate with hex** (see the variables table) |
| SMS Club / Twilio / Monobank keys | provider dashboards + `.env` | Rotate in the dashboard first, then `.env`, then `ff up -d dotnet-backend` |

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
Backups: nightly age-encrypted pg_dump → /root/fuelflow-backups (cron 03:20); restore = deploy/restore.sh
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
| Login codes never arrive | SMS Club / Twilio credentials, balance, or sender name | Check the provider console; remember credential validation happens at boot, not at send time |
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
