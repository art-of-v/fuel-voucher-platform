# FuelFlow on DigitalOcean — Operations Runbook

Companion to `docs/DEPLOY_DIGITALOCEAN.md`. That document gets the stack running.
This one makes it **secure, observable, self-alerting, and boring to operate** — which is
what "production" actually means.

Conventions: `[laptop]` = your machine, `[server]` = on the Droplet over SSH.
All server steps assume Ubuntu 24.04 and that you completed steps 1–14 of the deploy guide.

---

## Part A — Is `deploy/` on the right track?

Yes. The architecture decisions are correct, and several subtle .NET-on-Docker traps are
already handled in comments. Audit result:

| Area | Status | Notes |
|---|---|---|
| Edge: single exposed service (Caddy), auto-HTTPS | ✅ | Ports 80/443 only; certs persist in `caddy_data` |
| Postgres/Redis bound to `127.0.0.1` | ✅ | Never public; reachable only on the box |
| `SSL Mode=Disable` for local PG | ✅ | Otherwise `DatabaseSetup.BuildConnectionString` appends `Require` and boot fails |
| Redis password in StackExchange format, hex not base64 | ✅ | Avoids the `new Uri()` parse crash on `/` |
| Fail-fast `.env` (`:?set X`) | ✅ | Stack refuses half-configured boots |
| JWT ≥32 chars guard, Twilio/Monobank startup guards | ✅ | Matches `Program.cs` production guards |
| Device signatures left ON | ✅ | Correct; `# DeviceAuth__Enabled: "false"` documented as emergency-only |
| pg healthchecks + `service_healthy` ordering | ✅ | Backend waits for PG/Redis readiness |
| backup.sh size-check + retention | ✅ | Refuses to keep <1 KB dumps |
| restore.sh stops API first, confirms | ✅ | |
| **Container log rotation** | ❌ missing | Default json-file driver grows forever → disk-full outage |
| **Healthcheck for backend/admin** | ❌ missing | Restart-on-crash works, restart-on-hang doesn't |
| **Off-site backups** | ❌ missing | Script's own comment admits dumps die with the droplet |
| **Monitoring / notifications** | ❌ missing | Nobody tells you when it's down |
| **Intrusion hardening beyond UFW** | ⚠️ partial | Deploy guide sets UFW; no fail2ban / auto security updates |
| mobile/nginx.conf proxies to `admin-backend:4000` | ⚠️ stale | Legacy web-export leftover; **not used** by prod compose. Ignore or fix someday |
| `QR_ENCRYPTION_KEY` / `SESSION_SECRET` (Render had them) | ✅ verified unused | No code references — correctly absent from `.env` template |

Parts B–H below close every ❌ in order. Do them once, top to bottom.

---

## Part B — Close the two compose gaps (10 min)

### B1. Log rotation for every container

Without this, `/var/lib/docker` slowly eats the disk and takes Postgres down with it.
Edit `deploy/docker-compose.prod.yml` — add a top-level default logging block right under
`name: fuelflow`:

```yaml
name: fuelflow

# Every service inherits this unless it overrides it. Caps each container's logs
# at 3 files x 10 MB = 30 MB, so a chatty service can never fill the disk.
x-default-logging: &default-logging
  driver: json-file
  options:
    max-size: "10m"
    max-file: "3"

services:
```

Then add `logging: *default-logging` to each of the five services (caddy,
dotnet-backend, admin-frontend, postgres, redis), e.g.:

```yaml
  caddy:
    image: caddy:2-alpine
    logging: *default-logging
    ...
```

### B2. Healthcheck so a hung backend restarts

`restart: unless-stopped` only fires on process exit. If Kestrel deadlocks (port open,
requests never answered), nothing recovers it. Add to `dotnet-backend`:

```yaml
    healthcheck:
      # In-container probe; no curl needed (wget ships in the runtime image).
      test: ["CMD-SHELL", "wget -qO- http://127.0.0.1:8080/health >/dev/null || exit 1"]
      interval: 30s
      timeout: 5s
      retries: 3
      start_period: 90s   # migrations can legitimately take a while on first boot
```

Apply both changes locally, commit, then on the server:

