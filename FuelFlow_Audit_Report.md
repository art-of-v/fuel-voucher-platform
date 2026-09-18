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

---

## 3. Security Findings (ordered by severity × confidence)

### SEC-1 — Refresh endpoint does not re-check `IsBanned` / `IsDeleted` — **Medium, Confirmed**
`Features/Auth/Refresh/RefreshTokenCommand.cs:95-99` guards only `!refreshToken.User.IsActive`. A banned-but-active or soft-deleted user with a still-valid refresh token can keep minting 15-minute access tokens. Bounded because `SessionValidationMiddleware` re-checks `IsBanned` live on authenticated requests — but `/api/auth/refresh` is itself anonymous, so it never passes that check. → TASK-003.

### SEC-2 — `token_version` check fails open when the claim is missing/unparseable — **Medium, Confirmed**
`Middleware/SessionValidationMiddleware.cs:46-53` — `int.TryParse(...)` false → the whole mismatch condition short-circuits to *allow*. Global session invalidation ("log out everywhere", post-compromise) is defeated by any token lacking the claim. Current tokens always include it, so this is defense-in-depth today. → TASK-004 (bundle with SEC-1).

### SEC-3 — OTP codes hashed with unsalted single-round SHA-256 — **Low, Confirmed (accepted risk)**
`SharedKernel/Security/SecretsHasher.cs:16-20`. A 6-digit code space (10^6) is trivially reversible from a DB leak. Mitigated by 10-min expiry + 5-attempt cap + per-phone rate limit; the class comment owns the tradeoff. Refresh tokens (high entropy) are fine with the same hasher. → TASK-024 (optional hardening: HMAC the OTP).

### SEC-4 — `Monobank:Enabled` not enforced at startup in Production — **Low, Likely**
`Program.cs:187-198` only blocks boot when Monobank is enabled *and* the key is a placeholder. If a config typo left it `false`, the webhook controller (`MonobankWebhookController.cs:65-77`) processes webhooks **without signature verification** and swaps in `MockMonobankClient`. `deploy/docker-compose.prod.yml:123` hardcodes `"true"`, so the real deploy holds. → TASK-011.

### SEC-5 — `DeviceAuth` disablement escape hatch reachable in Production — **Low, Confirmed (by design)**
`Program.cs:237-254` + `DeviceAuthOptions.cs:15`: `DeviceAuth__AcknowledgeDisabledInProduction=true` boots with device binding off; a stolen access token then suffices to purchase. Deliberate, logged, opt-in. Flagged for the risk register only. Prod compose leaves it on.

### SEC-6 — `DeviceAuth__RequireSignatureForEndpoints` config-binder append — **Low, Confirmed (fail-safe)**
`SharedKernel/Options/DeviceAuthOptions.cs:30-34` pre-seeds the list; .NET's binder overwrites by index and never clears, so env overrides can only *add* enforced endpoints, never remove them. A config foot-gun, not a hole. Documented at `deploy/docker-compose.prod.yml:163-164`. → TASK-025 (doc/robustness only).

### Investigated and REFUTED (reported so they are not "re-fixed")
- **Device-signature trailing-slash bypass — not present.** `Middleware/DeviceSignatureMiddleware.cs:220-279` normalizes paths (`NormalizePath` collapses slashes + strips trailing `/`) before matching, so `POST /api/purchases/` is still enforced. Confirmed by `MiddlewareTests`.
- **Injection — none found.** No `FromSqlRaw`/string-concatenated SQL. The `ExecuteSqlInterpolatedAsync` calls in `BackgroundJobs/FulfillmentService.cs` use `FormattableString` → Npgsql parameter binding.
- **Git-history secret leak — refuted for the DB password** (all occurrences are literal `***REDACTED***`). **But** see §12 item 1 for the Monobank token, which `.gitleaks.toml` states *was* real in commit `3cb50dc`.

### Confirmed-good controls (do not regress)
Deny-by-default fallback authz (`AuthSetup.cs:100-110`) + pinned anonymous surface test; JWT HS256 with ≥32-char secret enforced and placeholder refused in prod; refresh-token reuse → family revocation; `GlobalExceptionHandler` withholds EF/Npgsql detail; explicit CORS allow-list + Origin-checked refresh cookie (`HttpOnly`/`Secure`/`SameSite`); Caddy security headers + `/hangfire`,`/metrics` blocked at edge; layered rate limiting (per-phone/-IP/-user); PDF import Staff-only with 25MB caps + magic-byte check; secrets kept in gitignored env files with `:?` guards.

---

## 4. Dependency / Infrastructure Findings

### INF-1 — CI runs no tests, no gitleaks, no dependency audit — **High, Confirmed**
`.github/workflows/ci.yml` (jobs 26-114): backend does `restore`+`build` only; JS jobs `npm ci`+`build`; mobile `typecheck`. No `dotnet test`, no JS `test` scripts, no `gitleaks`, no `dotnet list package --vulnerable`/`npm audit` — yet the `deploy` job (line 121) fires on every green push to `main`. The excellent `.gitleaks.toml` and the ~430 backend tests are never run in the pipeline. The green-CI signal is weaker than it looks. → TASK-015..018.

### INF-2 — Self-hosted root deploy on push to `main` with no approval gate — **Medium, Confirmed**
`ci.yml:121-145`: `deploy` runs on the prod box (`[self-hosted, linux, x64]`), does `git reset --hard` + `git clean -fd` in `/root/FuelFlow` as root, then `docker compose build && up -d`. Guarded to `push`+`main` only (good), but no GitHub `environment` approval and no evidence of branch protection. Anyone able to push `main` gets root RCE on prod. → TASK-019.

### INF-3 — No .NET lock files — **Medium, Confirmed**
No `packages.lock.json` under `backend/` (JS side has all three `package-lock.json`). CI and the on-server build both `restore` fresh, so transitive versions can shift into a prod build with no diff — undermining the SHA-pinning discipline used everywhere else. → TASK-020.

### INF-4 — Prod compose base images tag-pinned, not digest-pinned — **Medium, Confirmed**
`deploy/docker-compose.prod.yml:51,219,241` use `caddy:2-alpine`/`postgres:16-alpine`/`redis:7-alpine` while the root compose and all Dockerfiles pin `@sha256:`. `docker compose build` runs on the server each deploy, so a moving `postgres:16-alpine` tag can change the DB engine under a live database. → TASK-021.

### INF-5 — Dev compose enables live Monobank + hardcoded DB password — **Low/Medium (dev only), Confirmed**
`docker-compose.yml:42-49`: `ASPNETCORE_ENVIRONMENT=Development` with `Monobank__Enabled=true` pointing at `https://api.monobank.ua`, inline `Password=postgres`. Dev machines can hit live payments. → TASK-026.

### INF-6 — `.env.example` stale vs. prod variable surface — **Low, Confirmed**
Root `.env.example` (7 lines, old `MONOBANK_API_TOKEN` names) doesn't match the `:?`-guarded prod var set (`JWT_SECRET`, `MONOBANK_TOKEN`, `MONOBANK_PUBLIC_KEY`, domains, Loki/Grafana creds). No `deploy/.env.example`. → TASK-027.

### INF-7 — Frontends run nginx as root; edge has no rate limiter — **Low, Confirmed**
`admin/Dockerfile`/`website/Dockerfile` have no `USER` (mitigated by `no-new-privileges`). Caddy has no edge throttle on `/api/auth/*` (API-side limits exist). Optional hardening. → TASK-028.

