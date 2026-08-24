# FuelFlow Security Audit 2026-08-21 — open items

**NO GO.** The blockers are unchanged: over-the-air mobile updates are unsigned (FF-03, Critical), and no backup restore has ever been demonstrated (FF-14). Everything rated Critical or High that could be closed in code **was closed and verified** — those findings were removed from this report rather than annotated.

- **Audit date:** 2026-08-21 → 2026-08-24
- **Scope:** FuelFlow backend (.NET 10 API + JobsWorker), admin SPA, mobile app, deploy tooling, CI, git history
- **Rules of engagement:** static analysis and local test runs only; no live host, database, Monobank endpoint or Twilio account contacted. No secret value appears anywhere in this report — locations and classes only.

> **Pruned 2026-08-24.** All fixed findings (2 Critical, 5 High, 11 Medium, 10 Low, 4 Info),
> the completed verification records, and the second/third remediation-round narratives were
> removed. Full text remains in git history (`git log -- docs/security-audit-2026-08-21/`).
> What remains here is the open residue: two owner-action blockers, one deferred code change
> (FF-23), the pre-deploy checklist, the week-one watchlist, standing coverage caveats, and the
> refuted-hypotheses appendix.

---

## Open findings

| ID | Title | Sev | Status |
|----|-------|-----|--------|
| FF-03 | Expo OTA updates are unsigned | Critical | **OPEN** — release-sequenced operator action |
| FF-05 | Monobank merchant token committed to git history | High | **OPEN** — owner attestation + history decision |
| FF-14 | Backup restore never exercised | Medium | **OPEN** — restore drill (encryption tooling itself is fixed) |
| FF-23 | Voucher import runs synchronously in-request | Medium | **PARTIAL** — bounded by `ImportConcurrencyGuard`; background-job rewrite deferred |

---

## FF-03 — Expo OTA updates are unsigned — Critical, CONFIRMED, OPEN

**Attack.** The mobile app ships with `expo-updates`, which fetches a JavaScript bundle at launch and runs it. Update code signing is not enabled, so the client accepts whatever bundle the update server serves and verifies only that it came over TLS. Anyone who obtains the EAS publish token — from a developer machine, a CI log, a screenshot, or the token's own history — publishes a bundle of their choosing to every installed app. That bundle is the whole application: the checkout screen, the OTP entry field, the deep-link handler for `fuelflow://payment-result`. There is no store review in this path and no user action required.

**Why this outranks everything else.** Every other control assumes the client is the client. Server-authoritative pricing stops a *patched* app; it does not stop an attacker serving the *official* app's code. Device signing binds a request to a device key that the substituted bundle can read and use. The blast radius is the entire installed base simultaneously, and the compromise persists across restarts.

**Guard sought.** `expo-updates` supports `codeSigningCertificate` / `codeSigningMetadata` in `app.json`'s `updates` block, which makes the client refuse a bundle whose manifest is not signed by a key the binary was built with. Neither key is present.

**Why it cannot be fixed by an edit.** Enabling code signing requires generating a keypair, embedding the certificate in a **new native build**, and shipping that build to the stores. Adding `codeSigningCertificate` while pointing at a nonexistent file breaks the build; enabling it for clients not built with the certificate breaks their updates.

**Required actions (operator).**
1. Rotate the EAS token now, and audit which CI logs and machines have held it.
2. Generate an update signing keypair; store the private key in the EAS secret store, never in the repo.
3. Add `codeSigningCertificate` to the `updates` block and produce a new native build.
4. Release that build to both stores. Only after it is the dominant install base does OTA signing actually protect anyone.
5. Until step 4 completes, treat the OTA channel as a trusted-publisher channel and restrict who can publish to it.

**Current enforcement posture.** `mobile/scripts/check-update-signing.mjs` fails the build when an update channel is enabled without a signing certificate, wired into CI as `npm run check:ota-signing`. `.github/workflows/ci.yml` sets `FUELFLOW_ACK_UNSIGNED_OTA: 'true'`, so the gate currently reports rather than blocks. Delete the four-line `env:` block once signing ships and the gate goes hard automatically. If it is still set on **2026-11-30**, re-review the acceptance rather than letting it become permanent.