```bash
# [server]
cd ~/FuelFlow && git pull
cd deploy && docker compose --env-file .env -f docker-compose.prod.yml up -d --build
docker ps --format 'table {{.Names}}\t{{.Status}}'   # expect "(healthy)" on backend within ~2 min
```

---

## Part C — Security hardening (30 min)

The deploy guide already did: SSH keys, swap, UFW (22/80/443), Docker. Finish the job:

### C1. Automatic security patches

```bash
# [server]
sudo apt-get install -y unattended-upgrades
sudo dpkg-reconfigure -plow unattended-upgrades   # choose "Yes"
```

### C2. Brute-force protection (SSH)

```bash
# [server]
sudo apt-get install -y fail2ban
sudo systemctl enable --now fail2ban
sudo fail2ban-client status sshd   # watch bans accumulate over time
```

### C3. SSH lockdown — only after confirming key login works

```bash
# [server]
sudo sed -i 's/^#\?PasswordAuthentication.*/PasswordAuthentication no/' /etc/ssh/sshd_config
sudo sed -i 's/^#\?PermitRootLogin.*/PermitRootLogin prohibit-password/' /etc/ssh/sshd_config
sudo systemctl reload ssh
# KEEP THIS SSH SESSION OPEN. Open a second terminal and confirm you can still log in
# before you close the first one.
```

### C4. Verify the attack surface

```bash
# [server]
sudo ss -tlnp   # public listeners must be ONLY sshd + docker (caddy). Everything else binds 127.0.0.1
# [laptop]
nmap -p- YOUR_DROPLET_IP   # expect exactly 22, 80, 443 open
```

### C5. Secret rotation procedure (write this down before you need it)

| Secret | Where it lives | Rotation impact |
|---|---|---|
| `POSTGRES_PASSWORD` | `deploy/.env` | Update `.env`, then `docker compose up -d dotnet-backend postgres`; users stay logged in |
| `JWT_SECRET` | `deploy/.env` | Invalidates all access tokens; refresh tokens survive → users re-auth silently |
| `REDIS_PASSWORD` | `deploy/.env` | Cache flush only; sessions/caches rebuild |
| Twilio / Monobank keys | provider dashboards + `.env` | Rotate in dashboard first, then `.env`, then `up -d dotnet-backend` |

Never edit `.env` values without redeploying the affected service afterwards — env vars are
read at container start.

---

## Part D — Backups you can actually restore from (45 min)

The nightly cron (deploy guide §10) writes dumps to the same disk as the database. One
ransomware event, one DO fire, one fat-fingered `rm` and both are gone. Fix it:

### D1. Copy dumps off the droplet (DO Spaces, S3-compatible)

```bash
# [server]
sudo apt-get install -y rclone
rclone config          # create remote "spaces": DO Spaces, key/secret from DO console
rclone lsd spaces:     # verify
```

Append to `deploy/backup.sh`, just before the final line:

```bash
# Off-box copy — the dump is useless if it dies with the droplet.
if command -v rclone >/dev/null && rclone listremotes | grep -q .; then
  rclone copy "$OUT" "spaces:fuelflow-backups" --transfers 1
  echo "[$(date -Is)] Copied off-box."
fi
```

(Creation of the Spaces bucket: DO console → Spaces → create `fuelflow-backups`, region
closest to the droplet, restrict file listing.)

### D2. Alert when backups stop happening

A silent cron failure is worse than no backup. Add a second cron entry that complains if
no fresh dump exists:

```bash
# [server] crontab -e
20 3 * * * cd /root/FuelFlow/deploy && ./backup.sh >> /var/log/fuelflow-backup.log 2>&1
0 9 * * * find /root/fuelflow-backups -name 'fuelflow_*.dump' -mmin -1440 | grep -q . || echo "FuelFlow: NO BACKUP IN LAST 24h" | mail -s "BACKUP FAILED" you@example.com
```