### Requires tooling to verify (see §12)
JS CVEs (`npm audit` in admin/website/mobile), .NET CVEs (`dotnet list package --vulnerable`), the two OpenTelemetry **pre-release** packages in the API (`FuelFlow.API.csproj:24,51`), and whether `mobile`'s `lucide-react` (web build, alongside `lucide-react-native`) is actually used.

### Confirmed-good
`permissions: contents: read` in CI, actions SHA-pinned, no `pull_request_target`; Postgres/Redis localhost-bound; Redis password via `umask 077` file not argv; non-root backend container; strong Caddy CSP/headers; exact-pinned .NET packages with a documented `SSH.NET` forward-pin for GHSA-q939-rpr3-3284.

---

## 5. Database & Data Findings

### DB-1 — No DB uniqueness backstop for one-fulfillment-per-voucher — **High, Confirmed (absence); Likely-low reachability**
`Features/Orders/Configurations/FulfillmentConfiguration.cs:31-32` — both `HasIndex(OrderId)` and `HasIndex(VoucherId)` are **non-unique** (confirmed in the model snapshot). Nothing in the schema prevents two `fulfillments` rows for the same `voucher_id`. Safety rests entirely on the app-level advisory lock + conditional `UPDATE ... WHERE status='Available'` compare-and-swap (`BackgroundJobs/FulfillmentService.cs:642-656`), which looks correct under READ COMMITTED. **But** admin paths mutate voucher status directly, bypassing the CAS — `RestoreVoucher/RestoreVoucherCommandHandler.cs:43`, `UnblockVoucher/UnblockVoucherCommand.cs:51` — so a restore/unblock racing the fulfillment job has no DB rejection. This is real fuel/money potentially handed out twice. → TASK-005 (add partial unique index after a dupe check).

### DB-2 — `CreateCheckout` calls Monobank before persisting the order; no transaction — **High, Confirmed**
`Features/Orders/CreateCheckout/CreateCheckoutCommandHandler.cs`: `Orders.Add(order)` (:156) → `CreateInvoiceAsync` (:171) → single `SaveChangesAsync` (:192), with the invoice exception swallowed (:184-190). If the process dies between a successful Monobank call and `SaveChanges`, **Monobank holds an invoice with no order row** — the customer can pay it, and the webhook hits `order_not_found`. Conversely a failed invoice call persists a dead `PendingPayment` order with null `PaymentUrl`. `RefundOrderCommandHandler` demonstrates the correct ordering (persist intent *before* the external call, :173 then :205). **Highest-impact money bug after DB-1.** Flagged independently by the security, data, code-health, and testing audits. → TASK-001.

### DB-3 — No optimistic concurrency token on any money/stock entity — **Medium, Confirmed**
No `xmin`/`RowVersion`/`IsConcurrencyToken` anywhere (grep: 0 hits). `Order`, `Refund`, `FuelVoucher`, `Fulfillment` all use last-writer-wins outside the advisory-lock hot path. Admin edits racing the background jobs can silently lose updates. → TASK-009 (map `xmin` on `Order` first; config-only, no schema change).

### DB-4 — Monobank webhook idempotency is app-logic-only; concurrent duplicates can race — **Medium, Likely**
`Features/Monobank/ProcessWebhook/ProcessMonobankWebhookCommandHandler.cs`: staleness/status/state-machine checks (:71-135) all read the same pre-transition state; the ORDER_CREATED outbox insert is de-duped by read-then-insert (:157-183) with **no unique constraint** on `outbox_events` (`OutboxEventConfiguration.cs:41-42` indexes only `Processed`/`CreatedAtUtc`). Two overlapping deliveries can both insert. Downstream fulfillment is itself idempotent, so double-fulfill is unlikely — but the backstop is missing. → TASK-010 (partial unique index or per-order advisory lock in the webhook).

### DB-5 — Soft-delete global query filter inconsistent — **Medium, Confirmed**
`HasQueryFilter(!IsDeleted)` exists on `FuelVoucher` (`FuelVoucherConfiguration.cs:118`) and `Order` (`OrderConfiguration.cs:71`) but **not on `User`** (`UserConfiguration.cs` has the column + filtered unique index but no filter). Deleted users are excluded only by manual `Where` — any handler that forgets it leaks soft-deleted users. (Note: the Order/Voucher filters are then bypassed 15+ times via `IgnoreQueryFilters()` for admin views — intentional but means they protect little.) → TASK-012.

### DB-6 — Refund↔order status transitions unguarded against sync-vs-handler races — **Low-Medium, Likely**
`RefundOrderCommandHandler.ApplyOrderStatus` (:275-298) and `RefundStatusSyncService.ApplyOrderStatus` (:222-245) both mutate `order.Status` with no lock/token; the scheduled sync can overlap a manual/webhook refund. Mitigated by the unique `refunds(order_id)` index (no duplicate refund rows). Resolved by DB-3's concurrency token. → covered by TASK-009.

### DB-7 — Unbounded admin/user list queries — **Medium, Confirmed (performance)**
`GetAdminOrders...Handler.cs:21-28` and `GetAdminPurchases...Handler.cs:43-51` `ToListAsync()` the whole table with graph includes, no `Skip/Take`; `GetAdminUsers...Handler.cs:13-33` loads the **entire** users table then filters/searches **in memory**. A `Persistence/Pagination.cs` helper exists and `GetAdminVouchers` already uses it as the template. → TASK-013 (start with `GetAdminUsers`).

### DB-8 — Reconciliation sweep coupled to every payment webhook — **Low, Likely (performance)**
`FulfillmentService.ProcessPendingOrdersAsync:68-69` unconditionally runs `FixMismatchedFulfillmentsAsync` (scan of all `Fulfilled` orders) + backfill on every webhook-triggered pass (`ProcessMonobankWebhookCommandHandler.cs:206-210`). O(all fulfilled orders) work per payment. → TASK-014 (move to a low-frequency scheduled job).

### Confirmed-good
`refunds(order_id)` unique index + graceful `DbUpdateException` race handling; `orders.idempotency_key` filtered-unique index + checkout dedup; per-order `pg_advisory_xact_lock` + single-transaction claim-and-fulfill; reads correctly `AsNoTracking` with explicit `.AsTracking()` opt-in on mutation paths; verification codes & refresh tokens hashed before store; webhook amount validated against server-side price.

---

## 6. Vertical Slice Architecture Findings

**Verdict: mostly sound.** Nearly every slice follows a consistent `Command`/`Query`+`Handler` pattern (auto-registered via Scrutor scan, `ServiceSetup.cs:127-132`); shared contracts correctly live in `SharedKernel/DTOs`; domain entities co-locate with their slice. The issues below are localized, not systemic — and no large new abstraction is warranted.

### ARC-1 — `ProvidersController` is an anemic-controller-with-logic — **High (consistency), Confirmed**
`Features/Providers/ProvidersController.cs` (449 lines) injects `ApplicationDbContext` (:23) and does inline CRUD of Stations/FuelTypes/FuelPackages including a multi-entity cascade delete (:143-150) and package-cloning (:189-224). Read paths use handlers; write paths skip the application layer that exists in the same slice. The single biggest inconsistency in the backend. Dependency direction: Controller → EF directly. → TASK-029 (extract to command handlers mirroring `Features/Stations/`).

### ARC-2 — Providers vs Stations own the same entities — **Medium, Confirmed**
Both slices write `Stations`/`FuelTypes`/`FuelPackages` with divergent fuel-package logic. "Provider" is really a read projection of a Station. Two owners, no single source of truth. → TASK-030 (make Stations the mutation owner, Providers read-only). One-time ownership decision.

