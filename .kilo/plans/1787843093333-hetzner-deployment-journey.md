# FuelFlow — Hetzner Deployment Journey (Teaching Plan)

Interactive, phase-by-phase mentoring session for a backend dev learning DevOps.
One phase at a time. Checkpoint at the end of every phase before proceeding.

## Operating instructions for the executor

- This is a **teaching session**, not a run-to-completion script. Teach with developer analogies; explain WHY before every command and WHAT it did after.
- Deliver exactly ONE phase per turn, then STOP and wait for the learner to say "done"/"next" or paste an error. Never run ahead.
- Every phase ends with the stated Checkpoint — do not proceed until the learner confirms it.
- Before each command, warn about common failure modes: what the error looks like and how to fix it.
- Do not run mutating commands on the learner's machines or the Hetzner server without the learner's explicit go-ahead.
- Source edits (Dockerfile, appsettings, mobile config) are expected parts of phases — the learner requested this hands-on approach.
- If a blocker from the audit (FF-03, FF-05, FF-14) surfaces, pause and explain; do not silently skip.
- The learner's local machine is Windows (Docker Desktop ready); a Mac mini exists for iOS builds only.

## Execution override (2026-08-28) — learner asked to defer teaching to the end

The learner wants the server running ASAP. All teaching (explanations, exercises,
line-by-line diff walks) is moved to a single review walkthrough at the END of the
journey. Remaining phases run in execution mode:

- Executor drives the work; each phase still ends with its checkpoint command, but the
  learner just confirms the result instead of doing exercises.
- Phase 1 implementation is COMPLETE and build-verified: /health now checks Redis
  (single shared IConnectionMultiplexer, AbortOnConnectFail=false, per-dependency
  status), pre-deploy config fixes applied (AllowedHosts/CORS/Monobank deep link +
  mobile URLs, mobile check-api-config gate passes), local compose Redis connection
  string switched to the safe comma form, mobile/.dockerignore excludes .env, and a
  startup-log Redis password redaction bug fixed. The local `docker compose up` smoke
  test was SKIPPED by learner request (prod stack previously proven functional; local
  run not on the critical path).
- `deploy/.env` generated locally with random secrets (hex Redis, base64 Postgres/JWT,
  LF line endings). Hard gate before Phase 5 boot: learner must fill
  TWILIO_ACCOUNT_SID / TWILIO_AUTH_TOKEN / TWILIO_PHONE_NUMBER / MONOBANK_TOKEN /
  MONOBANK_PUBLIC_KEY and set ACME_EMAIL, or ValidateSecurityConfiguration crash-loops
  the API.
- Next: Phase 2 (Hetzner account + server). Blockers to confirm with learner: Hetzner
  account status, DNS control over palne.shop, availability of Twilio/Monobank creds.

## Phase 0 — status: COMPLETE (2026-08-27)

Decisions recorded below (answers, domain, config fixes). Next phase to teach: Phase 1.

## Context (from the codebase audit)

The repo **already contains** most of what the learner's phase plan says "write from scratch":