(If you skip mail setup, Part E's Telegram alert replaces this.)

### D3. Do one real restore drill — now, not during an incident

```bash
# [server]
cd ~/FuelFlow/deploy
./restore.sh "$(ls -t /root/fuelflow-backups/fuelflow_*.dump | head -1)"
curl -fsS https://$API_DOMAIN/health     # {"status":"healthy",...}
curl -fsS https://$API_DOMAIN/api/stations | head -c 200   # data actually came back
```

An untested backup is a hope, not a backup. Repeat quarterly.

---

## Part E — Notifications (30 min)

You want a push message to your phone when: the site goes down, disk >85%, RAM pressure,
or backups stop. Cheapest reliable setup: **UptimeRobot (external) + Telegram (from server)**.

### E1. Create a Telegram alert channel

1. Telegram → talk to `@BotFather` → `/newbot` → save the token.
2. Send any message to your new bot, then get your chat id:
   `curl -s https://api.telegram.org/bot<TOKEN>/getUpdates | grep -o '"id":[0-9]*'`
3. On the server, make a tiny notifier:

```bash
# [server] /usr/local/bin/fuelflow-alert  && chmod +x
#!/usr/bin/env bash
curl -fsS "https://api.telegram.org/bot${TG_TOKEN}/sendMessage" \
     -d chat_id="${TG_CHAT_ID}" -d text="FuelFlow: $1" >/dev/null
```

Store `TG_TOKEN` / `TG_CHAT_ID` in `/etc/environment`, then `source /etc/environment`.

### E2. External uptime checks (catches network/DNS/cert failures too)

[UptimeRobot](https://uptimerobot.com) free tier, two monitors:

- HTTPS `https://APP_DOMAIN/health` — every 5 min → alert via Telegram/email
- HTTPS `https://API_DOMAIN/health` — every 5 min → alert via Telegram/email

`/health` returns 200 only when the DB answers (`PipelineSetup.cs`), so this single URL
covers app + database reachability.

### E3. DigitalOcean resource alerts

DO console → Monitor → Alerts:

- **Disk usage > 85%** → email (disk-full is the #1 killer of small droplets)
- **CPU > 90% for 10 min**, **Memory > 90% for 10 min** → email
- Enable **weekly droplet backups** ($2.40/mo) as the disaster-recovery layer beneath pg dumps

### E4. Cert-expiry safety net

Caddy renews automatically; you'll only notice failure via the ACME email you set in
`ACME_EMAIL`. Optionally add a monthly check:

```bash
# [server] crontab -e
0 8 1 * * expiry=$(date -d "$(echo | openssl s_client -servername $API_DOMAIN -connect $API_DOMAIN:443 2>/dev/null | openssl x509 -noout -enddate | cut -d= -f2)" +%s); if [ $((expiry - $(date +%s))) -lt 1209600 ]; then /usr/local/bin/fuelflow-alert "TLS cert expires in <$(( (expiry - $(date +%s))/86400 )) days"; fi
```

---

## Part F — Logging: what exists, where it lives, how to read it (understand, don't install)

You do NOT need ELK/Loki at this scale. Three layers already exist; learn them:

| Layer | Written by | Lives in | Retention |
|---|---|---|---|
| HTTP access log | `RequestLoggingMiddleware` (every request: method/path/status/duration/IP) | container stdout → `docker logs fuelflow-backend` | 30 MB/container (Part B1) |
| App errors | `DatabaseLoggerProvider`: Error/Critical persisted asynchronously | Postgres table `error_logs` → admin UI → Error Logs tab | until cleaned |
| Domain/admin audit | handlers writing `audit_log` | Postgres table `audit_log` → admin UI → Audit tab | until cleaned |

Daily-driver commands:

```bash
# [server]
docker logs fuelflow-backend --tail 100 -f              # follow live traffic
docker logs fuelflow-backend 2>&1 | grep -E 'ERR|FTL'   # recent errors
docker logs fuelflow-caddy --tail 50                    # edge/TLS issues
docker exec -it fuelflow-postgres psql -U fuelflow -d fuelflow \
  -c "SELECT created_at_utc, level, message FROM error_logs ORDER BY created_at_utc DESC LIMIT 20;"
```

Log-driven habits: after every deploy, `docker logs fuelflow-backend --since 10m` once;
weekly, skim the admin Error Logs tab. If a container ever crash-loops, `docker inspect
fuelflow-backend --format '{{.State.ExitCode}} {{.RestartCount}}'` plus full logs is the
whole story — startup exceptions print an unhandled stack trace (on this runtime they show
as exit 139; always read the exception above it).

---

## Part G — Updates, rollback, CI/CD

### G1. Manual update (the safe ritual)

```bash
# [server]
cd ~/FuelFlow
./deploy/backup.sh                       # 1. checkpoint first, always
git pull                                 # 2. get code
cd deploy && docker compose --env-file .env -f docker-compose.prod.yml up -d --build
                                         # 3. rebuild changed images; migrations apply on boot
docker ps --format 'table {{.Names}}\t{{.Status}}'
sleep 60 && docker logs fuelflow-backend --since 2m   # 4. watch one boot cycle
curl -fsS https://$API_DOMAIN/health                  # 5. green or roll back
```

### G2. Rollback

```bash
# [server]
cd ~/FuelFlow && git log --oneline -5 && git checkout <previous-good-sha>
cd deploy && docker compose --env-file .env -f docker-compose.prod.yml up -d --build
# If a bad MIGRATION shipped too: ./restore.sh <dump-from-before-the-deploy>
```

Rule of thumb: code-only rollback = checkout + rebuild; schema damage = restore + checkout.

### G3. CI/CD (when manual gets tedious)

GitHub Actions: on push to `main` → run backend tests + admin/mobile typecheck (jobs exist
in `.github/workflows/ci.yml`) → then SSH-deploy G1 steps via `appleboy/ssh-action`.
Repository secrets needed: `SSH_HOST`, `SSH_KEY`, plus nothing else — the server already
holds `.env`. Keep deploys gated on tests passing; keep the manual ritual documented for
when automation lies to you.

---

## Part H — Transparency: the one-page mental model

```
Internet ──► :80/:443 CADDY (auto-TLS, routes by hostname)
                │ app.domain.com ──► admin-frontend:5000 (nginx SPA)
                │                      └─ /api/* proxied ─┐
                └ api.domain.com ──► dotnet-backend:8080 ◄┘ (same origin = no CORS pain)
                                        ├─ postgres  (volume postgres_data, 127.0.0.1:5432)
                                        ├─ redis     (cache, 127.0.0.1:6379)
                                        └─ Hangfire jobs run in-process here
Money path: POST /api/purchases → Monobank invoice → webhook (ECDSA-verified, fail-closed)
            → order PendingFulfillment → FulfillmentService assigns vouchers FEFO per minute
Config truth: deploy/.env (secrets) + appsettings.Production.json (behavior) — env beats file
Data truth: EF migrations on boot (RunMigrationsOnBoot=true); schema history in __EFMigrationsHistory
Backups: nightly pg_dump → /root/fuelflow-backups → rclone → Spaces; restore = deploy/restore.sh
Watchtower: UptimeRobot on both /health + DO disk/CPU/RAM alerts → Telegram/email
```

**Where do I look when…**

| Symptom | First look |
|---|---|
| Site down | UptimeRobot → then `docker ps` → `docker logs fuelflow-caddy` → backend logs |
| 502 from Caddy | backend crashed/hung: `docker logs fuelflow-backend --tail 200` |
| Checkout fails | backend logs around the request id; Monobank webhook lines; device-signature 401s |
| Slow | `docker stats`; DB slow queries: psql `SELECT ... FROM error_logs`; import = known pdfium RAM hog |
| Disk filling | `docker system df`; `du -sh /var/lib/docker/volumes/postgres_data`; prune old images `docker image prune -f` |
| "Did my deploy land?" | `git log -1` on server vs GitHub; `/api/app-version` |

---

## Done means all of this

- [ ] Compose has log caps + backend healthcheck (B)
- [ ] fail2ban active, unattended-upgrades on, PasswordAuthentication no, nmap shows 22/80/443 only (C)
- [ ] Nightly dump → off-box Spaces copy, freshness alarm armed, **one successful restore drill logged** (D)
- [ ] UptimeRobot on both domains + DO disk/memory alerts + Telegram bot tested end-to-end (E)
- [ ] You personally ran: tail logs, query error_logs, one manual deploy, one rollback rehearsal (F,G)
- [ ] This file committed; next person on-call can find everything from it alone (H)
