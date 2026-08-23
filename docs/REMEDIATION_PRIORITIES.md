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

### F4 — "Live Supabase DB password committed in git history" — **REFUTED (2026-08-22)**

This finding does not hold for this repository. It is kept rather than deleted because it drove
real decisions — the Gitleaks CI job (F15) exists because of it, and it was still being treated as
an open Critical eight months later.

What was checked, and how:

- **The three commits it cites do not exist here.** `git cat-file -t 7ff3dae1`, `0ff95631` and
  `aa36b124` all return `fatal: Not a valid object name`.
- **No live password value is present in any object, at any commit.** The Supabase host and project
  ref do appear in five commits (`215dc1c`, `01f9b2f`, `c308d50`, `8814c74`, `ae8618e`). Extracting
  every distinct value in the password position from **all 3,766 blobs in the object database**
  yields exactly one string, ten times over: the literal `***REDACTED***`. There is nothing to
  rotate and nothing to purge.
- **What *is* in history** is the Supabase project ref and the `postgres.<ref>` username. That is
  low-value on its own (the project ref appears in any client that ever connected), and it is not
  a credential.

Caveat, stated rather than hidden: this proves the current object database is clean, not that no
live value was ever pushed. If history was rewritten before this check, or if F4 was written against
a different clone, a value could have existed on a remote at some point. That question is answered
by the owner's rotation attestation (secrets rotated 2026-08-20), not by this repository.

**The real open secrets finding is FF-05, not F4.** See
[docs/security-audit-2026-08-21/02-findings-critical-high.md](security-audit-2026-08-21/02-findings-critical-high.md).

### FF-05 — Monobank merchant token committed to git history — HIGH, OPEN

- **Location and class:** commit `3cb50dc197918b771ec912bd21514d9e9c7e6412` →
  `appsettings.Production.json`, key `Monobank:Token`. Class: payment-provider merchant API token.
  The value is not reproduced in any document; it was handled by hash and entropy profile only.
- **Why it survives scrutiny where the rest of that file does not:** the same commit's
  `Monobank:PublicKey`, `Twilio:PhoneNumber`, `Twilio:AccountSid`/`AuthToken` are all literal
  placeholders, and its `Jwt:Secret` is the 71-character `PlaceholderSecret` constant that
  `Extensions/AuthSetup.cs:31-42` refuses to boot with in Production. The Monobank token is 34
  characters with entropy 3.75 and **no placeholder marker** — it cannot be dismissed the same way.
- **Fix:**
  1. Rotate the merchant token in the Monobank merchant portal. Only the merchant can do this.
  2. Set the new value as `Monobank__Token` in `deploy/.env` on the Droplet — never committed.
  3. Decide explicitly between rewriting history (squash/force-push) and accepting the exposure on
     the strength of step 1. Record whichever is chosen; an undecided item reads as "handled".
  4. Confirm repository visibility. A private repo changes the blast radius but not the need to
     rotate.

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
  full-history checkout via `fetch-depth: 0`, artifact on failure). The binary is now verified
  against a pinned SHA-256 before it runs, because an unverified download executes with the
  repository checked out and the job token in the environment.
- **The gate is green, not red.** This entry previously said it "will stay red until F4's
  git-history purge is complete". There is no purge to complete — see the F4 refutation above — and
  no live value exists in any blob to trip the scanner. Treat a red `secrets-scan` as a real leak,
  not as expected background noise.

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

- **Gitleaks** in CI — shipped (see F15). Its job is to stop the *next* committed credential, not to
  clear F4, which is refuted. Note the tool's blind spot: `generic-api-key`'s allowlist drops any
  finding whose captured value contains one of ~1,476 stopwords as a substring, so a longer, more
  random secret is *more* likely to be silently suppressed. A green scan is not proof of no secret.
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