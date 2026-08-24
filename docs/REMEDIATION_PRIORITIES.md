# FuelFlow — Prioritized Security Remediation Plan

> **2026-08-24:** this plan has been largely executed and superseded by
> [SECURITY_AUDIT_2026-08-21.md](SECURITY_AUDIT_2026-08-21.md) and its findings sub-reports
> (`security-audit-2026-08-21/`). Every finding below marked done/refuted was removed rather than
> annotated; what remains is the open residue. Historical detail lives in the audit report.

**Priority tiers**

| Tier | Meaning | When |
|---|---|---|
| **P0** | Critical — active/known risk. Fix immediately. | Before anything else |
| **P1** | Required before go-live. | Before first real deploy |
| **P2** | Strongly recommended before scale | Within first month / before >500 users |
| **P3** | Nice-to-have / ongoing hygiene | Continuous |

---

## P1 — Required before go-live

### FF-05 — Monobank merchant token committed to git history — HIGH, OPEN (owner action)

- **Location and class:** commit `3cb50dc` → `appsettings.Production.json`, key `Monobank:Token`.
  Payment-provider merchant API token, 34 characters, no placeholder marker. The value is not
  reproduced anywhere; it is handled by hash and entropy profile only.
- **Status:** the owner attests all secrets were rotated 2026-08-20. That is unverifiable without
  touching live systems. What is still owed:
  1. Written confirmation that the token was rotated on or after 2026-08-20 and revoked
     **merchant-side** (not merely replaced in config).
  2. An answer on whether this repository is or ever was public.
  3. An explicit decision on history: rewrite with `git filter-repo`, or formally accept the
     residue — defensible only if (1) confirms rotation and the repo was never public.

(The earlier P0 "live Supabase DB password" finding that used to sit here was **refuted**
2026-08-22: its cited commits do not exist in this repository, and a full 3,766-blob sweep of the
object database found only the literal `***REDACTED***` string. See
[security-audit-2026-08-21/09-refuted.md](security-audit-2026-08-21/09-refuted.md).)

---

## P2 — Before scaling

| ID | Finding | Severity | Fix |
|---|---|---|---|
| FF-23 | Voucher import runs synchronously in-request | Medium | Enqueue import to Hangfire; POST returns ImportId; admin polls a status endpoint. Currently bounded only by `ImportConcurrencyGuard` + 300 s edge timeouts. |

---

## P3 — Ongoing hygiene (continuous)

- **dependabot** — extend the existing `backend/.github/dependabot.yml` to `admin` and `mobile` (npm).
- **CI gates** — add `dotnet list package --vulnerable --include-transitive` and `npm audit --audit-level=high` as CI jobs. Both were unreachable under restricted egress during the audit; other vulnerable dependencies may exist unlooked-for (FF-33 was found by build warnings alone, and is now fixed via a pin).
- **CVE review** — monthly 15-min review of the parse/imaging stack most likely to be an RCE surface:
  `Docnet.Core`, `UglyToad.PdfPig`, `SixLabors.ImageSharp`, `ZXing.Net`.
- **Middleware order** — pin the rate-limiter-after-authentication ordering with a test so a refactor cannot silently turn per-user limits into per-IP ones.
- **Roadmap hardening from `docs/SECURITY.md`** — app attestation (Play Integrity / App Attest),
  SSL pinning, signed OTA updates (FF-03).

---

## Deploy readiness

In-code remediation items are done and verified (build + unit suites green; see the audit's
coverage statement for exact figures and their limits). What remains before first real traffic is
operational, not code — work through
[security-audit-2026-08-21/05-deploy-checklist.md](security-audit-2026-08-21/05-deploy-checklist.md)
in order, then keep the week-one watchlist
([06-post-deploy-watchlist.md](security-audit-2026-08-21/06-post-deploy-watchlist.md)) open.