---

## FF-05 — Monobank merchant token committed to git history — High, SUSPECTED (live), OPEN

**Location and class.** Commit `3cb50dc197918b771ec912bd21514d9e9c7e6412` → `appsettings.Production.json`, key `Monobank:Token`. Payment-provider merchant API token, 34 characters, entropy 3.75, no placeholder marker. The value is not reproduced anywhere; it is handled by hash and entropy profile only. Every other value in that committed file was verified to be a placeholder or the burned dev secret Production refuses to boot with — one value survives scrutiny, not three.

**Independent corroboration.** `docs/FRAUD_ANALYSIS.md:62` lists as open work: *"Rotate committed Monobank token — Token currently committed; rotate after webhook verification ships."* The project's own documentation says this token was committed and needs rotating.

**Why it stays SUSPECTED rather than CONFIRMED.** Confirming it is live means calling Monobank's API, which the ROE forbids. Confirming it is dead means trusting an attestation. The operator stated on 2026-08-21 that *"all secrets were rotated yesterday"* — recorded as an attestation dated 2026-08-20, unverifiable within this engagement.

**Why it is still open despite the attestation.** The value remains in history, so exposure is permanent for anyone who cloned before rotation. Repository visibility is undetermined — if ever public, the reader population is unbounded. And rotation is exactly the claim that should be verified by whoever can log into the Monobank merchant console.

**Required actions (operator).**
1. Confirm in writing that this specific token was rotated on or after 2026-08-20, and that the old one is revoked **merchant-side** (not merely replaced in config).
2. State whether the repository is public or private, and whether it ever was public.
3. Decide on history: rewrite (`git filter-repo`) and force-push, or formally accept the residue. Accepting is defensible **only** if rotation is confirmed and the repo was never public.
4. The two commits are allowlisted by commit in `.gitleaks.toml` so the scanner's signal stays meaningful; that allowlist is time-bound in-file (re-review by 2026-11-30) and is not a substitute for either decision above.

---

## FF-14 — Backup restore never exercised — Medium, tooling fixed, RESTORE DRILL OPEN

`deploy/backup.sh` and `deploy/restore.sh` exist: `age` asymmetric encryption so the Droplet holds only the public key, off-host upload, and a `pg_restore --list` TOC validation step. What has never happened is a restore. An untested backup is a belief; the failure modes that only appear at restore time (wrong dump format, missing roles, absent extension, silently truncated upload) are exactly the ones that matter.

**Action.** Restore the latest encrypted backup to a scratch database; record archive timestamp, `pg_restore` exit status, and row counts for `Users`, `FuelVouchers`, `Orders` matching production ± expected drift. This is also the project's own launch gate (`docs/FRAUD_ANALYSIS.md:33,36`).

---

## FF-23 — Voucher import runs synchronously in-request — Medium, PARTIAL

A multi-page voucher PDF is rendered and QR-decoded inside the HTTP request, occupying a request thread for minutes. Concurrent imports multiply that; with `WorkerCount = 1` there is no elastic capacity behind it. Bounded today by `ImportConcurrencyGuard` (caps parallel imports so the thread pool cannot be exhausted) and 300 s proxy timeouts at both edges — but a single large import still blocks a thread for minutes.

**Remaining work.** Move import into a Hangfire job: enqueue returns an `ImportId`, the admin SPA polls a status endpoint. This removes the need for the 300 s proxy timeouts, which are themselves a small availability liability. Touches the money-adjacent import path and the admin upload flow — carry it with tests, not unreviewed.

---

## Priorities at a glance