### ARC-3 — `FulfillmentService` is a god service — **Medium, Confirmed**
`BackgroundJobs/FulfillmentService.cs` (725 lines, 6 deps): outbox draining + voucher matching/claiming + mismatch repair + backfill + status transitions + auto-refund orchestration. For a background *orchestrator* the cross-slice reach is defensible, but the responsibility count is high — and it already drifted (TASK-002). **Do not refactor speculatively** — live payment code with careful transaction semantics; if touched, split `FixMismatchedFulfillmentsAsync` first. → TASK-031 (low priority, conditional).

### ARC-4 — Reporting slices depend on a command handler's static method — **Low, Confirmed**
`Report/GetReport/GetReportQueryHandler.cs:103,152` and `Admin/GetReconciliation/GetReconciliationQueryHandler.cs` call `RefundOrderCommandHandler.ComputeFulfilledValueKopecks` (:305). Domain calc hanging off a *command* handler. → TASK-032 (move to `Order` entity / `Orders/SharedModels`).

### ARC-5 — `ProviderEventService` is a cross-cutting audit util inside a feature slice — **Low, Likely**
`Features/Providers/ProviderEventService.cs` is a generic audit-event writer (its entity already lives in `SharedKernel/Domain`), yet 6 slices (Auth, Orders, Vouchers) import the Providers namespace just to log audit rows. → TASK-033 (relocate to `SharedKernel/Audit`, rename). Low-risk, additive.

### ARC-6 — Read operations named `Command` — **Low, Confirmed (cosmetic)**
`Sync/GetSync/GetSyncCommand.cs`, `Vouchers/GetInventory/GetInventoryCommand.cs`, `Orders/GetUserPurchases/GetUserPurchasesCommand.cs`, `Vouchers/GetUserVouchers/GetUserVouchersCommand.cs` are reads modeled as `Command`. Muddies the read/write signal; nothing breaks. → TASK-034 (rename opportunistically; CRLF makes rename diffs look large).

### Confirmed-good boundaries (do not "fix")
Shared DTOs used correctly across slices; `UserController` thinly composing two handlers; handler-reuse along the payment seam; EF-in-handlers is the deliberate vertical-slice-over-shared-EF style (a repository abstraction here would be a means-not-goal violation).

---

## 7. Code Health & Maintainability Findings

### CH-1 — Two divergent copies of `FulfillmentService`; the bugfix landed in only one — **High, Confirmed**
`BackgroundJobs/FulfillmentService.cs` (API, 725 lines) vs `FuelFlow.JobsWorker/Services/FulfillmentService.cs` (600 lines), ~80% identical, both registered. Divergences in the **JobsWorker** copy:
- **Duplicate-event guard uses the pre-fix substring match** (`:491-497` `payload.Contains(orderIdString)`) — the API copy replaced this with jsonb containment (`:513-524`) precisely because the substring version matched ids embedded in other payloads and full-scanned.
- **Expiry filter commented out** (`:573-576` `// TODO: uncomment to exclude expired vouchers`) — so if the JobsWorker fulfills, customers get the most-expired stock first.
- **Auto-refund + partial-fulfillment tracking** exist only in the API copy.
A genuine DRY violation (identical logic, must change for the same reason) that has already produced a correctness bug. → TASK-002 (extract one shared implementation; interim: port the two fixes into the worker).

### CH-2 — `VerifyChallengeCommandHandler` reimplements the shared crypto verifier — **Medium, Confirmed**
`Features/Auth/VerifyChallenge/VerifyChallengeCommandHandler.cs:175-282` inlines the full PEM/RSA/ECDSA fallback logic that `SharedKernel/Security/AsymmetricSignatureVerifier.cs` already provides (and that the webhook + device middleware use). A forked crypto verifier is the duplication most likely to develop a security-relevant skew. → TASK-035 (inject `IAsymmetricSignatureVerifier`).

### CH-3 — `AssignVouchersToOrderAsync` is the worst backend function — **Medium, Confirmed**
`BackgroundJobs/FulfillmentService.cs:331-603` — ~270 lines: transaction + advisory lock + four early-return branches each repeating an identical "mark outbox processed + commit" block (:368-378, :386-402, :409-435, :577-588) + nested assignment loop + post-commit refund. Its length is *why* CH-1 drift happened. → TASK-036 (extract `MarkOutboxProcessedAsync` + phase methods; do after TASK-002).

### CH-4 — Silent empty catches in PDF parsers — **Low, Confirmed**
`KloVoucherParser.cs:76-78`, `WogVoucherParser.cs:69,108-110`, `OkkoVoucherParser.cs:68` — `catch (Exception) { }` around QR decode, no log. Degrades gracefully (confidence drops) but a systematically failing decoder is invisible. → TASK-037 (log at debug/warning).

### CH-5 — `any` at JS API boundaries — **Low, Confirmed**
`mobile/src/features/vouchers/api/getVouchers.ts:14,68,84` and ~28 hits in `admin/src/pages/admin.tsx`. Defensively coded but no runtime contract validation, so a backend field rename surfaces as silent `undefined`. → TASK-038 (type the API DTOs / add a schema at the seam).

### CH-6 — Magic numbers in `MonobankClient` — **Low, Confirmed**
`MonobankClient.cs:39` (`ccy = 980`), `:47` (`validity = 3600`) — unexplained literals. → TASK-039 (named constants). Kopecks conversion is already well-abstracted via `Money.ToKopecks` — good.

### CH-7 — Empty `_payload/` directory + two live TODOs — **Low, Confirmed**
`_payload/` at repo root is empty (stray build artifact). TODOs: the dangerous JobsWorker expiry filter (CH-1) and a documented `VoucherExpiration:Enabled` test escape hatch (`FulfillmentService.cs:617`, warns never to disable in prod — consider gating to non-Production in code). The dead nginx `/uploads` block is **already removed and documented** (`admin/nginx.conf:73-75`) — no action. → TASK-040 (delete `_payload/`).

### Confirmed-good
Disciplined error handling overall — `RefundOrderCommandHandler` narrow `catch (DbUpdateException) when(...)` for the refund race + column-limit truncation; `MonobankClient` catch-log-**rethrow**; `SmsClubSmsService` exception filter preserving the budget-guard. C# nullability/type-safety clean (no `#pragma warning disable` outside generated migrations). No accidental critical-path swallow beyond the known CreateCheckout case (DB-2).

---

## 8. UI/UX & Accessibility Findings

### UX-1 — Admin custom dialogs have no dialog semantics, focus trap/restore, or Escape — **High (a11y blocker), Confirmed**
Six hand-rolled `<div className="fixed inset-0…">` modals in `admin/src/pages/admin.tsx` (QR :1940, signature :1963, delete-selected :1982, delete-all :2006, refund :2030, reconciliation :2056) — no `role="dialog"`, `aria-modal`, labelledby, focus management, or Escape. The delete/refund ones gate destructive actions. Radix is already a dependency. → TASK-041 (swap to `@radix-ui/react-dialog`/`AlertDialog`).

### UX-2 — Mojibake in the signature modal — **Medium, Confirmed**
`admin.tsx:1967,1977` render corrupted bytes (`╨ƒ╨ò╨á…`) instead of Ukrainian text via `t(...)`. User-facing broken text; likely an encoding-on-save issue (repo-wide CRLF per memory). → TASK-006 (quick fix).

