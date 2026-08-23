# FuelFlow Pre-Production Security Audit — Verdict: **NO GO**

**NO GO.** Two blockers remain open and neither can be closed by changing code in this repository: over-the-air mobile updates are unsigned, so anyone holding the EAS token can push arbitrary JavaScript to every installed app (FF-03, Critical); and no backup restore has ever been demonstrated, which is an explicit GO criterion and this project's own launch gate (`docs/FRAUD_ANALYSIS.md:33,36`). Everything else rated Critical or High is fixed in code and verified below.

- **Audit date:** 2026-08-21 → 2026-08-22
- **Scope:** FuelFlow backend (.NET 10 API + JobsWorker), admin SPA, mobile app, deploy tooling, CI, git history
- **Rules of engagement:** static analysis and local test runs only; no live host, database, Monobank endpoint or Twilio account was contacted. No secret value is reproduced anywhere in this report — locations and classes only.
- **Deviation from the original brief:** the brief was read-only ("findings only"). The operator subsequently instructed *"fix all this and report to MD file"*, so remediation was performed. The remediation has since been committed and merged by the repository owner as **PR #322** (`bac99db`, 17 commits) — it is no longer pending review in a working tree.

---

## Report index

| # | Section | File |
|---|---------|------|
| 1 | Verdict | this file |
| 2 | Executive summary | this file, below |
| 3 | Findings table | [01-findings-table.md](security-audit-2026-08-21/01-findings-table.md) |
| 4 | Findings in detail | [02-findings-critical-high.md](security-audit-2026-08-21/02-findings-critical-high.md), [03-findings-medium-low.md](security-audit-2026-08-21/03-findings-medium-low.md) |
| 5 | Doc-claim verification | [04-doc-claim-verification.md](security-audit-2026-08-21/04-doc-claim-verification.md) |
| 6 | Deploy-blocking checklist | [05-deploy-checklist.md](security-audit-2026-08-21/05-deploy-checklist.md) |
| 7 | Post-deploy watchlist | [06-post-deploy-watchlist.md](security-audit-2026-08-21/06-post-deploy-watchlist.md) |
| 8 | Coverage statement | [07-coverage-statement.md](security-audit-2026-08-21/07-coverage-statement.md) |
| — | Hardening (no demonstrated exploit) | [08-hardening.md](security-audit-2026-08-21/08-hardening.md) |
| — | Refuted hypotheses | [09-refuted.md](security-audit-2026-08-21/09-refuted.md) |

---

## 2. Executive summary

For the owner, in plain terms — ten points, no jargon.

1. **Do not launch yet.** Two things are unfinished, and both are operational actions rather than code: signing your mobile app's automatic updates, and proving once that a database backup can actually be restored.

2. **The mobile update channel is the biggest open risk.** Your app can update itself without going through the app stores. That channel is not signed, so whoever holds the publishing token can replace your app's code on every phone that has it installed — including the screens that handle payment. Rotate that token and turn on update signing before launch.

3. **The two ways money could previously walk out the door are closed.** Prices are now computed on the server, so a hacked app cannot buy vouchers for 1 ₴; and payment confirmations from Monobank are cryptographically verified, so nobody can forge a "paid" message. Both were verified by reading the code, not by trusting the docs.

4. **A customer could previously download your entire voucher catalogue without logging in.** That is fixed. So is a bug where a customer could order a nonsensical quantity and overflow the price calculation into a negative number.

5. **An admin could previously issue a second set of vouchers for a single payment** by re-triggering a test-payment endpoint on an already-completed order. That endpoint is now unreachable in production and can no longer resurrect a finished order.

6. **Your security documentation cannot be trusted as a status report.** It describes several problems as unfixed that were fixed months ago, and describes one "Critical" leaked database password that does not exist — the value in those commits is the word `***REDACTED***`. One document contradicts itself on whether admin voucher changes are logged (they are). Details in section 5.

7. **Your automated secret scanner was reporting "all clear" while genuinely failing to look.** The industry-standard rule it relies on silently discards any secret that happens to contain one of 1,476 common English words — and longer, more random secrets are *more* likely to trip that. It was fixed by adding rules built around your own configuration names, then tested with both real and fake secrets to prove it now catches and correctly ignores each.

8. **One committed credential still needs your confirmation:** a Monobank merchant token sits in an old commit. You told me all secrets were rotated on 2026-08-20; I cannot verify that without touching live systems, and your own `FRAUD_ANALYSIS.md:62` also lists it as needing rotation. Please confirm in writing, and confirm whether this repository is public or private.

