# FuelFlow Pre-Production Security Audit — Verdict: **NO GO**

**NO GO.** Two blockers remain open and neither can be closed by changing code in this repository: over-the-air mobile updates are unsigned, so anyone holding the EAS token can push arbitrary JavaScript to every installed app (FF-03, Critical); and no backup restore has ever been demonstrated, which is an explicit GO criterion and this project's own launch gate (`docs/FRAUD_ANALYSIS.md:33,36`). Everything else rated Critical or High is fixed in code and verified below.

- **Audit date:** 2026-08-21 → 2026-08-22
- **Scope:** FuelFlow backend (.NET 10 API + JobsWorker), admin SPA, mobile app, deploy tooling, CI, git history
- **Rules of engagement:** static analysis and local test runs only; no live host, database, Monobank endpoint or Twilio account was contacted. No secret value is reproduced anywhere in this report — locations and classes only.
- **Deviation from the original brief:** the brief was read-only ("findings only"). The operator subsequently instructed *"fix all this and report to MD file"*, so remediation was performed. Nothing has been committed — all changes sit in the working tree for review.

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

All remediation is uncommitted working-tree change:

```
30 files changed, 970 insertions(+), 120 deletions(-)
```

plus two new backend files (`SmsBudgetGuard.cs`, `ImportConcurrencyGuard.cs`) and a new `deploy/` directory (compose file, Caddyfile, hardened `backup.sh` / `restore.sh`).

**Verification state at time of writing:**

| Check | Result |
|-------|--------|
| `dotnet build FuelFlow.slnx` | Build succeeded — 0 errors, 3 warnings (all in `FuelFlow.IntegrationTests`) |
| `dotnet test FuelFlow.UnitTests` | Failed: 0, **Passed: 340**, Skipped: 0 |
| `npx vitest run` (admin) | **14 passed** (1 file) |
| `npm run build` (admin) | Succeeds — 1812 modules, 561 kB bundle; rebuilt `dist/index.html` has 0 inline scripts, so the Caddy CSP `script-src 'self'` holds |
| `gitleaks detect` (full history, new config) | 672 commits scanned, **no leaks found** |
| `gitleaks detect` (synthetic positive control) | **4/4** real secrets caught, **0** false positives |

**Client-compatibility statement (required by the operator's deploy-order note):** no remediation in this audit changes the device-signature payload format. The FF-04 fix is server-side only — it adds enforcement to a trailing-slash path variant that no legitimate client sends. Installed mobile builds are unaffected; checkout will not break. The separate, pre-existing deploy gate at `TODO.md:82` still stands and is carried into section 6.