### UX-3 — `AdminScreen` is a 2233-line monolith; no code-splitting — **High (maintainability), Confirmed**
`admin/src/pages/admin.tsx`: 2233 lines, 38 `useState`, 10 inline tab bodies, 0 `useCallback`/`useMemo`; `admin/src/App.tsx:3` static-imports it, no `lazy`/`Suspense` anywhere — the whole app ships in one chunk behind login. Perf impact classified **Likely risk / opportunity** (not measured). Some tabs (`ProvidersTab` etc.) already follow the good split pattern. → TASK-042 (extract + lazy-load tabs), TASK-043 (route-level code-split).

### UX-4 — Login lacks `<form>`/labels; controls on non-focusable divs — **Medium, Confirmed**
`admin.tsx:552-586` login has no `<form>` (Enter doesn't submit), placeholder-only inputs, no `type="tel"`/`autoComplete`. 18 `<label>` with 0 `htmlFor`. Voucher select-all/row checkboxes are clickable `<div>`s (:1063,:1107) — not keyboard-reachable; sortable `<th onClick>` (:1080) with no `aria-sort`. → TASK-044.

### UX-5 — Inconsistent loading/error/empty states in admin — **Medium, Confirmed**
Vouchers tab does it well; users/purchases tables show "No … found" while loading and **no query handles `isError`** (grep: 0) — a failed fetch silently shows empty. Mobile already has a `LoadingState`/`ErrorState`/`EmptyState` trio to mirror. → TASK-045.

### UX-6 — Mobile screens bypass the design system with raw `Pressable`s — **Medium, Confirmed**
167 `TouchableOpacity`/`Pressable` vs 39 `accessibilityLabel`. `app/company.tsx` (10 raw, 0 a11y, incl. an icon-only close button :492), `contracts.tsx`, `basket.tsx`, `my-codes.tsx`, `checkout.tsx`, `profile.tsx`. These lose the accessible name/role/state + 44pt target the `Button`/`IconButton` primitives enforce. → TASK-046.

### Minor
UX-7 admin user-delete uses `window.confirm` (`:690`) inconsistent with the custom modals (→ folds into TASK-041). UX-8 `colSpan={10}` on a 12-col users table (`:707`) — cosmetic. Mobile reduced-motion not honored (admin CSS does, `index.css:255`) — low.

### Confirmed-good (mobile design system — exemplary)
`Button.tsx` (role/label/`accessibilityState`/hitSlop-to-44pt), `IconButton.tsx` (**required** `accessibilityLabel`), `BottomSheet.tsx` (hardware-back, safe-area, `role="header"`), `InlineFeedback.tsx` (`role="alert"` + live region), `TextField.tsx` (`aria-invalid`/hint), documented design tokens with enforced import boundary. Admin's `ProvidersTab.tsx` is the in-repo "good pattern" reference.

---

## 9. Performance Findings
All classified **code pattern / likely risk / opportunity** — nothing was measured at runtime.
- Admin re-render surface (UX-3): 38 `useState` in one 2200-line component, `.sort()`/`.map()` re-derived per keystroke — **likely risk** on the vouchers table.
- No admin code-splitting (UX-3) — **opportunity**.
- Unbounded admin queries (DB-7) — **confirmed pattern**, will degrade with data volume.
- Reconciliation sweep per webhook (DB-8) — **likely** scaling issue.
- Mobile lists mostly fine; a few `.map` over `FlatList` for small bounded lists — **opportunity only**. Effect/timer cleanup mostly present; no leak confirmed.

---

## 10. Testing & Verification Findings

**Backend testing is strong** (~430 cases, 55 files, **zero skipped/disabled**, real-Postgres via Testcontainers). Well-covered with high-quality assertions: the pinned anonymous-endpoint surface (`AnonymousEndpointSurfaceTests.cs` — the best test in the suite), auth/OTP/JWT/refresh-family reuse, `token_version` invalidation, **voucher double-allocation across both binaries** (`FulfillmentConcurrencyIntegrationTests.cs` with a barrier), double-redemption, webhook business logic (amount mismatch, stale date, duplicate), refund path, signature verification, PDF parsers.

**Critical test gaps (untested high-risk behavior):**
- **TEST-G1 (High):** no test that a **banned/soft-deleted user cannot refresh** (SEC-1) — refresh is anonymous so it skips `SessionValidationMiddleware`. → TASK-007.
- **TEST-G2 (Medium):** no test for the **CreateCheckout failure path** (DB-2) — Monobank throws → order state. → TASK-008.
- **TEST-G3 (Medium):** no **concurrent duplicate webhook** test (DB-4). → TASK-010's regression.
- **TEST-G4 (Medium):** no direct **trailing-slash/case** test on the device-middleware match (defense-in-depth for the refuted bypass). → TASK-023.
- **Structural:** unit tier uses `UseInMemoryDatabase` (no constraints/locks) — mitigated by the real-Postgres integration tests, but new concurrency logic added at the unit tier would pass falsely.

**Frontend testing is the weakest area:** admin has **one** test file (`admin/src/lib/utils.test.ts`, pure helpers only); mobile has **none** (typecheck only). The client that initiates real payments has no behavioral tests. → TASK-047.

---

## 11. Positive Findings (evidence-backed strengths — preserve these)
- **Deny-by-default authorization** with a test that *pins the entire anonymous surface* and fails CI on any new anonymous endpoint.
- **Refresh-token reuse detection** with whole-family revocation; tokens stored hashed.
- **Real-money race protection** proven by barrier-based, cross-process concurrency integration tests against real Postgres.
- **Correct refund ordering** (persist intent before external call) — the model CreateCheckout should copy.
- **Runtime infra hardening**: digest-pinned images, SHA-pinned actions, least-privilege CI token, localhost-bound DB/Redis, non-root backend, strong Caddy CSP/headers.
- **Mobile accessibility-first design system** with a required-label icon button and enforced import boundaries.
- **Disciplined error handling** and clean C# nullability across the backend.
- A thoughtful **`.gitleaks.toml`** written specifically to catch what the default ruleset missed.

---

## 12. Requires Verification (could not be established from static inspection alone)

| # | What was observed | Why it may matter | How to verify safely |
|---|---|---|---|
| 1 | `.gitleaks.toml:150-158` states commit `3cb50dc` contained a **real 34-char `Monobank:Token`** (not a placeholder). | If the live acquiring token was never rotated, it may be recoverable from git history — direct financial risk. **Highest-priority item.** | Confirm out-of-band with Monobank that the token was rotated after that commit. Run `gitleaks detect` over full history. |
| 2 | `deploy/.env` untracked *now*; full history not scanned here. | A live JWT/DB/Redis secret could have been committed and later removed. | `gitleaks detect --log-opts="--all"` over the whole history. |
| 3 | secp256k1 webhook verification relies on Linux OpenSSL (`AsymmetricSignatureVerifier.cs:13-14`). | A silently-failing verifier would break fulfillment (fails closed — good) but must be proven to *accept* real signatures on the prod image. | Replay a real Monobank-signed webhook against the prod container; confirm valid→200, invalid→401. |
| 4 | `TokenVersion` write/bump path not located in this pass. | If banning a user doesn't bump `TokenVersion`, SEC-1+SEC-2 combine into "ban ineffective until token expiry". | Runtime: ban a user, confirm `TokenVersion` increments and existing access tokens are rejected. |
| 5 | DB-1 reachability. | Whether the advisory-lock + CAS fully covers double-allocation in practice, incl. admin restore/unblock racing the job. | Concurrency/load test: two orders + one voucher; admin unblock during fulfillment. Also `SELECT voucher_id,count(*) FROM fulfillments GROUP BY 1 HAVING count(*)>1` before adding the unique index. |
| 6 | DB-4: does Monobank deliver genuinely *overlapping* duplicate webhooks (vs sequential retries)? | Determines whether the outbox race is reachable. | Inspect Monobank retry semantics / prod logs for concurrent deliveries. |
| 7 | Dependency CVE status. | Cannot assess from versions alone. | `npm audit --omit=dev` in admin/website/mobile; `dotnet list package --vulnerable --include-transitive`. Verify the two OpenTelemetry pre-release packages and mobile `lucide-react` usage. |
| 8 | Unbounded-query cost (DB-7). | Real table-scan impact at prod data volume. | `EXPLAIN ANALYZE` the admin list queries against prod-sized data. |

---

## 13. Prioritized Baby-Task Backlog

> Format per task. Effort: XS ≈ 10-30m · S ≈ 30-90m · M ≈ 1-4h · L > 4h. "Baby steps" are the smallest safe units; atomic security/money fixes are **not** split into unsafe fragments.

### P0 — Immediate integrity / verification

#### TASK-000 — Verify the historical Monobank token was rotated
- **Priority:** P0 · **Category:** Security · **Severity:** Critical (if unrotated) · **Confidence:** Confirmed (exposure existed) · **Effort:** XS · **Dependencies:** none
- **Target:** `.gitleaks.toml:150-158`; Monobank merchant dashboard (external)
- **Evidence:** gitleaks config documents a real 34-char `Monobank:Token` in commit `3cb50dc`.
- **Problem/Risk:** A live acquiring token in git history could let a third party create/cancel invoices against the merchant account.
- **Baby steps:** 1) In the Monobank dashboard, check the token's issue date vs commit `3cb50dc`. 2) If not rotated since, rotate it now. 3) Update `MONOBANK_TOKEN` in `deploy/.env` and redeploy. 4) Run `gitleaks detect --log-opts="--all"` to confirm no other live secret in history.
- **Verification:** New token in prod; payments still work; old token rejected by Monobank.
- **Regression protection:** Wire gitleaks into CI (TASK-016).
- **Expected benefit:** Closes the single highest financial-risk uncertainty.
- **Do not change:** Any working payment config beyond the token value.