9. **Abuse and cost controls were largely broken and are now working.** The per-phone SMS limit never functioned at all (it failed silently on every request), so an attacker could have run up your Twilio bill; there was also no overall request limit. Both are fixed, plus a daily SMS spend ceiling.

10. **Refunds hold up well.** I specifically tried to double-refund, over-refund, refund a negative amount, and race two refunds at once. All four are blocked, and only admins can refund at all.

---

## Change summary

Remediation was merged by the repository owner as **PR #322** (`bac99db`) across 17 commits, one per finding cluster:

```
code, config and deploy tooling:  37 files changed, 1742 insertions(+), 120 deletions(-)
this report:                      12 files,          2160 insertions(+)
```

That includes two new backend files (`SmsBudgetGuard.cs`, `ImportConcurrencyGuard.cs`) and a new `deploy/` directory (compose file, Caddyfile, `backup.sh` / `restore.sh`).

**Note on verification timing:** the checks below were run against the working tree *before* the merge. The merge was a fast-forward of that same content, so they still describe what is on `main` — but they were not re-run post-merge, and re-running them on `main` is checklist item 0 below.

**Verification state at time of writing:**

| Check | Result |
|-------|--------|
| `dotnet build FuelFlow.slnx` | Build succeeded — 0 errors, 3 warnings (all in `FuelFlow.IntegrationTests`) |
| `dotnet test FuelFlow.UnitTests` | Failed: 0, **Passed: 340**, Skipped: 0 |
| `npx vitest run` (admin) | **14 passed** (1 file) |
| `npm run build` (admin) | Succeeds — 1812 modules, 561 kB bundle; rebuilt `dist/index.html` has 0 inline scripts, so the Caddy CSP `script-src 'self'` holds |
| `gitleaks detect` (full history, new config) | 672 commits reported scanned, **no leaks found** — see the coverage caveat on this figure in [07-coverage-statement.md](security-audit-2026-08-21/07-coverage-statement.md) |
| `gitleaks detect` (synthetic positive control) | **4/4** real secrets caught, **0** false positives |

