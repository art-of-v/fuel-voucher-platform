# Hardening recommendations

Kept separate from the findings, per the brief: **nothing here has a demonstrated exploit.** These are defence-in-depth suggestions. None is a deploy blocker, and none should be allowed to dilute the findings sections.

Ordered by value-per-effort.

---

## 1. Set a global authorization fallback policy

`Program.cs` sets no `FallbackPolicy`. An endpoint with no `[Authorize]` attribute is therefore anonymous **by default** — which is exactly how [FF-01](03-findings-medium-low.md) happened: an omission, not a mistake.

```csharp
options.FallbackPolicy = new AuthorizationPolicyBuilder()
    .RequireAuthenticatedUser()
    .Build();
```

This inverts the default so that forgetting an attribute produces a 401 rather than a public endpoint. It requires auditing every genuinely anonymous route first (`/health`, station/fuel-type reference data, auth endpoints, the Monobank webhook) and marking each `[AllowAnonymous]` explicitly — which is worth doing anyway, because it turns the anonymous surface into an enumerable list instead of a property you have to derive by reading 34 controllers.

**Highest-value item in this section.** It converts a whole class of future Critical into a startup-visible 401.

**Deliberately not implemented in the 2026-08-22 remediation round, and this is the reason.** The change itself is four lines; the work is the audit that must precede it. Getting it wrong is not a subtle regression — a single missed `[AllowAnonymous]` returns 401 on a public endpoint in production, which for the station list or the Monobank webhook means a visible outage or silently lost payment callbacks. The integration suite cannot be executed in this environment to catch that, so shipping it here would have meant an unverified change to the anonymous surface of every endpoint. It is the right change, made with tests and a route-by-route list, not from an audit seat.

## 2. Project DTOs instead of returning EF entities — **done for the anonymous endpoints (2026-08-22)**

[FF-31](03-findings-medium-low.md#ff-31--anonymous-cacheable-station-endpoints-returned-raw-entities--info-confirmed-fixed-2026-08-22) was the specific instance and is now fixed: `/api/stations` and `/api/stations/fuel-types` project `PublicStationResponse` and `PublicFuelTypeResponse`. **The pattern is broader and the recommendation stands for the rest of the API.** Returning an entity means the API's response contract is whatever the database model happens to be *today*. Adding a `CostPerLiter` or `SupplierMargin` column to `Station` silently publishes it — anonymously and with `Cache-Control: public, max-age=300` — with no change to any endpoint and nothing for a reviewer to notice.

A DTO makes that addition a deliberate act. The reasoning is recorded as a comment on `StationController` so it survives without this document.

## 3. Add a retention policy for `OutboxEvents`

[FF-25](03-findings-medium-low.md#ff-25--three-unbounded-outboxevents-full-table-loads--medium-confirmed-fixed) fixed the unbounded *reads*. The table still grows forever. Add a pruning job for processed events past a retention window, before table size becomes an incident rather than a chore.

## 4. Move voucher import to a background job

The proper fix for [FF-23](03-findings-medium-low.md#ff-23--voucher-import-runs-synchronously-in-request--medium-confirmed-partial). `ImportConcurrencyGuard` bounds the damage; it does not remove the shape of the problem. Enqueue the import, return a job id, poll from the admin SPA. This also removes the need for the 300 s proxy timeouts, which are themselves a small availability liability.

## 5. Implement WP-4's atomic `mark-used` — **done (2026-08-22)**

`FRAUD_ANALYSIS.md:57` tracked this accurately and I confirmed it was unimplemented (read-then-write). It was genuinely benign — authorization correct, outcome idempotent, no money moving — which is why it sat here rather than in the findings. It was worth doing anyway, for the reason given at the time: the voucher state machine is now uniformly conditional-update, which is easier to reason about than "atomic except in one handler".

`MarkVoucherAsUsedCommandHandler` now reads with `AsNoTracking()` for authorization and reporting, then issues a conditional `ExecuteUpdateAsync` whose `WHERE` clause repeats `Status == Assigned`, then re-reads when 0 rows are affected so the losing caller is told the true state rather than a stale one.

**One caveat remains, and one is now closed.** *Closed:* the two tests in `MarkVoucherAsUsedConcurrencyIntegrationTests` have been executed — **2 passed**, and a negative control against the pre-fix handler **fails**, so the tests are proven to catch the bug rather than merely to pass ([07-coverage-statement.md](07-coverage-statement.md#test-execution-once-docker-became-available--2026-08-23)). *Standing:* the fix could not be tested in the unit suite at all — the EF in-memory provider does not translate `ExecuteUpdate`, so one existing test had to move to the integration suite, which means this guard is only ever exercised when a Docker daemon is present. An `xmin` concurrency token and raw SQL were both considered and rejected — the first changes behaviour for every `FuelVoucher` write on the money paths, the second hits the same provider limitation.

## 6. Either enforce `TokenVersion` or remove it

[FF-15](03-findings-medium-low.md#ff-15--tokenversion-latent-trap--low-suspected-fixed-documented). **Withdrawn on re-check (2026-08-22): the field *is* enforced**, at `SessionValidationMiddleware.cs:36-54`, which compares the token's version against the user's on every request. The premise of this recommendation — "a field named `TokenVersion` that does not version tokens" — was wrong, and the earlier "FIXED (documented)" label understated what the code already did. Nothing to do.

## 7. Consider `SameSite=Strict` or a CSRF token for refresh

`SameSite=None` on the refresh cookie is currently defended by an Origin allow-list (`AuthController.cs:127-128`). That works, and [FF-08](02-findings-critical-high.md#ff-08--api-published-on-the-admin-origin--high-confirmed-fixed) removed the second origin that made it hard to reason about. It is still a single string-comparison standing between an attacker's page and a token refresh. If the mobile and web flows permit `Strict`, prefer it; otherwise a double-submit token is a cheap second layer.

## 8. Raise `WorkerCount` above 1, deliberately

`Program.cs:67` sets `WorkerCount = 1`. That is a reasonable single-Droplet default and it makes several concurrency questions moot — but it also means one slow job stalls all fulfilment, and it is the reason a backlog is a plausible symptom in the watchlist. If it is raised, the advisory-lock and conditional-update guarantees in `FulfillmentService` become load-bearing in a way they currently are not. Change it consciously, with the concurrency tests to match.

## 9. Emit startup security state as structured output, not just warnings

`ValidateSecurityConfiguration` logs `SECURITY:` warnings for disabled device binding and active test phones. Warnings scroll past. Emitting a single structured "security posture" event at startup — device binding on/off, test-phone count, DevBypass state — makes the posture queryable and alertable rather than something a human has to notice in a deploy log.

## 10. Time-bound the `.gitleaks.toml` commit allowlist

The two commit allowlist entries exist so the scanner's signal stays meaningful while FF-05's history decision is pending. They are documented in-file. Add the decision date from checklist item #5 so they get removed rather than becoming permanent blind spots.

## 11. Add a rate limit to the Monobank webhook endpoint

It must stay anonymous (Monobank calls it) and it is signature-verified, so forged calls are rejected. But signature *verification* costs CPU, so an unauthenticated flood of invalid signatures is a cheap way to spend your cycles. The FF-26 global limiter now covers it, which is probably enough; a dedicated, generous policy keyed on source would be tighter.