#### TASK-002 — Reconcile the two `FulfillmentService` copies (port fixes to JobsWorker)
- **Priority:** P0 · **Category:** Security/Correctness · **Severity:** High · **Confidence:** Confirmed · **Effort:** S (port) / M (extract shared) · **Dependencies:** none
- **Target files:** `FuelFlow.JobsWorker/Services/FulfillmentService.cs:491-497,573-576`; ref `backend/src/FuelFlow.API/BackgroundJobs/FulfillmentService.cs:513-524,611-628`
- **Evidence:** JobsWorker uses pre-fix `payload.Contains(orderIdString)` and has the expiry filter commented out with a TODO.
- **Problem/Risk:** If the JobsWorker is ever the deployed fulfiller, it can (a) mis-match order ids embedded in other payloads and (b) hand out the most-expired vouchers first.
- **Recommended change:** Immediately port the jsonb-containment guard and the expiry filter into the JobsWorker copy. Then (separate step) extract one shared implementation behind interfaces for the injected deps.
- **Baby steps:** 1) Copy the jsonb-containment guard into the worker's duplicate check. 2) Uncomment/port the expiry predicate. 3) Add a JobsWorker unit test proving expired vouchers are skipped. 4) *(follow-up)* extract shared `FulfillmentService`.
- **Verification:** JobsWorker unit tests assert expired-voucher exclusion and correct payload matching; both copies produce identical fulfillment for the same fixture.
- **Regression protection:** The new expiry-exclusion test (step 3).
- **Expected benefit:** Removes a latent correctness/money bug and stops future drift.
- **Do not change:** The API copy's already-correct logic; advisory-lock/transaction semantics.

#### TASK-001 — Persist the order before calling Monobank in `CreateCheckout`
- **Priority:** P0 · **Category:** Correctness/Payment · **Severity:** High · **Confidence:** Confirmed · **Effort:** M · **Dependencies:** none (pairs with TASK-008)
- **Target:** `Features/Orders/CreateCheckout/CreateCheckoutCommandHandler.cs:156,171-192`
- **Evidence:** `Orders.Add` → `CreateInvoiceAsync` → single `SaveChangesAsync`; invoice exception swallowed (:184-190).
- **Problem/Risk:** A crash between a successful Monobank call and `SaveChanges` leaves a payable invoice with **no order row** (webhook → `order_not_found`); a failed call persists a dead order with null `PaymentUrl`.
- **Recommended change:** Mirror `RefundOrderCommandHandler`'s ordering — `SaveChanges` the order as `PendingPayment` first, then call Monobank, then a second `SaveChanges` to attach invoice id/url. On invoice failure, surface a retryable error (or enqueue via outbox) rather than silently returning 200.
- **Baby steps:** 1) Split into two saves around the Monobank call. 2) Decide failure behavior (retryable 502 vs outbox). 3) Add alerting on the `MonobankInvoiceFailed` metric. 4) Add the regression test (TASK-008).
- **Verification:** Simulated crash/failure leaves no orphaned invoice and no unpayable-but-silent order; happy path unchanged.
- **Regression protection:** TASK-008.
- **Do not change:** Idempotency-key dedup behavior; the kopecks conversion.

### P1 — High security / correctness

#### TASK-003 — Reject banned/deleted users on refresh
- **Priority:** P1 · **Category:** Security · **Severity:** Medium · **Confidence:** Confirmed · **Effort:** XS · **Dependencies:** none (pairs TASK-007)
- **Target:** `Features/Auth/Refresh/RefreshTokenCommand.cs:95-99`
- **Evidence:** Guard checks only `!User.IsActive`.
- **Problem/Risk:** Banned/soft-deleted user with a live refresh token keeps minting access tokens; `/api/auth/refresh` is anonymous so it skips the live ban check.
- **Recommended change:** Extend the guard to `|| User.IsBanned || User.IsDeleted` and revoke the family.
- **Baby steps:** 1) Add the conditions. 2) Revoke token family on rejection. 3) Add TASK-007 test.
- **Verification:** Banned & soft-deleted users get 401 on refresh; normal refresh works.
- **Regression protection:** TASK-007.

#### TASK-004 — Fail closed on missing/unparseable `token_version`
- **Priority:** P1 · **Category:** Security · **Severity:** Medium · **Confidence:** Confirmed · **Effort:** XS · **Dependencies:** none (bundle with TASK-003)
- **Target:** `Middleware/SessionValidationMiddleware.cs:46-53`
- **Evidence:** `int.TryParse` false → condition short-circuits to allow.
- **Recommended change:** Treat missing/unparseable as version 0 (never equal to a real `TokenVersion ≥ 1`) → reject once bumped.
- **Baby steps:** 1) Default parse failure to a sentinel that forces mismatch. 2) Add a middleware unit test (no claim + bumped version → 401).
- **Verification:** Token without `token_version` is rejected after a bump.
- **Regression protection:** The new middleware test.
- **Do not change:** The documented "inactive users keep non-payment access" behavior.