| Tier | Meaning | Items |
|---|---|---|
| **P1** — before go-live | Required for first real deploy | Checklist blockers #1–#5 below (FF-03 token rotation + signing build, restore drill, FF-05 attestation + history decision) |
| **P2** — before scale (>500 users) | Strongly recommended | [FF-23](#ff-23--voucher-import-runs-synchronously-in-request--medium-partial): move import to Hangfire with a status endpoint |
| **P3** — continuous hygiene | Ongoing | Monthly 15-min CVE review of the parse/imaging stack (`Docnet.Core`, `UglyToad.PdfPig`, `SixLabors.ImageSharp`, `ZXing.Net`); mobile's Expo-54 toolchain audit highs — CI gates at critical until an Expo SDK upgrade lands (re-review by 2026-11-30); roadmap hardening from `docs/SECURITY.md` (app attestation, SSL pinning); the open hardening backlog at the end of this file |

---

## Deploy-blocking checklist

Ordered. Each item is independently verifiable by someone other than its author — a command to run or a document to sign.

### Blockers — deploy cannot proceed

| # | Action | Why | How to verify |
|---|---|---|---|
| **1** | Rotate the EAS publish token; restrict who can publish to the OTA channel | [FF-03](#ff-03--expo-ota-updates-are-unsigned--critical-confirmed-open). Until signing ships, the token *is* the trust boundary for the entire installed base | New token issued after 2026-08-22; old token shows revoked in the Expo console; publisher list enumerated in writing |
| **2** | Generate an update signing keypair; add `codeSigningCertificate` to the `updates` block; build and release a new native build to both stores | [FF-03](#ff-03--expo-ota-updates-are-unsigned--critical-confirmed-open). This is the actual fix. **Do not add the config key before the certificate file exists — it breaks the build** | A build from the new binary rejects an unsigned manifest. Private key lives in the EAS secret store, and `git log -- <cert path>` shows no private key was ever committed |
| **3** | Perform a restore drill: restore the latest encrypted backup to a scratch database and compare row counts | [FF-14](#ff-14--backup-restore-never-exercised--medium-tooling-fixed-restore-drill-open). Also this project's own launch gate, `docs/FRAUD_ANALYSIS.md:33,36` | A dated note recording: archive timestamp, `pg_restore` exit 0, and row counts for `Users`, `FuelVouchers`, `Orders` matching production ±expected drift |
| **4** | Confirm in writing that the Monobank merchant token in `3cb50dc` was rotated on or after 2026-08-20 and revoked **merchant-side**, and state whether this repository is or ever was public | [FF-05](#ff-05--monobank-merchant-token-committed-to-git-history--high-suspected-live-open). Replaced-in-config is not revoked. If the repo was ever public, the reader population is unbounded | A dated attestation naming the rotation date, plus the repo's visibility setting and history |
| **5** | Decide git history: rewrite with `git filter-repo` and force-push, **or** formally accept the residue | [FF-05](#ff-05--monobank-merchant-token-committed-to-git-history--high-suspected-live-open). Accepting is defensible only if #4 confirms rotation **and** the repo was never public. Rewriting invalidates every clone and fork | Either the rewritten history verified clean, or a signed acceptance recording both preconditions |

### Required before first real traffic

| # | Action | Why | How to verify |
|---|---|---|---|
| 6 | Set every `:?`-guarded variable in the production `.env`, including `MONOBANK_REDIRECT_URL` | An unset variable must fail the compose run, not boot a broken payment flow | `docker compose -f deploy/docker-compose.prod.yml config` exits 0 with no substitution warnings |
| 7 | Evaluate the **merged** production config (env + `appsettings.Production.json`) as a single unit | Neither file is meaningful alone — `Auth:DevBypass` from either source is a full auth bypass | Boot in Production and confirm `ValidateSecurityConfiguration` passes; capture the startup log and check for any `SECURITY:` warning |
| 8 | Decide device binding explicitly: `DeviceAuth__Enabled=true` **only if** a signing mobile build is released, otherwise `DeviceAuth__AcknowledgeDisabledInProduction=true` | `TODO.md:82` — enabling early locks existing users out of checkout. A recorded decision either way — no third outcome exists, because the app refuses to start | Startup either passes silently (enabled) or logs the `SECURITY: DeviceAuth is disabled` warning |
| 9 | Review and prune `Auth:TestPhones` | Each entry is a permanent, non-rotating OTP for that number — a production credential | The startup warning reports the expected count. If it reports 0, better |
| 10 | Validate the Caddyfile | Edge config changes must be proven valid before they ship | `docker compose exec caddy caddy validate --config /etc/caddy/Caddyfile` exits 0 |
| 11 | Confirm the redis container reaches `healthy` | Healthcheck argv changed | `docker compose ps` shows `redis` as `healthy`, not `starting` or `unhealthy` |
| 12 | Load the admin dashboard and confirm zero CSP violations | The build has 0 inline scripts so `script-src 'self'` should hold — should, verified locally, not observed in production | Browser console shows no `Content Security Policy` errors on login and on every tab |
| 13 | Import a real voucher PDF **larger than 1 MB** through the admin origin | The nginx body-size limit was raised to match the backend; day-one 413s would surface as generic SPA errors | Import completes; no 413 in the nginx access log |
| 14 | Confirm `/health` returns 200 and that something actually watches it | `FRAUD_ANALYSIS.md:34` shows monitoring is `⬜ TODO` | `curl -f https://<host>/health` exits 0, **and** an alert destination is named and tested with a deliberate failure |
| 15 | Verify a Monobank webhook end-to-end in production with the real public key | "Not a placeholder" is not "the correct key" | One real sandbox-or-live payment transitions an order to `PendingFulfillment`; a request with a tampered body is rejected |
| 16 | Confirm the two `.gitleaks.toml` commit allowlist entries are documented and time-bound | The allowlist keeps the scanner's signal meaningful; it must not become permanent amnesia | Both entries carry an in-file comment naming FF-05 and the decision from #5 |

---

## Post-deploy watchlist — week one

What to watch, why, and what number should make someone stop and look. Ordered by how much damage the failure does before you notice it.

### Money and voucher integrity

| Watch | Why | Threshold |
|---|---|---|
| Orders whose voucher count exceeds the paid quantity | Double issuance for one payment. The simulate-endpoint hole is closed, but this is the invariant that matters — check it directly rather than trusting the closure | **Any** occurrence. This should be structurally impossible; one instance means a path nobody found |
| Vouchers assigned twice, or assigned while not `Available` | Advisory lock + conditional `UPDATE ... WHERE status='Available'` (`FulfillmentService.cs:474-481`) should make this impossible. Verify the guarantee in data, not the code | **Any** occurrence |
| Sum of refunds per order vs. the order total | Over-refund is the most direct money-out path. Capped at `RefundOrderCommandHandler.cs:115`, blocked at `:68`, raced-safe at `:175-198` — confirm the invariant in data | Any order where refunds > paid |
| Orders stuck in `PendingFulfillment` | `WorkerCount = 1` with a 1-minute cron. Backlog means the worker is wedged | > 10 minutes old, or count rising across three consecutive checks |
| Expired vouchers delivered to customers | Fixed in fulfilment ordering, but this is the customer-visible symptom and will reach you as a complaint before a metric | Any voucher assigned with `ExpiresAt` in the past |

### Cost — this is where an attacker hurts you cheaply

| Watch | Why | Threshold |
|---|---|---|
| Twilio spend per day, and per phone number | `SmsBudgetGuard` caps daily spend, but a cap being *hit* is the signal | Any day the ceiling is reached, or any single number receiving > 5 messages/hour |
| OTP send:verify ratio | Healthy is near 1:1. A pump shows as sends with no verifies — visible even when each source IP stays under its limit | Sustained > 3:1 over an hour |
| 429 rate by policy name | Many 429s on legitimate traffic means the limiters are too tight; **zero 429s ever** means they may not be engaged | Alert on a step change either way, not on an absolute count |

### Host survival — one Droplet, shared fate

| Watch | Why | Threshold |
|---|---|---|
| Per-container memory against the `mem_limit`s | Limits were sized by reasoning, not observed under real load | Any container sustained > 80% of its limit |
| OOM kills (`dmesg` / Docker events) | Losing the API is an outage; losing Postgres mid-write is a data problem | **Any** occurrence |
| Disk free | Log rotation is configured but unobserved | < 20% free |
| `OutboxEvents` row count and growth rate | Reads are bounded, but the table still grows forever — it needs a retention policy before it needs one urgently | Growth without bound after week one → schedule pruning |

### Auth and abuse

| Watch | Why | Threshold |
|---|---|---|
| Failed device-signature rejections on `/api/purchases` | If enforcement is on, a spike means either an attack or an unreleased client version | Any sustained rate above baseline |
| Requests to `/api/purchases/` **with** a trailing slash | Nothing legitimate sends this. It is the probe signature of the old trailing-slash bypass | **Any** occurrence — treat as targeted, not accidental |
| 404s from the admin origin's `$fuelflow_api_allowed` deny path | Attempts to reach the API through the admin hostname — or an admin SPA feature missed in the allow-list | Any occurrence: check whether it is an attacker or a broken dashboard feature |
| Refresh attempts rejected on Origin | The CSRF defence for a `SameSite=None` refresh cookie (`AuthController.cs:127-128`) | Any sustained rate |
| `Auth:TestPhones` logins | Each is a permanent password for that number | Any login from a test phone that you did not perform |
| Startup logs on every deploy | `ValidateSecurityConfiguration` warnings are the only signal that DeviceAuth is off or test phones are live | Grep every deploy's startup log for `SECURITY:` |

### Supply chain

| Watch | Why | Threshold |
|---|---|---|
| Unexpected OTA publishes | [FF-03](#ff-03--expo-ota-updates-are-unsigned--critical-confirmed-open) is open. Until signing ships this log is the **only** detection for the highest-severity finding | Any publish not matching a planned release. Review daily, not weekly |
| `FUELFLOW_ACK_UNSIGNED_OTA` in `.github/workflows/ci.yml` | The FF-03 gate exists but reports rather than blocks via that flag. Delete the four-line `env:` block the moment signing ships. If still set on **2026-11-30**, re-review the acceptance | — |
| CI gitleaks results | Green for the right reasons now. A *new* red is real; a green after someone edits the config is not automatically trustworthy | Any red → treat as a real leak until proven otherwise. Any `.gitleaks.toml` change → review as a security change |

**One structural note:** most items above are database queries or log greps nobody is currently running, because monitoring (`docs/FRAUD_ANALYSIS.md:34`) is `⬜ TODO`. A watchlist with no monitoring is a to-do list. The highest-leverage week-one action is alerting on the four "**Any** occurrence" thresholds — double issuance, double assignment, OOM kills, unexpected OTA publishes — because each is silent until it is expensive.

---

## Standing coverage caveats

Read before treating any closure in this report as an all-clear.

### Not audited at all

| Area | What was not examined | Why it matters |
|---|---|---|
| **Device register / challenge / verify handlers** | Whether signature verification *runs* was audited (and fixed); the enrolment protocol itself was not | If a device can be registered against another user's account, or a challenge replayed, device binding protects a broken primitive. **The most consequential gap here** |
| **Company invitation → member → gift / recall flows** | Tenancy isolation on multi-user company features | Cross-tenant purchase isolation via `LegalEntityId` was verified; the invitation and gifting graph was not. An IDOR here would be a High |
| **Referral system** | Self-referral, referral loops, reward double-claim | Direct money-out path if rewards are financial |
| **Admin SPA internals** | `zustand persist` token storage, `dangerouslySetInnerHTML` usage, which `VITE_*` values reach the shipped bundle | Token in localStorage turns any admin-origin XSS into full admin takeover. The edge and exports were hardened, not the internals |
| **Mobile app internals** | secure-store consistency, `EXPO_PUBLIC_*` secrets in the bundle, `fuelflow://payment-result` deep-link trust, biometric gate, cert pinning, `__DEV__` branches in release | `FRAUD_ANALYSIS.md:188` raises a mobile signing-key concern that remains **UNVERIFIED**. Look at the deep-link handler first: if trusted as a payment outcome rather than a navigation hint, that is a money finding |
| **Refund 24-hour timeout job** | `RefundStatusSyncService` reconciliation logic | The refund command is well guarded; the background reconciliation running every minute was not audited |
| **Effective merged production config** | env + `appsettings.Production.json` evaluated as one unit | Cannot be done until the production `.env` exists; startup-time validation substitutes a runtime gate, not an equivalent. Checklist #7 |

### Constraints that limit conclusions

- **Pre-rewrite git objects may be unrecoverable.** Evidence of a past filter-style rewrite exists (empty `.git/refs/original`). Reachable *and* unreachable objects were swept clean, but objects orphaned by that rewrite and garbage-collected since are gone and unauditable. Rotation, not scanning, is the mitigation — hence FF-05's attestation requirement.
- **Repository visibility is undetermined**, materially changing FF-05's severity: private-and-rotated is hygiene; ever-public is an incident. Checklist #4.
- **Gitleaks commit-count discrepancy never reconciled.** The tool reported 672 commits scanned; `git rev-list --all --count` reports thousands more. Treat the tool's figure as its own claim. The independent blob-level sweep of the whole object database is the stronger evidence and stands.
- **Gitleaks ran in git mode only.** Uncommitted or gitignored files in a working tree have never been scanned (`--no-git` timed out during the audit and was not retried).
- **Static analysis throughout.** No deployed host, database, Monobank endpoint or Twilio account was contacted. Every closure means *the code contains the guard and tests pass*, not *the guard was observed stopping a live attack*. Concurrency guarantees were verified by reading SQL semantics plus the executed WP-4 integration tests, not by production observation.

---

## Open hardening (no demonstrated exploit — backlog, not gates)

1. **Project DTOs instead of EF entities across the rest of the API.** Done for the anonymous station/package endpoints. Returning an entity makes the response contract whatever the database model is *today*; adding a sensitive column then publishes it with no endpoint change and nothing for review to flag.
2. **Add a retention policy for `OutboxEvents`.** Reads are bounded; the table still grows forever. Prune processed events past a window before size becomes an incident.
3. **`SameSite=Strict` or a CSRF token for refresh.** Currently defended by a single Origin string-comparison (`AuthController.cs:127-128`). Prefer Strict if flows permit; otherwise a double-submit token is a cheap second layer.
4. **Raise `WorkerCount` above 1, deliberately.** One slow job currently stalls all fulfilment. If raised, the advisory-lock and conditional-update guarantees become load-bearing — change it with concurrency tests to match.
5. **Emit startup security posture as structured output.** `ValidateSecurityConfiguration` warnings scroll past; one structured event (device binding on/off, test-phone count, DevBypass state) makes posture queryable and alertable.

---

## Appendix — refuted hypotheses

Recorded so the same ground is not re-litigated, and so the load-bearing controls are named. A report listing only what is broken gives a false picture of a codebase whose money paths are, in most respects, carefully built.

### Money

| Hypothesis | Why it failed | Guard |
|---|---|---|
| Client-controlled checkout price — buy a 2,000 ₴ voucher for 1 ₴ | Server never reads a price from the request; recomputes from `FuelPackages` | `ServerPricing.cs` |
| Forge a Monobank "paid" webhook | ECDSA secp256k1 / SHA-256 over the **raw** body; Production refuses to boot on a placeholder public key | `ProcessMonobankWebhookCommandHandler.cs`, `Program.cs:177-185` |
| Replay a legitimate webhook to double-fulfil | Terminal states enforced — `Fulfilled`/`Cancelled` cannot be re-entered from a webhook | `OrderStateMachine` |
| Tamper with the amount in a webhook body | Any change invalidates the signature; amount also reconciled against the order | Same handler |
| Over-refund beyond what was paid | Refundable balance derived server-side, `Math.Min` caps the request | `RefundOrderCommandHandler.cs:109,115` |
| Double-refund | Existing refund row in any state other than `Failed` blocks a second attempt | `RefundOrderCommandHandler.cs:62-64,68` |
| Negative-amount refund to *add* money | `if (amount <= 0)` → `"NothingToRefund"` | `RefundOrderCommandHandler.cs:117-126` |
| Race two concurrent refunds | Unique index on `OrderId`; second insert fails and is handled | `RefundOrderCommandHandler.cs:175-198` |
| Refund without admin rights | Controller-level role requirement | `AdminOrderController.cs` |

Five independent guards make refunds the best-defended surface examined, including one handling the concurrent case correctly rather than by hoping.

### Vouchers

| Hypothesis | Why it failed | Guard |
|---|---|---|
| Two orders assigned the same voucher | Advisory lock plus atomic conditional `UPDATE ... WHERE status = 'Available'`; second update affects 0 rows and is handled | `FulfillmentService.cs:474-481` |
| Mint duplicate vouchers via import | Unique indexes on `VoucherNumber` and `QrPayload` | Schema |
| Race `mark-used` to redeem twice | Atomic conditional `ExecuteUpdateAsync` with the `Assigned` predicate in the `WHERE`; loser is told the true outcome. Verified by executed integration tests plus a failing negative control against the pre-fix handler | `MarkVoucherAsUsedCommandHandler` |
| Admin voucher changes go unaudited | `RecordEventAsync` in every state-changing handler; no separate assign endpoint bypassing audit | Handlers under `Features/Vouchers` |

### Authentication and tenancy

| Hypothesis | Why it failed | Guard |
|---|---|---|
| Buy against another company via its `LegalEntityId` | Membership verified server-side against the authenticated principal | Checkout handler |
| CSRF the refresh endpoint (`SameSite=None` cookie) | Origin allow-list rejects cross-origin refresh | `AuthController.cs:127-128` |
| CSRF the Hangfire dashboard | Dashboard does not use cookie auth — no ambient credential to ride; unauthenticated bypass refused in Production | `HangfireDashboardAuthorizationFilter`, `Program.cs` |
| Reuse a rotated refresh token | Rotation with family revocation | `RefreshTokenCommand.cs` |
| Boot production with the burned placeholder JWT secret | Startup refusal on the exact placeholder constant | `AuthSetup.cs` |

### Secrets

| Hypothesis | Why it failed |
|---|---|
| Live Supabase database password in git history | **REFUTED.** Cited commits do not exist; a sweep of all 3,766 blobs found exactly one value in the password position across ten occurrences — the literal `***REDACTED***`. Proves the current object database is clean, not that no live value ever reached a remote — that is FF-05's attestation question |
| Live JWT signing secret in `3cb50dc:appsettings.Production.json` | Template file; the value is exactly `PlaceholderSecret`, refused at Production startup. Burned, not exploitable |
| Live Twilio credential in history | False positive — a `process.env` reference, not a value |
| `Session:Secret` at `72492dc` live | Belongs to the retired admin-panel backend; consumer no longer exists |
| Secrets in objects orphaned by the past rewrite | All 10 unreachable blobs read with values masked: env-var names, Key Vault secret-*name* constants, config references. No values. Caveat: gc'd pre-rewrite objects are unrecoverable — see constraints above |

### Infrastructure

| Hypothesis | Why it failed | Guard |
|---|---|---|
| `/health` reports healthy with a dead database | Probes `GetPendingMigrationsAsync()`, returns 503 on failure | `PipelineSetup.cs` |
| `[ResponseCache]` on a money endpoint serves stale prices | Removed from `PackageController` with rationale recorded; remaining attributes only on anonymous reference data | `PackageController.cs` |
| Middleware ordering lets a request reach checkout before authorization or signing | Order pinned: forwarded headers → authentication → rate limiter → session validation → authorization → device signature | `PipelineSetup.cs`, `PipelineOrderConventionTests` |
| No request body size limits | 25 MB at both `FormOptions.MultipartBodyLengthLimit` and Kestrel | `Program.cs` |
| CI downloads tools without integrity verification, or pins mutable tags | Gitleaks pinned with enforced SHA-256; all action refs pinned to commit SHAs | `.github/workflows/ci.yml` |
