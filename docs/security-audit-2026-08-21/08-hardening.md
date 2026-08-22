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

## 2. Project DTOs instead of returning EF entities

[FF-31](03-findings-medium-low.md#ff-31--anonymous-cacheable-apistationsfuel-types-returns-the-raw-entity--info-confirmed-open-hardening) is the specific instance; the pattern is broader. Returning an entity means the API's response contract is whatever the database model happens to be *today*. Adding a `CostPerLiter` or `SupplierMargin` column to `Station` silently publishes it — anonymously and with `Cache-Control: public, max-age=300` — with no change to any endpoint and nothing for a reviewer to notice.

A DTO makes that addition a deliberate act.

## 3. Add a retention policy for `OutboxEvents`

[FF-25](03-findings-medium-low.md#ff-25--three-unbounded-outboxevents-full-table-loads--medium-confirmed-fixed) fixed the unbounded *reads*. The table still grows forever. Add a pruning job for processed events past a retention window, before table size becomes an incident rather than a chore.

## 4. Move voucher import to a background job

The proper fix for [FF-23](03-findings-medium-low.md#ff-23--voucher-import-runs-synchronously-in-request--medium-confirmed-partial). `ImportConcurrencyGuard` bounds the damage; it does not remove the shape of the problem. Enqueue the import, return a job id, poll from the admin SPA. This also removes the need for the 300 s proxy timeouts, which are themselves a small availability liability.

## 5. Implement WP-4's atomic `mark-used`

`FRAUD_ANALYSIS.md:57` tracks this accurately and I confirmed it is unimplemented (`MarkVoucherAsUsedCommandHandler.cs:20-56` is read-then-write). It is genuinely benign today — authorization is correct, the outcome is idempotent, no money moves — so it is here rather than in the findings. Worth doing because it makes the voucher state machine uniformly conditional-update, which is easier to reason about than "atomic except in one handler".

## 6. Either enforce `TokenVersion` or remove it

[FF-15](03-findings-medium-low.md#ff-15--tokenversion-latent-trap--low-suspected-fixed-documented). The field is documented now, so the trap is disarmed. But a field named `TokenVersion` that does not version tokens will mislead someone eventually. Compare it during refresh, or delete it.

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