#### TASK-005 — Add partial unique index on `fulfillments(voucher_id)`
- **Priority:** P1 · **Category:** Data integrity · **Severity:** High · **Confidence:** Confirmed · **Effort:** S · **Dependencies:** verification §12-5
- **Target:** `Features/Orders/Configurations/FulfillmentConfiguration.cs:31-32` + new migration
- **Evidence:** Both indexes non-unique; admin restore/unblock bypass the CAS.
- **Recommended change:** `CREATE UNIQUE INDEX CONCURRENTLY` on `fulfillments(voucher_id)` (partial if soft-deletes apply) so double-allocation fails loudly.
- **Baby steps:** 1) Run the dupe check (§12-5). 2) If clean, add the migration. 3) Confirm the fulfillment happy path still inserts one row. 4) *(follow-up)* route admin status changes through the guarded claim.
- **Verification:** Attempted second fulfillment of the same voucher throws a unique violation; existing data has no dupes.
- **Regression protection:** An integration test asserting the constraint rejects a duplicate.
- **Do not change:** The advisory-lock/CAS path (this is a backstop, not a replacement).

#### TASK-006 — Fix mojibake in the admin signature modal
- **Priority:** P1 · **Category:** UX/Correctness · **Severity:** Medium · **Confidence:** Confirmed · **Effort:** XS · **Dependencies:** none
- **Target:** `admin/src/pages/admin.tsx:1967,1977`
- **Evidence:** Corrupted bytes rendered instead of `t(...)` Ukrainian strings.
- **Recommended change:** Replace literals with i18n keys; re-save the file as UTF-8.
- **Verification:** Modal shows correct Ukrainian; no other mojibake in the file.
- **Regression protection:** grep for the mojibake byte pattern in CI lint (optional).

#### TASK-007 — Regression test: banned/deleted user cannot refresh
- **Priority:** P1 · **Category:** Testing · **Severity:** High · **Confidence:** Confirmed · **Effort:** S · **Dependencies:** TASK-003
- **Target:** `backend/tests/FuelFlow.UnitTests/Auth/` (or IntegrationTests)
- **Verification:** Test fails before TASK-003, passes after.

#### TASK-008 — Regression test: CreateCheckout failure path
- **Priority:** P1 · **Category:** Testing · **Severity:** Medium · **Confidence:** Confirmed · **Effort:** S · **Dependencies:** TASK-001
- **Target:** `backend/tests/FuelFlow.UnitTests/Orders/`
- **Recommended:** Monobank client throws → assert order state, no orphaned invoice, `MonobankInvoiceFailed` metric emitted.

#### TASK-009 — Add optimistic concurrency token (`xmin`) to `Order`
- **Priority:** P1 · **Category:** Data integrity · **Severity:** Medium · **Confidence:** Confirmed · **Effort:** S · **Dependencies:** none
- **Target:** `Features/Orders/Configurations/OrderConfiguration.cs` (extend to `Refund`, `FuelVoucher` after)
- **Recommended change:** `b.Property<uint>("xmin").IsRowVersion().HasColumnName("xmin")` (config-only, no schema change). Handle `DbUpdateConcurrencyException` where admin edits race the jobs.
- **Verification:** Concurrent update of the same order raises a detectable conflict instead of a silent lost update.
- **Do not change:** The advisory-lock hot path (already safe).

#### TASK-010 — Backstop Monobank webhook idempotency under concurrency
- **Priority:** P1 · **Category:** Data integrity · **Severity:** Medium · **Confidence:** Likely · **Effort:** S · **Dependencies:** §12-6
- **Target:** `Features/Orders/Configurations/OutboxEventConfiguration.cs:41-42`; `Features/Monobank/ProcessWebhook/ProcessMonobankWebhookCommandHandler.cs:53-183`
- **Recommended change:** Add a partial unique index keyed by event type + orderId, **or** take a per-order `pg_advisory_xact_lock` in the webhook handler (as fulfillment does). Include a concurrent-delivery integration test.
- **Verification:** Two overlapping deliveries produce exactly one transition + one enqueue.

#### TASK-011 — Enforce `Monobank:Enabled=true` at startup in Production
- **Priority:** P1 · **Category:** Security/Config · **Severity:** Low → guards a High failure mode · **Confidence:** Likely · **Effort:** XS · **Dependencies:** none
- **Target:** `Program.cs` `ValidateSecurityConfiguration` (~:187-198)
- **Recommended change:** Refuse to boot in Production if Monobank is disabled, so the unsigned-webhook + mock-client path can't be reached by config drift.
- **Verification:** Prod boot with `Monobank:Enabled=false` throws at startup.

#### TASK-012 — Add soft-delete query filter to `User`
- **Priority:** P1 · **Category:** Data/Security · **Severity:** Medium · **Confidence:** Confirmed · **Effort:** S · **Dependencies:** none
- **Target:** `UserConfiguration.cs`
- **Recommended change:** `HasQueryFilter(e => !e.IsDeleted)`, then audit that intended admin queries add `IgnoreQueryFilters()`.
- **Verification:** Deleted users no longer appear in queries that lack an explicit bypass; admin views that need them still work.
- **Do not change:** Any query that legitimately needs deleted rows (add the bypass explicitly).

### P2 — Reliability & comprehension

#### TASK-013 — Paginate admin list queries (start with `GetAdminUsers`)
- **Priority:** P2 · **Category:** Performance · **Severity:** Medium · **Confidence:** Confirmed · **Effort:** M · **Dependencies:** none
- **Target:** `Features/Admin/.../GetAdminUsersQueryHandler.cs:13-33`, `GetAdminOrders...:21-28`, `GetAdminPurchases...:43-51`
- **Recommended change:** Push filters/search into the query; add `Skip/Take` using `Persistence/Pagination.cs` (template: `GetAdminVouchers`).
- **Verification:** Endpoints return a page + total count; DB does the filtering (check generated SQL).

#### TASK-014 — Decouple the reconciliation sweep from the payment webhook
- **Priority:** P2 · **Category:** Performance · **Severity:** Low · **Confidence:** Likely · **Effort:** S · **Dependencies:** TASK-002
- **Target:** `BackgroundJobs/FulfillmentService.cs:68-69,228-235`; enqueue at `ProcessMonobankWebhookCommandHandler.cs:206-210`
- **Recommended change:** Move `FixMismatchedFulfillmentsAsync`/backfill to a low-frequency Hangfire recurring job; keep the webhook path to just the paid order.

#### TASK-035 — Deduplicate the crypto verifier
- **Priority:** P2 · **Category:** DRY/Security · **Severity:** Medium · **Confidence:** Confirmed · **Effort:** S · **Dependencies:** none
- **Target:** `Features/Auth/VerifyChallenge/VerifyChallengeCommandHandler.cs:175-282`
- **Recommended change:** Inject `IAsymmetricSignatureVerifier`; if debug traces are needed, add a tracing overload rather than forking the algorithm.
- **Verification:** Existing `AsymmetricSignatureVerifierTests` cover the shared path; VerifyChallenge tests still pass.

#### TASK-036 — Decompose `AssignVouchersToOrderAsync`
- **Priority:** P2 · **Category:** Readability · **Severity:** Medium · **Confidence:** Confirmed · **Effort:** M · **Dependencies:** TASK-002
- **Target:** `BackgroundJobs/FulfillmentService.cs:331-603`
- **Recommended change:** Extract `MarkOutboxProcessedAsync(...)` (the 4× repeated block) and split resolve/assign/finalize phases. Behavior-preserving.
- **Verification:** Existing concurrency + fulfillment integration tests still green.
- **Do not change:** Transaction boundaries, advisory-lock scope, CAS semantics.