- `backend/src/FuelFlow.API/Dockerfile` — multi-stage .NET 10, arch-aware RID (arm64/x64), non-root `appuser`, verifies `pdfium.so`, EXPOSE 8080
- `docker-compose.yml` — local stack (admin-database, redis, dotnet-backend, admin-frontend, mobile-app)
- `deploy/docker-compose.prod.yml` — production stack: caddy, dotnet-backend, admin-frontend, postgres, redis; Postgres/Redis bound to `127.0.0.1` only; mem limits; log rotation; `:?` env guards
- `deploy/Caddyfile` — reverse proxy with auto-HTTPS (Let's Encrypt), security headers, admin /api allow-list
- `deploy/.env.production.example` — full secret template with generation instructions
- `/health` endpoint already exists (`PipelineSetup.cs`) — checks DB only, **does not check Redis**
- Serilog already configured (console output; not yet JSON structured)
- `deploy/backup.sh` / `deploy/restore.sh` — age-encrypted pg_dump + restore
- CI: `.github/workflows/ci.yml` (gitleaks, build/test matrix, audits) + `backend/.github/workflows/ci.yml`
- Docs: `docs/DEPLOY_DIGITALOCEAN.md`, `docs/DIGITALOCEAN_OPERATIONS.md`, `docs/SECURITY_AUDIT_2026-08-21.md`

## Key deviations from the learner's assumed plan

1. **Phase 1 is mostly done** — teach by line-by-line review, not rewrite. Real gap: `/health` does not verify Redis.
2. **Reverse proxy is Caddy, not Nginx** — the repo already uses Caddy with automatic TLS. Do NOT introduce nginx+certbot; keep Caddy (simpler, already hardened). Phase 3 is re-scoped to "understand & verify the existing Caddy setup".
3. **No "Hetzner Managed Databases" option exists** for Cloud servers (Phase 4 Option B) — Hetzner Cloud has no managed PostgreSQL/Redis. Option A (Docker on same VPS) is the only cheap path; the "scale-out" path is a second VPS with Postgres on a private Network.
4. Mobile native app (iOS/Android) only needs the API; the `mobile-app` web-export container is optional and can be dropped from the prod stack.
5. Open security blockers from the audit (resolve before real users): **FF-03** unsigned Expo OTA (Critical), **FF-05** Monobank token in git history (High, attestation needed), **FF-14** backup restore never drilled.
6. Mobile app hardcodes the old Render URL (`mobile/.env`, `app.json`, `apiClient.ts`) — must point at the new API domain and rebuild before beta.

## Phase 0 answers (2026-08-27)

- **Hetzner account:** No → Phase 2 starts with account creation (payment method, verification).
- **Domain:** `palne-shop` (exact TLD and registrar uncertain; reportedly used before for an owner's website). **Decision pending** — see "Open decision: domain" below.
- **Local OS:** Windows (primary dev machine). Mac mini with macOS exists for iOS builds (EAS/TestFlight). Phase 1 local `docker compose up` requires Docker Desktop + WSL2 on Windows — confirm installed before Phase 1.
- **GitHub repo:** Private → Phase 5 cannot `git clone` over HTTPS anonymously; use `scp` of `deploy/` + `.env` (simplest) or a read-only deploy key. Phase 6 pushes images to a private ghcr.io package; server pulls with a token.
- **Dockerfile:** Write one **from scratch** as a learning exercise (learner hasn't written one recently), then diff against the production `backend/src/FuelFlow.API/Dockerfile` line by line and explain each difference.

## Domain decision (resolved 2026-08-27)

- **Domain:** `palne.shop` — registrar/DNS control still to be confirmed by learner (needed by Phase 3).
- Subdomains: `api.palne.shop` (API + Monobank webhook), `app.palne.shop` (admin panel) → `ROOT_DOMAIN` / `API_DOMAIN` in `deploy/.env`.
- `fuelflow.com` is **not a real domain** (invented by an AI agent) — must be purged from config.
- **Monobank redirect must be the mobile deep link** `fuelflow://payment-result` (the user returns to the app after payment), NOT a website URL.

### Pre-deploy config fixes (implementation item — apply during Phase 1 app-readiness)

- `backend/src/FuelFlow.API/appsettings.Production.json`:
  - `AllowedHosts`: remove `fuel-voucher-platform.onrender.com;fuel-flow-opal.vercel.app;fuelflow.com` → `api.palne.shop;app.palne.shop`
  - `Monobank:RedirectUrl`: `https://fuelflow.com/payment-result` → `fuelflow://payment-result`
  - `Cors:AllowedOrigins`: `https://fuel-flow-opal.vercel.app` → `https://app.palne.shop`
- `backend/src/FuelFlow.API/appsettings.Development.json`: CORS origin → `http://localhost:5001`
- `mobile/app.json` (`extra.apiUrl`), `mobile/.env`, `mobile/src/core/api/apiClient.ts`, `eas.json`: production API URL → `https://api.palne.shop` (before beta; EAS build profiles must declare `EXPO_PUBLIC_API_URL`)
- `docs/*` stale URL references (cosmetic — do at the end)
- Note: `deploy/docker-compose.prod.yml` already overrides RedirectUrl / CORS / AllowedHosts via env with `:?` guards, so the compose stack works without the file fixes — but the committed files must be correct for direct `dotnet` runs and to stop stale references spreading.

## Phase plan

### Phase 0 — Hetzner Reality Check (this doc)
- Pricing vs AWS/DO/GCP; cheapest viable setup; Cloud vs Dedicated; exact server choice (CAX11 / CX22); dashboard concepts; setup order.

### Phase 1 — App readiness on any Linux server
- Write a fresh Dockerfile **from scratch** in a scratch folder (learning exercise), then diff against the production `backend/src/FuelFlow.API/Dockerfile` and explain every difference (multi-stage, RID arch, non-root user, pdfium verification, EXPOSE 8080).
- Same exercise for a minimal `docker-compose.yml` + `.env.example`, then compare with `deploy/docker-compose.prod.yml` (mem limits, healthchecks, `127.0.0.1` bindings, `:?` guards).
- Add Redis to `/health` (existing endpoint only checks the DB).
- Confirm `.dockerignore`s (backend, admin, mobile) are correct.
- Apply the pre-deploy config fixes listed under "Domain decision" (AllowedHosts, Monobank deep-link redirect, CORS, mobile URL).
- Prereq on Windows: Docker Desktop + WSL2 installed and running.
- Run `docker compose up` locally; verify `localhost:<port>/health` shows DB+Redis healthy.

### Phase 2 — Hetzner account & server
- hcloud CLI, SSH key generation, create server (Ubuntu 24.04 LTS, region FSN1, cheapest tier, attach key at creation).
- Cloud Firewall: 22 (restrict to learner IP), 80, 443 only. Never expose 5432/6379.
- Hardening: non-root user, disable root+password SSH, unattended-upgrades.
- Checkpoint: SSH as non-root works; root login rejected; port scan = 22/80/443 only.

### Phase 3 — Server software
- Install Docker + Compose plugin; add user to docker group.
- **Re-scoped:** verify existing Caddy config rather than installing nginx+certbot (deviation #2). DNS A records for app/api subdomains.
- Checkpoint: `https://api.<domain>/health` serves a valid cert (502 expected until app runs).

### Phase 4 — PostgreSQL + Redis
- Named volume `postgres_data`; strong password via env; bind to 127.0.0.1 (never publish 5432).
- Redis with `requirepass` (already handled in prod compose via config file, not argv).
- SSH tunnel for local psql; run first migration against production DB.
- Checkpoint: local psql via tunnel lists tables; `redis-cli PING` → PONG inside container.

### Phase 5 — First manual deploy
- `scp` compose + `.env` (or git clone + deploy key); build-on-server vs build-in-CI tradeoff (RAM for .NET build).
- `docker compose up -d`; Caddy routing; live logs via `docker compose logs -f`.
- Checkpoint: `https://api.<domain>/health` → 200, DB+Redis healthy.

### Phase 6 — GitHub Actions CI/CD
- ghcr.io for images (public/private), workflow: build → push → SSH → pull → `up -d`.
- Secrets: SSH key, host, ghcr token. Rollback procedure (re-pull previous image tag).
- Checkpoint: push to main → green → live.

### Phase 7 — Logs, monitoring, alerts
- Docker logs commands; Serilog JSON output; UptimeRobot on `/health`; Sentry SDK; optional Grafana+Prometheus; log rotation (already in compose: 10MB×3).
- Checkpoint: UptimeRobot UP; Sentry captures test exception.

### Phase 8 — Backups
- Use existing `backup.sh`/`restore.sh`; cron; off-site copy (Hetzner Object Storage via rclone); snapshots; 3-2-1 rule; **perform the FF-14 restore drill**.
- Checkpoint: backup → restore to test DB → data intact.

### Phase 9 — Security checklist
- No secrets in repo/image (gitleaks already in CI); port scan for 5432/6379; HTTPS + redirect; rate limiting (repo already uses ASP.NET Core built-in rate limiter — review config, not AspNetCoreRateLimit); fail2ban; `.env` perms 600.
- Checkpoint: all items YES.

## Validation per phase
Each phase ends with the checkpoint listed above; learner verifies before "next".
