# Refuted hypotheses

The brief required that every finding survive my own attempt to refute it. This section records the attacks that **failed** — hypotheses I pursued and could not make work, because a guard was genuinely there.

It is here for three reasons. It stops the same ground being re-litigated in the second audit pass. It names the controls that are load-bearing, so a future refactor knows what it is standing on. And a report that lists only what is broken gives a false picture of a codebase whose money paths are, in most respects, carefully built.

---

## Money

| Hypothesis | Why it failed | Guard |
|---|---|---|
| Client-controlled checkout price — patch the app, buy a 2,000 ₴ voucher for 1 ₴ | The server never reads a price from the request. It looks up `FuelPackages` and recomputes | `ServerPricing.cs`. `FRAUD_ANALYSIS.md:30` WP-1 CONFIRMED |
| Forge a Monobank "paid" webhook | ECDSA secp256k1 / SHA-256 verified over the **raw** body. Production refuses to boot if the public key is still a placeholder, so verification cannot be silently disabled | `ProcessMonobankWebhookCommandHandler.cs`, `Program.cs:177-185` |
| Replay a legitimate webhook to double-fulfil | Terminal states are enforced — `Fulfilled` and `Cancelled` cannot be re-entered from a webhook | `OrderStateMachine`, per `FRAUD_ANALYSIS.md:232-238`. (The *simulate* endpoint bypassed this — [FF-06](02-findings-critical-high.md#ff-06--apipurchasessimulate-production-reachable-and-bypassed-the-state-machine--high-confirmed-fixed) — but the webhook path holds) |
| Tamper with the amount in a webhook body | Any change invalidates the signature; the amount is also reconciled against the order | Same handler |
| Over-refund — claim more than was paid | `ComputeRefundAmountKopecks(order)` derives the refundable balance server-side and `Math.Min` caps the request against it | `RefundOrderCommandHandler.cs:109,115` |
| Double-refund — call refund twice | An existing refund row in any state other than `Failed` blocks a second attempt | `RefundOrderCommandHandler.cs:62-64,68` |
| Negative-amount refund — refund `-500` to *add* money | `if (amount <= 0)` → `"NothingToRefund"` | `RefundOrderCommandHandler.cs:117-126` |
| Race two concurrent refunds through the existing-refund check | A unique index on `OrderId` makes the second insert fail; the handler catches `DbUpdateException` and reports the winning refund instead of a 500 | `RefundOrderCommandHandler.cs:175-198` |
| Refund without admin rights | Controller-level role requirement | `AdminOrderController.cs:12-14`, endpoint `:67-68` |

The refund flow was a previously admitted coverage gap and is the single best-defended surface I examined — five independent guards, including one that handles the concurrent case correctly rather than by hoping.

## Vouchers

| Hypothesis | Why it failed | Guard |
|---|---|---|
| Two orders assigned the same voucher | An advisory lock plus atomic conditional `UPDATE ... WHERE status = 'Available'` — the second update affects 0 rows and the code handles that | `FulfillmentService.cs:474-481` |
| Mint duplicate vouchers via import | Unique indexes on `VoucherNumber` and `QrPayload` | Schema |
| Race `mark-used` to redeem twice | WP-4 is genuinely unimplemented (read-then-write, `MarkVoucherAsUsedCommandHandler.cs:20-56`) — but the outcome is **benign**: an already-`Used` voucher returns success, so the result is idempotent, no money moves, and the worst case is a redundant write. Authorization is also correct (`WorkerUserId`, then `AssignedToUserId`) | Not a guard so much as an absence of consequence — recorded honestly as such |
| Admin voucher changes go unaudited | `RecordEventAsync` in 5 handlers covering every state-changing path. There is no separate assign endpoint — assignment flows through the audited `PUT /{id}` and bulk-action | `BulkActionVouchersCommandHandler.cs:41,94`, `DeleteVoucherCommandHandler.cs:36`, `Import/VouchersController.cs:84`, `UnblockVoucherCommand.cs:60`, `UpdateVoucherCommandHandler.cs:59` |

## Authentication and tenancy

| Hypothesis | Why it failed | Guard |
|---|---|---|
| Buy against another company by supplying its `LegalEntityId` | Membership is verified server-side against the authenticated principal; the client-supplied id cannot widen access | Checkout handler |
| CSRF the refresh endpoint (the cookie is `SameSite=None`) | An Origin allow-list rejects cross-origin refresh attempts. Strengthened indirectly by [FF-08](02-findings-critical-high.md#ff-08--api-published-on-the-admin-origin--high-confirmed-fixed), which removed the second origin serving the same API | `AuthController.cs:127-128` |
| CSRF the Hangfire dashboard (`IgnoreAntiforgeryToken = true`) | The dashboard does not use cookie authentication, so there is no ambient credential for a cross-site request to ride. The flag is safe **given** that — and `Program.cs:99-104` no longer permits the unauthenticated bypass in Production | `HangfireDashboardAuthorizationFilter`, `Program.cs:193-200` |
| Reuse a rotated refresh token | Rotation with family revocation — replaying an old token invalidates the family | `RefreshTokenCommand.cs`. `TODO.md:81` CONFIRMED |
| Boot production with the burned placeholder JWT secret | Startup refusal on the exact placeholder constant | `AuthSetup.cs:13,31-42` |

## Secrets

| Hypothesis | Why it failed |
|---|---|
| **A live Supabase database password is in git history** (`REMEDIATION_PRIORITIES.md` P0 F4, `FRAUD_ANALYSIS.md:22`) | **REFUTED.** The three cited commits do not exist in this repository. A sweep of 2,622 text blobs found 23 distinct postgres-URI credentials and **every one is a placeholder** — `postgres`, `password`, `PASSWORD` (paired with username `USER` in `render.yaml`), a 4-char entropy-0.0 masked log value, and the F4 "password" itself, which resolves to the literal `***REDACTED***`. Full detail in [04-doc-claim-verification.md](04-doc-claim-verification.md) |
| A live JWT signing secret is in `3cb50dc:appsettings.Production.json` | **REFUTED — and this corrects my own earlier over-report.** That file is a template. The 71-char `Jwt:Secret` is exactly the constant `AuthSetup.cs:13` names `PlaceholderSecret`, which Production refuses to boot with. Burned, not exploitable |
| A live Twilio credential is in git history | **False positive.** The match was a `process.env` reference, not a value |
| `Session:Secret` at `72492dc` is a live secret | Belongs to the retired admin-panel backend — no longer deployed, and the component that consumed it no longer exists |
| Secrets hide in objects orphaned by the past history rewrite | `git fsck --unreachable --dangling` surfaced 10 unreachable blobs; 2 flagged, both abandoned Azure CDKTF code. Read with values masked, the "secrets" were env-var *names* passed to `GetRequiredEnv(...)`, Key Vault secret-*name* constants (lines 82/86), and `AdministratorPassword = config.PostgresAdminPassword` — a config reference. **No secret values.** Caveat: pre-rewrite objects may have been gc'd and are unrecoverable — see [07-coverage-statement.md](07-coverage-statement.md) |

## Infrastructure

| Hypothesis | Why it failed | Guard |
|---|---|---|
| `/health` is a static 200 that would report healthy with a dead database | It actually probes — `GetPendingMigrationsAsync()`, returning `Results.Problem("Database unreachable", 503)` on failure | `PipelineSetup.cs`. `TODO.md:35` CONFIRMED |
| `[ResponseCache]` on a money endpoint serves a stale price | Removed from `PackageController`, with a comment recording why. The two remaining attributes are on anonymous reference data | `PackageController.cs:8`; `StationController.cs:8`, `StationNodeController.cs:8` |
| Middleware ordering lets a request reach checkout before authorization or signing | Order is correct: `UseRateLimiter` → `SessionValidationMiddleware` → `UseAuthorization` → `DeviceSignatureMiddleware` → `MapControllers` | `PipelineSetup.cs:20-37` |
| Request body size limits are absent, so a huge upload exhausts memory | 25 MB at both `FormOptions.MultipartBodyLengthLimit` and `Kestrel.Limits.MaxRequestBodySize`. (This bounds *bytes*; it did not bound declared PDF geometry — [FF-09](03-findings-medium-low.md#ff-09--ff-10--pdf-page-dimensions-and-qr-decode-size-unclamped--medium-confirmed-fixed)) | `Program.cs:79-82` |
| CI runs a tool downloaded without integrity verification, or actions on mutable tags | Both closed: gitleaks pinned at `ci.yml:20` with a SHA-256 recorded at `:21` and enforced at `:52`; nine action refs pinned to commit SHAs | `.github/workflows/ci.yml` |
