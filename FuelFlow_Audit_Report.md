# Enterprise Solution Audit Report — FuelFlow

**Date:** 2026-09-18
**Method:** Read-only, evidence-based static audit. No source files were modified.
**Branch inspected:** `fix/verify-code-no-auto-activate` (HEAD `aef1b2d`)
**Auditor role:** Principal Security Auditor / Staff Architect / Senior Reviewer

> **Confidence discipline.** Every finding below is labelled **Confirmed** (directly demonstrated by the code cited), **Likely** (strong evidence, some inference), or **Suspected/Requires-verification** (plausible, needs runtime/tooling to confirm). File paths are repository-relative. Line numbers were accurate at inspection time; verify against current code before acting. Nothing here should be treated as an exploit proof without the runtime checks called out in §12.

---

## 1. Executive Summary

FuelFlow is a live, real-money fuel-voucher platform (≈105k LOC C# backend, React 19 admin, Expo React Native mobile, Next.js marketing site). **This is a well-engineered, security-conscious codebase** — far more hardened than typical for a solo project. The audit found no confirmed, remotely-exploitable authentication or authorization bypass in the current code. Two previously-suspected security bugs (device-signature trailing-slash bypass; git-history secret leak) were investigated and **refuted** as already-fixed / never-real.

The real risks cluster in three places:

1. **Payment/data-integrity correctness**, not classic vulnerabilities. The highest-value issues are (a) `CreateCheckout` calls Monobank *before* persisting the order and swallows failures — independently flagged by four separate audits — which can produce paid-but-unreconcilable invoices; and (b) the two copies of `FulfillmentService` (API vs JobsWorker) have **drifted**, with the JobsWorker copy missing an expiry filter and a payload-matching bugfix.

2. **Session-invalidation completeness.** The refresh endpoint does not re-check `IsBanned`/`IsDeleted` and the `token_version` check fails open when the claim is absent. Neither is a break today (the live-request middleware compensates), but a banned user can keep minting access tokens via `/api/auth/refresh`.

3. **Operational/supply-chain gaps.** CI builds but **runs no tests, no gitleaks, and no dependency audit**, yet auto-deploys to prod on every push to `main` via a self-hosted root runner with no approval gate. The tooling the repo is clearly set up for is not wired into CI.

**Most important uncertainty:** `.gitleaks.toml` documents that commit `3cb50dc` once contained a real Monobank acquiring token. **Confirm out-of-band that this token was rotated** — this is the single highest-value verification item.

Frontends are asymmetric: the **mobile** app has an excellent, accessibility-first design system; the **admin** app is the weak surface (one 2233-line component, no code-splitting, hand-rolled dialogs with no a11y).

### Category assessment (evidence-based, no single headline score)

| Dimension | Assessment | Basis |
|---|---|---|
| Security (authn/authz) | **Strong** | Deny-by-default authz, pinned anonymous surface, refresh-family reuse detection, prod config guards. Gaps are completeness, not bypasses. |
| Payment / data integrity | **Needs work** | Checkout ordering + swallow (P1), FulfillmentService drift (P0-adjacent), no DB uniqueness backstop on `fulfillments.voucher_id`, no concurrency tokens. |
| Secrets / config | **Strong, 1 open item** | Guarded env vars, good gitleaks config — but not run in CI; historical Monobank token rotation unverified. |
| Architecture (vertical slice) | **Mostly sound** | Consistent handler pattern; a few real boundary violations (ProvidersController writes inline; audit service misplaced). |
| Maintainability | **Good backend, weak admin** | Disciplined error handling; `AssignVouchersToOrderAsync` + admin `admin.tsx` are the outliers. |
| Type safety | **Good** | Clean C# nullability; `any` confined to JS API seams. |
| Testing | **Strong backend, ~none frontend** | Real-Postgres concurrency tests, pinned auth surface; admin has 1 test file, mobile has none. |
| Infrastructure / CI-CD | **Good runtime, weak pipeline** | Digest-pinned images & SHA-pinned actions at runtime; CI runs no tests/secret-scan/audit; root self-hosted deploy without approval. |
| UX / UI | **Mobile strong, admin gaps** | Mobile design system exemplary; admin missing loading/error states, mojibake strings. |
| Accessibility | **Mobile strong (with drift), admin weak** | Admin dialogs lack roles/focus trap; form labels unassociated. |

---

## 2. Solution Architecture Map

```
                    Mobile (Expo RN)          Admin (React 19 + Vite)     Website (Next.js 16)
                    device-signed /api            nginx + /api allow-list       static marketing
                          \                              |                          /
                           \                             |                         /
                            ─────────────  Caddy (edge: TLS, HSTS, CSP, blocks /hangfire & /metrics) ─────
                                                         |
                                          ASP.NET Core API (.NET 10)  :8080
                                          ├─ Middleware: DeviceSignature, SessionValidation,
                                          │              GlobalExceptionHandler, RequestLogging, RateLimiter
                                          ├─ Features/ (18 vertical slices: Command/Query + Handler)
                                          ├─ BackgroundJobs/ (Hangfire in-process: FulfillmentService)
                                          └─ SharedKernel/ (DTOs, Domain, Security, Money)
                                                         |
                    ┌────────────────────────┬──────────┴───────────┬─────────────────────┐
              PostgreSQL (Npgsql)          Redis                 Monobank API           Twilio / SMSClub
              EF Core 10, 40 migrations    cache/rate-limit      payments (UAH)          OTP delivery
              advisory locks + outbox      noeviction            webhook (secp256k1 sig)
```

Optional `FuelFlow.JobsWorker` is a **second host** that carries a *duplicate* `FulfillmentService` (see TASK-002). In the chosen deployment (per project memory) Hangfire runs in-process in the API and the separate worker is not required — but both are registered, so which binary is deployed determines fulfillment behavior.