#### TASK-023 — Trailing-slash/case theory test for device middleware
- **Priority:** P2 · **Category:** Testing · **Severity:** Low · **Confidence:** Confirmed · **Effort:** XS · **Dependencies:** none
- **Target:** `backend/tests/FuelFlow.UnitTests/Middleware/MiddlewareTests.cs`
- **Recommended:** `[Theory]` over `/api/purchases/`, cased, and wildcard variants → all enforced.

### P3 — UX, accessibility & performance

#### TASK-041 — Replace admin hand-rolled dialogs with Radix Dialog/AlertDialog
- **Priority:** P3 · **Category:** UX/Accessibility · **Severity:** High (a11y) · **Confidence:** Confirmed · **Effort:** M · **Dependencies:** none
- **Target:** `admin/src/pages/admin.tsx:1940,1963,1982,2006,2030,2056` (+ `window.confirm` at :690)
- **Recommended change:** Use `@radix-ui/react-dialog` (destructive → `AlertDialog`) for focus trap/restore, Escape, scrim, ARIA.
- **Verification:** Keyboard can open/escape each dialog; focus is trapped then restored; SR announces the dialog.

#### TASK-042 — Extract admin tab bodies into lazy-loaded components
- **Priority:** P3 · **Category:** Readability/Performance · **Severity:** High (maintainability) · **Confidence:** Confirmed · **Effort:** L (split into per-tab sub-tasks) · **Dependencies:** none
- **Target:** `admin/src/pages/admin.tsx` (10 `activeTab ===` blocks)
- **Recommended change:** One component per tab (following `ProvidersTab`), colocating that tab's state; `React.lazy` each. Do one tab per PR.
- **Verification:** Each tab renders identically; bundle splits per tab.
- **Do not change:** Behavior of each tab during extraction.

#### TASK-043 — Route-level code-splitting for the admin shell
- **Priority:** P3 · **Category:** Performance · **Severity:** Medium · **Confidence:** Confirmed · **Effort:** S · **Dependencies:** none
- **Target:** `admin/src/App.tsx:3`
- **Recommended:** `React.lazy` the post-login shell so login is a tiny first paint.

#### TASK-044 — Fix admin login form + label/checkbox semantics
- **Priority:** P3 · **Category:** Accessibility · **Severity:** Medium · **Confidence:** Confirmed · **Effort:** S · **Dependencies:** none
- **Target:** `admin/src/pages/admin.tsx:552-586,1063,1080,1107`
- **Recommended:** Wrap login in `<form onSubmit>`, `id`+`<label htmlFor>`, `type="tel"`/`autoComplete`; real `<input type=checkbox>`; `aria-sort` on sorted headers.

#### TASK-045 — Standardize admin loading/error/empty states
- **Priority:** P3 · **Category:** UX · **Severity:** Medium · **Confidence:** Confirmed · **Effort:** M · **Dependencies:** none
- **Target:** users/purchases tabs in `admin.tsx`; mirror mobile's `LoadingState`/`ErrorState`/`EmptyState`.
- **Recommended:** Handle `isLoading`/`isError` on every query with a retry affordance.

#### TASK-046 — Route mobile screens through the design-system primitives
- **Priority:** P3 · **Category:** Accessibility · **Severity:** Medium · **Confidence:** Confirmed · **Effort:** M · **Dependencies:** none
- **Target:** `mobile/app/company.tsx` (esp. icon-only close :492), `contracts.tsx`, `basket.tsx`, `my-codes.tsx`, `checkout.tsx`, `profile.tsx`
- **Recommended:** Replace raw `Pressable`/`TouchableOpacity` with `Button`/`IconButton`/`Chip` (labels become non-optional).

### P4 — Cleanup & architecture

#### TASK-029 — Extract `ProvidersController` writes into command handlers
- **Priority:** P4 · **Category:** Architecture · **Severity:** Medium · **Confidence:** Confirmed · **Effort:** M · **Dependencies:** none
- **Target:** `Features/Providers/ProvidersController.cs:60-314`
- **Recommended:** Mirror the existing `Features/Stations/` handlers. No new abstraction.
- **Verification:** Behavior identical; controller no longer injects `ApplicationDbContext`.

#### TASK-030 — Assign single ownership of Station/FuelType/FuelPackage mutations
- **Priority:** P4 · **Category:** Architecture · **Severity:** Medium · **Confidence:** Confirmed · **Effort:** M · **Dependencies:** TASK-029
- **Recommended:** Stations owns writes; Providers becomes a read projection. One-time decision.

#### TASK-031 — (Conditional) Split `FixMismatchedFulfillmentsAsync` out of `FulfillmentService`
- **Priority:** P4 · **Category:** Architecture · **Severity:** Medium · **Confidence:** Confirmed · **Effort:** M · **Dependencies:** TASK-002, TASK-036
- **Target:** `BackgroundJobs/FulfillmentService.cs:72-226`
- **Evidence:** God service with 6 responsibilities (see ARC-3).
- **Problem/Risk:** High responsibility count in live payment code; do **not** refactor speculatively.
- **Recommended change:** Only if already working in this file — extract the self-contained `FixMismatchedFulfillmentsAsync` repair pass as the lowest-risk seam. Otherwise leave it; the risk of breaking live Monobank fulfillment outweighs the tidiness gain.
- **Verification:** Concurrency + fulfillment integration tests remain green.
- **Do not change:** Transaction/advisory-lock semantics of the main assignment path.

#### TASK-032 — Move `ComputeFulfilledValueKopecks` off the command handler
- **Priority:** P4 · **Category:** Architecture · **Severity:** Low · **Confidence:** Confirmed · **Effort:** XS
- **Target:** `Orders/RefundOrder/RefundOrderCommandHandler.cs:305` → `Orders/SharedModels/Order.cs`
- **Verification:** Report/Reconciliation compile against the shared model; values unchanged.

#### TASK-033 — Relocate `ProviderEventService` to `SharedKernel/Audit`
- **Priority:** P4 · **Category:** Architecture · **Severity:** Low · **Confidence:** Likely · **Effort:** S · **Dependencies:** none
- **Recommended:** Move + rename (its entity already lives in `SharedKernel/Domain`); removes 6 slices' dependency on the Providers feature.

#### TASK-034 — Rename read `*Command` → `*Query`
- **Priority:** P4 · **Category:** Cleanup · **Severity:** Low · **Confidence:** Confirmed · **Effort:** XS · **Dependencies:** none
- **Target:** `Sync/GetSync`, `Vouchers/GetInventory`, `Orders/GetUserPurchases`, `Vouchers/GetUserVouchers`. Opportunistic (CRLF makes renames look large).

#### TASK-037 — Log swallowed exceptions in PDF parsers
- **Priority:** P4 · **Category:** Cleanup/Observability · **Severity:** Low · **Confidence:** Confirmed · **Effort:** XS
- **Target:** `KloVoucherParser.cs:76-78`, `WogVoucherParser.cs:69,108-110`, `OkkoVoucherParser.cs:68`

#### TASK-038 — Type the JS API-boundary DTOs
- **Priority:** P4 · **Category:** Type Safety · **Severity:** Low · **Confidence:** Confirmed · **Effort:** S
- **Target:** `mobile/src/features/vouchers/api/getVouchers.ts:14,68,84`; admin report/reconciliation rows.
- **Recommended:** Explicit API-DTO types or a schema (zod/valibot) at the seam.

#### TASK-039 — Name Monobank magic numbers
- **Priority:** P4 · **Category:** Cleanup · **Severity:** Low · **Confidence:** Confirmed · **Effort:** XS
- **Target:** `MonobankClient.cs:39,47` (`UahIso4217=980`, `InvoiceValiditySeconds=3600`).

