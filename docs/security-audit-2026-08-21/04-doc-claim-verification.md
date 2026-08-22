# 5. Doc-claim verification

The brief's instruction was *"trust nothing that is written down"* — every "done", "verified" and "already good" in the project's own documentation is an unverified hypothesis until a `file:line` citation confirms it. This section is that audit. The operator identified it as the highest-value output of the engagement.

**Legend.** **CONFIRMED** — the claim is true and I can cite the code. **PARTIAL** — true in part; the gap is named. **REGRESSED** — was true, is no longer. **NOT IMPLEMENTED** — claimed or tracked, absent in code. **STALE** — the code is *better* than the doc says; the doc misleads in the safe direction. **REFUTED** — the claim is false in a way that misdirected effort.

---

## Summary of what this section found

Four patterns, in descending order of how much damage each does:

1. **One "Critical" finding in the remediation plan does not exist.** `REMEDIATION_PRIORITIES.md` P0 F4 demands a git-history purge for a live Supabase database password. The commit hashes it cites are not in this repository, and the value in the commits that *do* touch that URI is the literal string `***REDACTED***`. Someone was being pointed at a P0 that is not real, while FF-03 — unsigned OTA updates, genuinely Critical — appears nowhere in any document.
2. **`FRAUD_ANALYSIS.md` contradicts itself on whether admin voucher changes are audited.** Its status table says implemented; its body rates the same item High and open. The body is wrong, and it is the part an owner reads for "what's left".
3. **Several docs describe fixed problems as unfixed.** Harmless to security, corrosive to trust: once a document is wrong in the safe direction, its accurate warnings stop being believed.
4. **Platform references are stale throughout.** Multiple docs still instruct the reader to configure Render, which is no longer the deployment target. A reader following those instructions configures nothing.

---

## `docs/REMEDIATION_PRIORITIES.md`