**Client-compatibility statement (required by the operator's deploy-order note):** no remediation in this audit changes the device-signature payload format. The FF-04 fix is server-side only — it adds enforcement to a trailing-slash path variant that no legitimate client sends. Installed mobile builds are unaffected; checkout will not break. The separate, pre-existing deploy gate at `TODO.md:82` still stands and is carried into section 6.

---

## Second remediation round — 2026-08-22

The first round closed everything rated Critical or High. This round went back for the items that had been recorded as *open, accepted* or *deferred*, on the basis that "accepted" is a decision the owner should make, not a label I award myself to finish sooner. Four of the five were closable in code.

| Finding | Was | Now |
|---|---|---|
| **WP-4** atomic `mark-used` | Unimplemented; assessed benign | **Fixed.** `MarkVoucherAsUsedCommandHandler` reads for authorization with `AsNoTracking()`, then performs a conditional `ExecuteUpdateAsync` carrying `Status == Assigned` in the `WHERE` clause, then re-reads on 0 rows affected so the loser is told the real outcome. Exactly one of two concurrent redemptions wins. |
| **FF-31** raw entity on an anonymous cacheable endpoint | Open (hardening) | **Fixed, and widened.** Both `/api/stations` and `/api/stations/fuel-types` now project explicit DTOs (`PublicStationResponse`, `PublicFuelTypeResponse`). The `fuel-types` action was also calling the *admin* query handler; it now uses a new public one. Verified against `mobile/src/core/types/api.ts`: **no client impact** — the DTOs keep exactly the fields the client declares. |
| **FF-33** `SSH.NET` GHSA-q939-rpr3-3284 | Open (accepted) | **Fixed.** Pinned to `2026.0.0` (the first patched release) via a direct `PackageReference` in `FuelFlow.IntegrationTests.csproj`, with a comment recording why the pin exists and when it can go. Verified empirically: the two `NU1903` audit warnings are gone. |
| **FF-15** `TokenVersion` latent trap | Fixed (documented) | **Confirmed already enforced in code** at `SessionValidationMiddleware.cs:36-54`. No code work was owed; the earlier "documented" label understated it. |
| **FF-03** unsigned OTA updates | Open, Critical | **Still open — cannot be closed in this repository.** But it is now *enforceable*: `mobile/scripts/check-update-signing.mjs` fails the build when an update channel is enabled without a signing certificate, wired into CI and `npm run check:ota-signing`. See the acknowledgement note below. |

**The FF-03 gate is currently set to report, not block.** `.github/workflows/ci.yml` sets `FUELFLOW_ACK_UNSIGNED_OTA: 'true'` on that step, so CI stays green while the Critical remains acknowledged rather than red. That was a judgement call made on the owner's behalf and it is reversible in one edit: **delete the four-line `env:` block** and the gate becomes hard. The flag is deliberately in the workflow file rather than defaulted inside the script, so it is visible to a reviewer. The script mirrors the backend's own posture at `Program.cs:221-233` — a known-missing control may be accepted, but only explicitly and never silently.

**Deliberately not done, stated rather than buried:**

- **FF-23** — moving voucher import off the request thread into Hangfire. Large, touches the admin SPA, and sits on the voucher-minting path; the integration suite cannot be executed here to catch a regression. Recommended as a follow-up with tests, not as an unverified change to a money path.
- **Hardening item 1** — a global authorization `FallbackPolicy`. Needs an audit of every anonymous route across 34 controllers first; one missed route means production 401s on a public endpoint.

**Verification after this round:**

| Check | Result |
|-------|--------|
| `dotnet build FuelFlow.slnx` | Build succeeded — **0 errors, 1 warning** (down from 3; the remaining one is the pre-existing obsolete `PostgreSqlBuilder()` at `TestDatabaseFixture.cs:14`) |
| `dotnet test FuelFlow.UnitTests` | Failed: 0, **Passed: 339**, Skipped: 0 — one test *relocated*, not deleted (see below) |
| `dotnet test FuelFlow.JobsWorker.UnitTests` | **9 passed** |
| `dotnet test FuelFlow.Providers.UnitTests` | **17 passed** |
| `node scripts/check-update-signing.mjs` | Exit 1 unacknowledged, exit 0 with the flag — both paths exercised |

**Why the unit count went down by one.** `MarkVoucherAsUsed_ShouldTransitionFromAssignedToUsed` could no longer run against the EF Core in-memory provider, which does not translate `ExecuteUpdate`. It moved to `MarkVoucherAsUsedConcurrencyIntegrationTests` alongside a new race regression test.

Two alternatives to the conditional `UPDATE` were considered and rejected on record: an `xmin` concurrency token (changes behaviour for *every* `FuelVoucher` write across the money paths, and risks unhandled `DbUpdateConcurrencyException` surfacing as 500s) and raw SQL (hits the same in-memory provider limitation, so it buys nothing).

---

## WP-4 verified by execution — 2026-08-23

The section above stated that the two WP-4 integration tests were **unexecuted**, and that the fix therefore rested on compilation plus reasoning about Postgres `READ COMMITTED` semantics rather than on observation. A Docker daemon became available (Engine `29.5.3`, `linux/x86_64`), so they were run. That caveat no longer applies.

| Run | Result |
|---|---|
| `MarkVoucherAsUsedConcurrencyIntegrationTests` | **Total 2, Passed 2** — `ConcurrentRedemption_TransitionsExactlyOnce` (2 s), `MarkVoucherAsUsed_TransitionsFromAssignedToUsed` (281 ms) |
| **Negative control** — the same test against the pre-fix read-then-write handler | **Total 1, Failed 1**, at `MarkVoucherAsUsedConcurrencyIntegrationTests.cs:105` |

**The negative control is the part worth reading.** A green test proves the code passes the test, not that the test would have caught the bug — and this report had already asserted, falsifiably, that *"against the pre-fix read-then-write code this test fails."* Rather than leave that as a claim, the `Status == Assigned` predicate was removed from the handler's `WHERE` clause and the test re-run. It failed exactly where predicted: the loser of the race received `"Voucher marked as used"` where `"Voucher already marked as used"` was expected — i.e. the voucher was redeemed twice. The handler was restored from backup and the predicate confirmed present at line 57; the negative control was a transient local edit and is in no commit and not in the working tree.

**What passed *before* the failure matters more than the failure.** The assertion at line 98 — `redemption.IsCompleted.Should().BeFalse()` while a competing transaction holds the row lock — passed in the negative-control run too. So the test genuinely blocks on a Postgres row lock rather than quietly running two sequential calls, and the status predicate is precisely the thing that turns a double redemption into an idempotent one. This is the strongest evidence in the report for any concurrency finding, and it is the only one obtained by observation rather than by reading SQL semantics.

Full details, including the verbatim failure output, are in [07-coverage-statement.md](security-audit-2026-08-21/07-coverage-statement.md#test-execution-once-docker-became-available--2026-08-23).