#### TASK-040 — Delete the empty `_payload/` directory
- **Priority:** P4 · **Category:** Cleanup · **Severity:** Low · **Confidence:** Confirmed · **Effort:** XS · **Dependencies:** confirm truly unused

#### TASK-024 — (Optional) HMAC the OTP hash
- **Priority:** P4 · **Category:** Security hardening · **Severity:** Low · **Confidence:** Confirmed · **Effort:** S
- **Target:** `SharedKernel/Security/SecretsHasher.cs`. Keyed HMAC so a bare table leak isn't offline-reversible. Keep refresh-token hashing as-is.

#### TASK-025 — Harden/doc the `RequireSignatureForEndpoints` binder foot-gun
- **Priority:** P4 · **Category:** Config · **Severity:** Low · **Confidence:** Confirmed · **Effort:** XS
- **Target:** `DeviceAuthOptions.cs:30-34`. Clear the list before binding, or document the append behavior at the config site.

### CI / Infrastructure

#### TASK-015 — Run backend + JS tests in CI
- **Priority:** P1 (pipeline safety) · **Category:** Infrastructure/Testing · **Severity:** High · **Confidence:** Confirmed · **Effort:** S · **Dependencies:** none
- **Target:** `.github/workflows/ci.yml`
- **Recommended:** Add `dotnet test` (needs Docker for Testcontainers on the runner) and the admin `vitest` script; gate `deploy` on them.
- **Verification:** A failing test blocks the deploy job.

#### TASK-016 — Run gitleaks in CI
- **Priority:** P1 · **Category:** Infrastructure/Security · **Severity:** High · **Confidence:** Confirmed · **Effort:** XS · **Dependencies:** none
- **Recommended:** Add a gitleaks job using the existing `.gitleaks.toml`; fail on findings.

#### TASK-017 — Add dependency vulnerability scanning to CI
- **Priority:** P1 · **Category:** Infrastructure/Supply-chain · **Severity:** High · **Confidence:** Confirmed · **Effort:** S
- **Recommended:** `dotnet list package --vulnerable --include-transitive` (fail on results) + `npm audit` per JS app. (Covers §12-7.)

#### TASK-019 — Gate prod deploy behind an approval environment + branch protection
- **Priority:** P1 · **Category:** Infrastructure/Security · **Severity:** Medium · **Confidence:** Confirmed · **Effort:** S
- **Target:** `.github/workflows/ci.yml:121-145` + GitHub settings
- **Recommended:** GitHub `environment` with required reviewer; branch protection + required PR review on `main`; consider a non-root deploy user instead of `/root`.
- **Do not change:** The existing `push`+`main` guard (keep it).

#### TASK-020 — Enable .NET lock files
- **Priority:** P2 · **Category:** Infrastructure/Supply-chain · **Severity:** Medium · **Confidence:** Confirmed · **Effort:** S
- **Recommended:** `<RestorePackagesWithLockFile>true</RestorePackagesWithLockFile>`, commit `packages.lock.json`, restore `--locked-mode`.

#### TASK-021 — Digest-pin prod compose base images
- **Priority:** P2 · **Category:** Infrastructure · **Severity:** Medium · **Confidence:** Confirmed · **Effort:** XS
- **Target:** `deploy/docker-compose.prod.yml:51,219,241` → `@sha256:` (match root compose).

#### TASK-026 — Default dev compose to `Monobank__Enabled=false`
- **Priority:** P3 · **Category:** Infrastructure · **Severity:** Low/Medium · **Confidence:** Confirmed · **Effort:** XS
- **Target:** `docker-compose.yml:42-49`.

#### TASK-027 — Add `deploy/.env.example` for the prod variable surface
- **Priority:** P4 · **Category:** Infrastructure/Docs · **Severity:** Low · **Confidence:** Confirmed · **Effort:** XS

#### TASK-028 — (Optional) nginx-unprivileged images + edge auth throttle
- **Priority:** P4 · **Category:** Infrastructure · **Severity:** Low · **Confidence:** Confirmed · **Effort:** S

#### TASK-047 — Establish frontend behavioral tests
- **Priority:** P4 · **Category:** Testing · **Severity:** Medium · **Confidence:** Confirmed · **Effort:** L (incremental)
- **Recommended:** Start with admin auth flow + one data-mutation screen (Testing Library); add a mobile test runner (Jest + RN Testing Library) covering checkout/voucher display. Do incrementally.

---

## 14. Dependency-Aware Execution Plan

```
Phase A — Verify & contain (do first, mostly out-of-band)
   TASK-000  Rotate/verify Monobank token          (blocks nothing, unblocks peace of mind)
   §12 items 3,4,5  runtime verification

Phase B — Payment/session integrity fixes
   TASK-002  Port FulfillmentService fixes to worker
   TASK-001  CreateCheckout ordering        →  TASK-008 (regression test)
   TASK-003  Refresh ban/deleted check      →  TASK-007 (regression test)
   TASK-004  token_version fail-closed       (bundle with TASK-003)
   TASK-005  fulfillments unique index       (after §12-5 dupe check)
   TASK-009  xmin concurrency token
   TASK-010  webhook idempotency backstop
   TASK-011  Monobank:Enabled prod guard
   TASK-012  User soft-delete filter

Phase C — Pipeline safety (parallel with B; protects everything after)
   TASK-016  gitleaks in CI
   TASK-015  tests in CI
   TASK-017  dependency scanning        →  resolves §12-7
   TASK-019  deploy approval gate
   TASK-020  .NET lock files
   TASK-021  digest-pin prod images

Phase D — Reliability & comprehension
   TASK-035  dedupe crypto verifier
   TASK-013  paginate admin queries
   TASK-002 → TASK-036  decompose AssignVouchers (after the port)
   TASK-014  decouple reconciliation sweep
   TASK-023  device-middleware theory test

Phase E — UX / accessibility
   TASK-006  mojibake fix (quick, can do anytime)
   TASK-041  Radix dialogs
   TASK-044  login form/labels
   TASK-045  loading/error/empty states
   TASK-042 → TASK-043  split + lazy-load admin (per-tab)
   TASK-046  mobile primitives

Phase F — Architecture & cleanup (last; after security paths stabilize)
   TASK-029 → TASK-030  Providers/Stations ownership
   TASK-031  (conditional) split FixMismatchedFulfillments  (after TASK-002/036)
   TASK-032, TASK-033, TASK-034   boundary tidy-ups
   TASK-037, TASK-038, TASK-039, TASK-040, TASK-024, TASK-025, TASK-026, TASK-027, TASK-028
   TASK-047  frontend tests (incremental, ongoing)
```

Rationale for ordering: verification and payment/session integrity come first because they concern live money and access. Pipeline safety (Phase C) is pulled early because it protects every subsequent change. **Cleanup and architecture (Phase F) come last** — deliberately, so a refactor never obscures or races an in-flight security/money fix (e.g. TASK-036 waits for TASK-002; TASK-040/`_payload` deletion waits until nothing depends on it).

---

## 15. Audit Limitations

This was a static, read-only inspection at a single commit. It could **not** establish: runtime configuration and actual env values in production; whether the historical Monobank token was rotated (§12-1); real dependency CVE status (no tooling run — §12-7); whether the voucher double-allocation or webhook races are reachable under real concurrency (§12-5,6); query plans at production data volume (§12-8); browser/device performance (all perf findings are code-pattern, not measured); and full git-history secret scanning. Line numbers reflect inspection time. No source files were modified. The task backlog describes recommended changes and their verification criteria; it does not itself execute or prove them.



