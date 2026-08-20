# FuelFlow — Prioritized Security Remediation Plan

Audit of `backend/` (.NET 10 API), `admin/` (React SPA), `mobile/` (Expo RN),
Docker configs, CI, and git history. Findings are prioritized into tiers.

**Priority tiers**

| Tier | Meaning | When |
|---|---|---|
| **P0** | Critical — active/known risk. Fix immediately. | Before anything else (today) |
| **P1** | Required before go-live. | Before first real deploy |
| **P2** | Strongly recommended before scale; or hardening | Within first month / before >500 users |
| **P3** | Nice-to-have / ongoing hygiene | Continuous |

---

## P0 — Critical: do today

### F4 — Live Supabase DB password committed in git history — CRITICAL

- **Root cause:** `docs/DEPLOY.md` previously contained the real connection string
  `postgresql://postgres.dfrjclroguyipgikekff:s.yQn7n6%21GWBK%26Y@aws-1-eu-west-1.pooler.supabase.com:6543/postgres`
  (commits `7ff3dae1`, `0ff95631`). The later redaction (`aa36b124`) does **not** remove it from history.
- **Impact:** Anyone with repo access (or history scrape) can connect to the production database.
- **Fix:**
  1. Rotate the Supabase database password (Supabase → Settings → Database → Reset password).
  2. Update the connection string wherever it's stored (Render env vars / `.env`).
  3. Rewrite git history to purge the secret (squash/rewrite + force-push), or move the repo to private if not already.
  4. Add Gitleaks to CI (see P3) so this class of leak cannot recur.

---

## P1 — Required before go-live

### F8 — Unbounded PDF upload — MEDIUM (in-code, verified)

- **Root cause:** `backend/src/FuelFlow.API/Features/Vouchers/Import/VouchersController.cs:40-55`
  checked the `.pdf` extension only — no size cap, no magic-byte check, no page cap. Import
  rasterized at 200 DPI in-request.
- **Fixed:** `[RequestSizeLimit(25_000_000)]` + Kestrel `MaxRequestBodySize` + `FormOptions`
  `MultipartBodyLengthLimit` (25 MB, `Program.cs`); `%PDF` magic-byte validation; page cap of 200
  (`PdfRenderer.MaxPages`, throws early) mapped to 400.
- **Verified:** `VoucherImportIntegrationTests` (3/3) cover the caps; full integration suite is 32/32 green.
- **Deferred to P2:** moving the import onto Hangfire off the request thread (requires a polling
  endpoint + admin UI rework).

### F15 — No secret scanner in CI — MEDIUM (process) — DONE

- **Fixed:** Gitleaks `secrets-scan` job added to `.github/workflows/ci.yml` (standalone binary,
  full-history checkout, artifact on failure). Note: the job will stay red until F4's git-history
  purge is complete.

### Checklist item — `Auth:DevBypass` and fail-fast guards

- **Already good:** `appsettings.Production.json:13` sets `DevBypass: false`, and the app refuses to
  start without Jwt/Twilio/Monobank config. Keep it that way; verify the env vars are actually set
  on the target platform before the first deploy.

---

## P2 — Before scaling (first month / >500 users)

Done in this pass: **F9** (AllowedHosts pinned — dev: `localhost;127.0.0.1;10.0.2.2`, prod: current live hosts), **F11** (send-code/verify-code rate limits now keyed on normalized phone number), **F6** (hardcoded `onrender` fallback dropped from `mobile/app.json`; admin fallbacks already removed), **F7** (`debug.keystore` untracked + gitignored), **F14** (base images pinned to digests across Dockerfiles + compose), `.env.example` (added `mobile/.env.example` and repo-root `.env.example` for compose vars).

Verified this pass (integration tests + unit tests, 32/32 + 337/337 green): **F10** (verification codes stored as hashes — `WidenVerificationCodeToHash` migration), **F11** (per-phone limits), **F8** (import caps), **F5** (`Jwt:Secret` removed from base appsettings). Two latent security bugs found while enabling the test suite and now fixed:
- `RefreshTokenCommand` family-revocation loop mutated detached entities under `QueryTrackingBehavior.NoTracking`, so replay detection never actually revoked the token family — added explicit `Update()`.
- `UseRateLimiter()` ran before `UseAuthentication()`, so the per-user purchase limit was effectively per-IP — auth now runs first.

| ID | Finding | Severity | Fix |
|---|---|---|---|
| — | Voucher import runs in-request (Hangfire offload) | Medium | Enqueue import to Hangfire; POST returns ImportId; admin polls a status endpoint |

---

## P3 — Ongoing hygiene (continuous)

- **Gitleaks** in CI (enforces F4 fix).
- **dependabot** — extend the existing `backend/.github/dependabot.yml` to `admin` and `mobile` (npm).
- **CI gates** — `dotnet list package --vulnerable --include-transitive` and `npm audit --audit-level=high`.
- **CVE review** — monthly 15-min review of the parse/imaging stack most likely to be an RCE surface:
  `Docnet.Core`, `UglyToad.PdfPig`, `SixLabors.ImageSharp`, `ZXing.Net`.
- **Structure** — verify the identity-ordering dependency in the middleware (F2) is covered by a test;
  add tests that pin middleware order.
- **Roadmap hardening from `docs/SECURITY.md`** — app attestation (Play Integrity / App Attest),
  SSL pinning, HSTS/security headers (add at nginx, not app).

---

## Rollout order

1. **P0:** rotate Supabase password → purge git history → Gitleaks.
2. **P1:** all in-code items done (F8 caps, F12 containers, F13 ports/Redis); verify
   `npx tsc --noEmit` (admin/mobile) and `dotnet build` + `dotnet test` (backend) per `AGENTS.md`.
3. Provision Droplet + managed Postgres + registry; apply Cloud Firewall + UFW.
4. Wire TLS + nginx (API proxy, admin static, `limit_req`, HSTS).
5. Add `.github/workflows/deploy.yml`; deploy to staging, then production.
6. Monitoring + alerts; one DB restore test; snapshot the Droplet.

Estimated early-stage cost: **≈ $30/mo** (2 GB Droplet ~$14 + managed PG ~$15 + registry ~$0.10).