| Claim | Citation | Verdict | Evidence |
|---|---|---|---|
| P0 **F4**: *"Live Supabase DB password committed in git history — CRITICAL"*, citing commits `7ff3dae1`, `0ff95631`, `aa36b124` | `:~20-25` | **REFUTED** | **None of those three commits exist in this repository.** The commits that actually touch the Supabase pooler URI are `ae8618e`, `8814c74`, `c308d50`, `01f9b2f`, `215dc1c`. I swept **2,622 text blobs** across all history and found 23 distinct postgres-URI credential values; **every one is a placeholder** (breakdown below). |
| F4 corollary: the committed value is a live password | `:22` | **REFUTED** | The value present at `HEAD:docs/REMEDIATION_PRIORITIES.md:22` is a 14-character string, 7 distinct characters, max repeat 6, entropy 2.41, containing the token `redact` — it is `***REDACTED***`. The document demanding a purge contains only a redaction marker. |
| *"the gitleaks job will stay red until F4's git-history purge is complete"* | `:49-51` | **REFUTED** | Empirically false. `gitleaks detect` is **green over 672 commits**. There was no red gate anyone was waiting on. This is [FF-22](03-findings-medium-low.md#ff-22--the-secret-scanner-reported-no-leaks-while-systematically-blind--medium-confirmed-fixed) — and the scanner was green for the wrong reason, which is worse than red. |

**The 23 credential values, resolved.** Each identified by hash, values never reproduced:

| Hash prefix | Resolved value | Note |
|---|---|---|
| `a942b37ccf` | `postgres` | local dev default, entropy 2.75 |
| `5e884898da` | `password` | entropy 2.75 |
| `0be64ae89d` | `PASSWORD` | in `render.yaml` as `postgresql://USER:PASSWORD@…` — username is `USER` |
| `7ee58f1354` | `***REDACTED***` | the F4 "live password" |
| `69bf0bc46f` | 4 chars, entropy **0.0** | masked log output in `backend_logs.txt` |
| *(remaining)* | placeholders of the same classes | |

**Conclusion: there is no live Supabase database password in this repository's history.**

**History-rewrite check.** `.git/refs/original` exists — evidence of a past `filter-branch`-style rewrite — but is an empty directory. Because `git rev-list --objects --all` does not include unreachable objects, I additionally ran `git fsck --unreachable --dangling`: 10 unreachable blobs, 2 flagged, both abandoned Azure CDKTF code (`namespace FuelFlow.Infra;`). Read with values masked, the "secrets" were environment-variable *names* passed to `GetRequiredEnv(...)`, Key Vault secret-*name* constants (`const string secretNameStripeSecret`, `secretNameTwilioToken`, lines 82/86), and `AdministratorPassword = config.PostgresAdminPassword` — a config reference. **No secret values in unreachable objects.** Caveat carried to the coverage statement: objects predating the rewrite may have been garbage-collected and are unrecoverable for audit.

---

## `docs/FRAUD_ANALYSIS.md`

| Claim | Citation | Verdict | Evidence |
|---|---|---|---|
| WP-1 server-authoritative pricing — *"✅ Implemented"* | `:30` | **CONFIRMED** | `ServerPricing.cs` recomputes unit price from `FuelPackages`; the client-supplied price is never trusted. (Note: the *quantity* was still unbounded — [FF-02](02-findings-critical-high.md#ff-02--bulk-checkout-quantity-unvalidated--integer-overflow--critical-confirmed-fixed). The claim is true; it was just not sufficient.) |
| WP-2 Monobank webhook signature verification — *"✅ Implemented"* | `:31` | **CONFIRMED** | ECDSA secp256k1 / SHA-256 over the raw body in `ProcessMonobankWebhookCommandHandler.cs`, with `Program.cs:177-185` refusing to boot in Production if `Monobank:PublicKey` is still a placeholder. Verification cannot be silently skipped. |
| WP-3 admin voucher audit events — *"✅ Implemented"* | `:32` | **CONFIRMED** | `RecordEventAsync` on `ProviderEventService` in **5 handlers**: `BulkActionVouchersCommandHandler.cs:41,94`, `DeleteVoucherCommandHandler.cs:36`, `Import/VouchersController.cs:84`, `UnblockVoucherCommand.cs:60`, `UpdateVoucherCommandHandler.cs:59`. |
| WP-3 — *"no `RecordEventAsync`/`IEventService` anywhere under `Features/Vouchers/`"*, rated **High, open** | `:15`, `:123-135` | **STALE — self-contradictory** | Directly contradicts `:32` **in the same document**, and the five citations above disprove it. **This is the single most misleading line in the docs**: an owner reading the executive summary sees an open High that has been closed. Coverage is complete for state-changing paths — there is no separate assign endpoint, since assignment goes through the audited `PUT /{id}` (`AdminVoucherController.cs:74`) and bulk-action (`:94`). |
| Second open High in the summary | `:17`, `:145-163` | **STALE** | Same pattern as above — summary row describes work since completed. |
| Supabase credential exposure | `:22` | **STALE** | Same claim as `REMEDIATION_PRIORITIES.md` F4; refuted above. |
| *"Rotate committed Monobank token — Token currently committed; rotate after webhook verification ships"* | `:62`, repeated `:309` | **CONFIRMED** | This one is accurate, and it independently corroborates [FF-05](02-findings-critical-high.md#ff-05--monobank-merchant-token-committed-to-git-history--high-suspected-live-open). The project's own docs say this token was committed and needs rotating; the owner attests rotation on 2026-08-20. **Still open pending written confirmation.** |
| WP-4 atomic `mark-used` | `:57` | **NOT IMPLEMENTED** — accurately tracked | `MarkVoucherAsUsedCommandHandler.cs:20-56` is read-then-write: `voucher.Status = VoucherStatus.Used; _context.FuelVouchers.Update(voucher);` with no conditional `UPDATE`. The doc is honest. **Benign** on inspection: authorization is correct (`WorkerUserId`, then `AssignedToUserId`), an already-`Used` voucher returns success so the outcome is idempotent, and no money moves. Worst case is a redundant write, not a double-spend. |
| Automated encrypted backup | `:33` | **PARTIAL** | Scripts now exist (`deploy/backup.sh`, `restore.sh`) with `age` encryption, off-host upload and `pg_restore --list` validation. **No restore has ever been performed.** |
| Basic monitoring / alerting | `:34` | **NOT IMPLEMENTED** | `⬜ TODO`. No alerting exists. `/health` exists (below) but nothing watches it. |
| *"production is not 'live' until all five above are done and verified"* | `:36` | **GATE NOT MET** | Items 4 (`:33`) and 5 (`:34`) are unmet. **The project's own launch gate fails on its own terms** — a direct input to the NO GO verdict, independent of my findings. |
| `Fulfilled` / `Cancelled` are terminal — *"no webhook may ever re-flip them"* | `:232-238` | **CONFIRMED for the webhook; was VIOLATED elsewhere** | The webhook handler honours it. `SimulatePaymentCommandHandler` bypassed `OrderStateMachine` entirely and force-set `PendingFulfillment` from any state — [FF-06](02-findings-critical-high.md#ff-06--apipurchasessimulate-production-reachable-and-bypassed-the-state-machine--high-confirmed-fixed). The rule was right; one caller ignored it. |
| secp256k1 verification is Linux-only | `:76` | **MOOT** | Concern predates containerisation; the deploy target is Linux Docker. |
| Mobile web build may expose a signing key | `:188` | **UNVERIFIED** | Mobile was not audited in this pass — see [07-coverage-statement.md](07-coverage-statement.md). Carried forward. |

---

## `docs/SECURITY.md`

| Claim | Citation | Verdict | Evidence |
|---|---|---|---|
| *"not yet implemented"*: refresh tokens stored plaintext | `:118-120` | **STALE** (fixed) | Hashed — `RefreshTokenCommand.cs:50,113`. |
| *"not yet implemented"*: verification codes stored plaintext | `:118-120` | **STALE** (fixed) | Hashed — `SendCodeCommand.cs:58,116`, `VerifyCodeCommand.cs:74`. |
| *"not yet implemented"*: OTP uses `Random.Shared` | `:118-120` | **STALE** (fixed) | CSPRNG — `SendCodeCommand.cs`. |
| *"Set `Jwt__Secret` in Render"* | `:86` | **STALE** | Wrong platform. Following this configures nothing on DigitalOcean. |
| *"Render terminates TLS"* | `:125` | **STALE** | TLS now terminates at Caddy — and this stale assumption is upstream of the forwarded-headers regression, [FF-19](03-findings-medium-low.md#ff-19--stale-forwarded-headers-trust-collapsed-rate-limit-partitions--low-confirmed-fixed). A stale doc that encodes a stale trust boundary is not merely untidy. |

All three `:118-120` items are wrong **in the safe direction**. The harm is second-order: a section that lists fixed items as broken trains readers to discount the section, including the parts that are correct.

---

## `TODO.md`

| Claim | Citation | Verdict | Evidence |
|---|---|---|---|
| CSPRNG OTP + 5-attempt lockout + forwarded-headers trust | `:97` | **PARTIAL** | CSPRNG ✅, 5-attempt lockout ✅, forwarded headers **REGRESSED** → [FF-19](03-findings-medium-low.md#ff-19--stale-forwarded-headers-trust-collapsed-rate-limit-partitions--low-confirmed-fixed), and it silently broke the per-phone OTP partition → [FF-07](02-findings-critical-high.md#ff-07--per-phone-otp-rate-limit-never-functioned--high-confirmed-fixed). |
| Refund amount capped at the refundable balance | `:98` | **CONFIRMED** | `RefundOrderCommandHandler.cs:109` computes it server-side; `:115` `Math.Min(command.AmountKopecks ?? refundableAmount, refundableAmount)`. |
| `FakeSmsService` cannot be selected in Production | `:99` | **CONFIRMED** | `ServiceSetup.cs:178-209` plus the hard refusal at `Program.cs:206-211`; `HasTwilioConfiguration` also rejects the literal `your_production_account_sid_here`. |
| `Auth:TestPhones` handled | `:100` | **CONFIRMED and improved** | Now warns at startup with a **count only** — never the numbers or codes (`Program.cs:243-254`). |
| `[ResponseCache]` removed from money-sensitive controllers | `:101` | **CONFIRMED** | `PackageController.cs:8` carries the explanatory comment and no attribute. |
| *"6 controllers"* still carry `[ResponseCache]` | `:26` | **DRIFT** | It is **2**: `StationController.cs:8`, `StationNodeController.cs:8`. Both anonymous reference data. See [FF-31](03-findings-medium-low.md#ff-31--anonymous-cacheable-apistationsfuel-types-returns-the-raw-entity--info-confirmed-open-hardening). |
| Refresh-token family revocation | `:81` | **CONFIRMED** | Implemented in `RefreshTokenCommand.cs`. |
| Do not enable device-signature enforcement before the signing mobile build ships | `:82` | **STILL STANDS** | Correct and important. Preserved deliberately: [FF-21](03-findings-medium-low.md#ff-21--deviceauthoptions-defaults-fail-open--low-confirmed-fixed)'s fix requires an explicit acknowledgement rather than forcing enforcement on. Carried into [05-deploy-checklist.md](05-deploy-checklist.md). |
| `/health` endpoint | `:35` | **CONFIRMED** | `PipelineSetup.cs`: minimal API, `.AllowAnonymous()`, probes the DB via `GetPendingMigrationsAsync()` and returns `Results.Problem("Database unreachable", 503)`. A real dependency check, not a static 200. |
| Deployment platform tasks | `:80` | **OPEN and now STALE** | Written for Render; target is DigitalOcean. |
| Static `000000` OTP via `DevBypass` | `:93` | **CONFIRMED fixed** | Blocked in Production at `Program.cs:193-200`. |
| Twilio UA SMS cost $0.2268/message | `:89` | **CONFIRMED** | Cited as the cost basis for [FF-07](02-findings-critical-high.md#ff-07--per-phone-otp-rate-limit-never-functioned--high-confirmed-fixed) and `SmsBudgetGuard`. |

---

## `.github/workflows/ci.yml`

The handoff recorded one standing CI weakness: gitleaks downloaded via `curl -sSfL` with no checksum, and third-party actions pinned to mutable major tags. A tag is a movable pointer — whoever controls the action's repository can retarget `v4` at new code, which then runs with the workflow's token.

| Item | Citation | Verdict |
|---|---|---|
| Pinned gitleaks version | `:20` `GITLEAKS_VERSION: "8.21.2"` | **CONFIRMED fixed** |
| Checksum recorded and enforced | `:21` `GITLEAKS_SHA256`, verified at `:52` via `sha256sum -c -` | **CONFIRMED fixed** |
| Third-party actions pinned to immutable commit SHAs | 9 refs at `:37, :59, :89, :92, :107, :122, :125, :148, :151`, each with a `# vX.Y.Z` comment | **CONFIRMED fixed** |

---

## Recommendation

Fix the docs in this order, because the ordering is about which wrong statement is currently costing the most:

1. **`FRAUD_ANALYSIS.md:15` and `:123-135`** — delete or mark closed. It is an open High in the summary that is actually done, and it contradicts `:32` in the same file.
2. **`REMEDIATION_PRIORITIES.md` F4** — mark REFUTED with the reasoning, not merely deleted. Someone will otherwise rediscover the `***REDACTED***` string and re-raise it as a P0. Replace with the real open item: FF-05, the Monobank token.
3. **`REMEDIATION_PRIORITIES.md:49-51`** — remove the false claim that the gitleaks gate is red.
4. **`SECURITY.md:118-120`** — move all three to implemented, with citations.
5. **Every Render reference** (`SECURITY.md:86,125`, `TODO.md:80`) — retarget to DigitalOcean/Caddy. `:125` in particular encodes a trust boundary that is no longer true.
6. **Add FF-03 to the tracked risk list.** The most severe finding in this audit is absent from all four documents.
