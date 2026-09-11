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

> **Status pass 2026-09-11** (post-go-live; production has been running on the single Hetzner
> server since 2026-08-31, deployed by CI). Findings and the deploy checklist were re-verified
> against the current code and the live server:
>
> - **FF-03 is resolved by a different means than signing**: `mobile/app.json` now ships
>   `expo.updates.enabled = false` (landed in commit `931b465`, build 121 — the binary currently
>   in the stores). No installed build fetches OTA updates, so the remote-JS-substitution attack
>   path is closed. Signing was never implemented; the CI gate now passes hard (no
>   acknowledgment needed) and will block any re-enable that is not signed. Residual actions are
>   recorded in the FF-03 section.
> - **FF-05**: repository visibility was previously "undetermined" — it is confirmed **private**
>   (GitHub API, 2026-09-11). The attestation and history decision remain open.
> - **FF-14**: still open — no restore drill has been performed (no scratch database exists on
>   the server, no drill note exists; 3 encrypted dumps on the server, off-box copy not configured).
> - **FF-23**: unchanged.
> - Deploy checklist: items 2, 6–13, 15, 16 verified **done** (evidence in the Status column);
>   item 1 is superseded by the channel disable but stays valid as hygiene; item 14 is **partial**
>   — nothing external watches `/health`.
> - The NO GO verdict above is historical: it blocked the first production deploy, which has since
>   happened with the blockers either closed (FF-03) or consciously carried (FF-05, FF-14). This
>   file now tracks the remaining residue only.

---

## Open findings

| ID | Title | Sev | Status |
|----|-------|-----|--------|
| FF-03 | Expo OTA updates are unsigned | Critical | **RESOLVED 2026-09-11** — update channel disabled in shipped builds (`expo.updates.enabled=false`, build 121). Signing not implemented; see residual actions |
| FF-05 | Monobank merchant token committed to git history | High | **OPEN** — owner attestation + history decision (repo visibility confirmed private) |
| FF-14 | Backup restore never exercised | Medium | **OPEN** — restore drill (encryption tooling itself is fixed) |
| FF-23 | Voucher import runs synchronously in-request | Medium | **PARTIAL** — bounded by `ImportConcurrencyGuard`; background-job rewrite deferred |

---

## FF-03 — Expo OTA updates are unsigned — Critical, CONFIRMED, RESOLVED (channel disabled)

**Attack (historical).** The mobile app ships with `expo-updates`, which fetches a JavaScript bundle at launch and runs it. Update code signing was not enabled, so the client accepted whatever bundle the update server served and verified only that it came over TLS. Anyone who obtained the EAS publish token could publish a bundle of their choosing to every installed app — the checkout screen, the OTP entry field, the deep-link handler for `fuelflow://payment-result` — with no store review and no user action required.

**Resolution (2026-09-11).** `mobile/app.json` now declares `"updates": { "enabled": false, ... }`
(commit `931b465`, which also produced build 121 — the binary currently in the stores). A build
compiled with the channel disabled never contacts the update server, so the shipped install base
has **no OTA path at all**. `mobile/scripts/check-update-signing.mjs` passes hard in this state
(no `FUELFLOW_ACK_UNSIGNED_OTA` needed) and hard-fails if the channel is ever re-enabled without a
`codeSigningCertificate`.

**Residual actions (still worth doing):**

1. **Rotate the EAS publish token** and audit which CI logs and machines have held it. With the
   channel disabled the token can no longer push code to current installs, but any *pre-121*
   TestFlight/internal builds compiled while the channel was enabled would still accept an update
   if one were published. Rotation closes that for good.
2. **Keep the channel disabled until signing ships.** If OTA is ever wanted again: generate a
   keypair, embed the certificate in a new native build, release it, and only then flip
   `enabled` — the CI gate enforces exactly this.
3. **Optional cleanup:** the `FUELFLOW_ACK_UNSIGNED_OTA` env block in `.github/workflows/ci.yml`
   is now vestigial (the gate passes without acknowledgment while `enabled: false`). Per the
   gate's own instruction, the env block can be deleted; the gate stays as the re-enable guard.

---

## FF-05 — Monobank merchant token committed to git history — High, SUSPECTED (live), OPEN

**Location and class.** Commit `3cb50dc197918b771ec912bd21514d9e9c7e6412` → `appsettings.Production.json`, key `Monobank:Token`. Payment-provider merchant API token, 34 characters, entropy 3.75, no placeholder marker. The value is not reproduced anywhere; it is handled by hash and entropy profile only. Every other value in that committed file was verified to be a placeholder or the burned dev secret Production refuses to boot with — one value survives scrutiny, not three.

**Independent corroboration.** The project's own (now-archived) fraud analysis, `docs/FRAUD_ANALYSIS.md:62` in git history, listed as open work: *"Rotate committed Monobank token — Token currently committed; rotate after webhook verification ships."*

**Why it stays SUSPECTED rather than CONFIRMED.** Confirming it is live means calling Monobank's API, which the ROE forbids. Confirming it is dead means trusting an attestation. The operator stated on 2026-08-21 that *"all secrets were rotated yesterday"* — recorded as an attestation dated 2026-08-20, unverifiable within this engagement.

**Why it is still open despite the attestation.** The value remains in history, so exposure is permanent for anyone who cloned before rotation. **Repository visibility (previously undetermined) is now confirmed: the repository is private** (GitHub API, 2026-09-11). Whether it was *ever* public is still only answerable by the owner. And rotation is exactly the claim that should be verified by whoever can log into the Monobank merchant console.

**Required actions (operator).**
1. Confirm in writing that this specific token was rotated on or after 2026-08-20, and that the old one is revoked **merchant-side** (not merely replaced in config).
2. Confirm whether the repository was ever public (current state: private).
3. Decide on history: rewrite (`git filter-repo`) and force-push, or formally accept the residue. Accepting is defensible **only** if rotation is confirmed and the repo was never public.
4. The two commits are allowlisted by commit in `.gitleaks.toml` so the scanner's signal stays meaningful; that allowlist is time-bound in-file (re-review by 2026-11-30) and is not a substitute for either decision above.

---

## FF-14 — Backup restore never exercised — Medium, tooling fixed, RESTORE DRILL OPEN

`deploy/backup.sh` and `deploy/restore.sh` exist: `age` asymmetric encryption so the server holds only the public key, off-host upload, and a `pg_restore --list` TOC validation step. What has never happened is a restore. An untested backup is a belief; the failure modes that only appear at restore time (wrong dump format, missing roles, absent extension, silently truncated upload) are exactly the ones that matter.

**Re-verified 2026-09-11:** the nightly cron is active (03:20) and 3 encrypted dumps exist in `/root/fuelflow-backups/`, but there is no scratch database on the server and no drill note — the drill has not been performed. The dumps also still live **only on the server** (`BACKUP_REMOTE` unset), so they do not survive server loss.

**Action.** Restore the latest encrypted backup to a scratch database; record archive timestamp, `pg_restore` exit status, and row counts for `Users`, `FuelVouchers`, `Orders` matching production ± expected drift. Then set `BACKUP_REMOTE` so dumps leave the server.

---

## FF-23 — Voucher import runs synchronously in-request — Medium, PARTIAL

A multi-page voucher PDF is rendered and QR-decoded inside the HTTP request, occupying a request thread for minutes. Concurrent imports multiply that; with `WorkerCount = 1` there is no elastic capacity behind it. Bounded today by `ImportConcurrencyGuard` (caps parallel imports so the thread pool cannot be exhausted) and 300 s proxy timeouts at both edges — but a single large import still blocks a thread for minutes. Re-verified 2026-09-11: the guard is in place, the import path is unchanged.

**Remaining work.** Move import into a Hangfire job: enqueue returns an `ImportId`, the admin SPA polls a status endpoint. This removes the need for the 300 s proxy timeouts, which are themselves a small availability liability. Touches the money-adjacent import path and the admin upload flow — carry it with tests, not unreviewed.

---

## Priorities at a glance

| Tier | Meaning | Items |
|---|---|---|
| **P1** — carried into production, close deliberately | Open residue from the go-live decision | FF-05 attestation + history decision; FF-14 restore drill + `BACKUP_REMOTE`; FF-03 residual EAS-token rotation |
| **P2** — before scale (>500 users) | Strongly recommended | [FF-23](#ff-23--voucher-import-runs-synchronously-in-request--medium-partial): move import to Hangfire with a status endpoint; **alerting depth**: UptimeRobot covers API up/down, the on-server Prometheus/Grafana/Loki stack (see `docs/DEPLOYMENT.md` → "Logs and monitoring") adds memory/fulfillment/voucher-pool/error-burst alerts to Telegram |
| **P3** — continuous hygiene | Ongoing | Monthly 15-min CVE review of the parse/imaging stack (`Docnet.Core`, `UglyToad.PdfPig`, `SixLabors.ImageSharp`, `ZXing.Net`); mobile's Expo-54 toolchain audit highs — CI gates at critical until an Expo SDK upgrade lands (re-review by 2026-11-30); roadmap hardening from `docs/SECURITY.md` (app attestation, SSL pinning); the open hardening backlog at the end of this file |

---

## Deploy-blocking checklist

Ordered. Each item is independently verifiable by someone other than its author — a command to run or a document to sign.

**Status 2026-09-11:** the first production deploy happened 2026-08-31. Items 2, 6–13, 15 and 16 were executed and verified against the live server (Status column). Item 1 is superseded but remains hygiene; items 3–5 map to FF-14/FF-05; item 14 is partial. The table is kept because CI cites "blockers 1 and 2" by number.

### Blockers — deploy cannot proceed

| # | Action | Why | How to verify | Status 2026-09-11 |
|---|---|---|---|---|
| **1** | Rotate the EAS publish token; restrict who can publish to the OTA channel | [FF-03](#ff-03--expo-ota-updates-are-unsigned--critical-confirmed-resolved-channel-disabled). Until signing ships, the token *is* the trust boundary for the entire installed base | New token issued after 2026-08-22; old token shows revoked in the Expo console; publisher list enumerated in writing | **Superseded** by the channel disable (current installs have no OTA path); rotation still recommended — see FF-03 residual actions |
| **2** | Generate an update signing keypair; add `codeSigningCertificate` to the `updates` block; build and release a new native build to both stores | [FF-03](#ff-03--expo-ota-updates-are-unsigned--critical-confirmed-resolved-channel-disabled). This is the actual fix. **Do not add the config key before the certificate file exists — it breaks the build** | A build from the new binary rejects an unsigned manifest. Private key lives in the EAS secret store, and `git log -- <cert path>` shows no private key was ever committed | **Done by different means** — `updates.enabled=false` shipped in build 121; channel closed instead of signed |
| **3** | Perform a restore drill: restore the latest encrypted backup to a scratch database and compare row counts | [FF-14](#ff-14--backup-restore-never-exercised--medium-tooling-fixed-restore-drill-open). Also this project's own launch gate (fraud analysis, git history) | A dated note recording: archive timestamp, `pg_restore` exit 0, and row counts for `Users`, `FuelVouchers`, `Orders` matching production ±expected drift | **OPEN** (FF-14) — no scratch DB, no drill note on the server |
| **4** | Confirm in writing that the Monobank merchant token in `3cb50dc` was rotated on or after 2026-08-20 and revoked **merchant-side**, and state whether this repository is or ever was public | [FF-05](#ff-05--monobank-merchant-token-committed-to-git-history--high-suspected-live-open). Replaced-in-config is not revoked. If the repo was ever public, the reader population is unbounded | A dated attestation naming the rotation date, plus the repo's visibility setting and history | **OPEN** (FF-05) — repo confirmed private 2026-09-11; attestation + ever-public answer still owed |
| **5** | Decide git history: rewrite with `git filter-repo` and force-push, **or** formally accept the residue | [FF-05](#ff-05--monobank-merchant-token-committed-to-git-history--high-suspected-live-open). Accepting is defensible only if #4 confirms rotation **and** the repo was never public. Rewriting invalidates every clone and fork | Either the rewritten history verified clean, or a signed acceptance recording both preconditions | **OPEN** (FF-05) |

### Required before first real traffic

| # | Action | Why | How to verify | Status 2026-09-11 |
|---|---|---|---|---|
| 6 | Set every `:?`-guarded variable in the production `.env`, including `MONOBANK_REDIRECT_URL` | An unset variable must fail the compose run, not boot a broken payment flow | `docker compose -f deploy/docker-compose.prod.yml config` exits 0 with no substitution warnings | **DONE** — stack boots; `MONOBANK__REDIRECT_URL=fuelflow://payment-result` confirmed in the running container |
| 7 | Evaluate the **merged** production config (env + `appsettings.Production.json`) as a single unit | Neither file is meaningful alone — `Auth:DevBypass` from either source is a full auth bypass | Boot in Production and confirm `ValidateSecurityConfiguration` passes; capture the startup log and check for any `SECURITY:` warning | **DONE** — container boots in Production; no startup `SECURITY:` posture warnings in logs (only runtime refresh-reuse detections, which are the control working) |
| 8 | Decide device binding explicitly: `DeviceAuth__Enabled=true` **only if** a signing mobile build is released, otherwise `DeviceAuth__AcknowledgeDisabledInProduction=true` | Enabling early locks existing users out of checkout. A recorded decision either way — no third outcome exists, because the app refuses to start | Startup either passes silently (enabled) or logs the `SECURITY: DeviceAuth is disabled` warning | **DONE** — signatures left **on**; current builds sign checkout (no 401s from signed clients observed) |
| 9 | Review and prune `Auth:TestPhones` | Each entry is a permanent, non-rotating OTP for that number — a production credential | The startup warning reports the expected count. If it reports 0, better | **DONE (review)** — `AUTH_TEST_PHONES` set in `deploy/.env`; zero test-phone logins observed in production logs. Re-check the list when the testing period ends |
| 10 | Validate the Caddyfile | Edge config changes must be proven valid before they ship | `docker compose exec caddy caddy validate --config /etc/caddy/Caddyfile` exits 0 | **DONE** — `Valid configuration` on the live container |
| 11 | Confirm the redis container reaches `healthy` | Healthcheck argv changed | `docker compose ps` shows `redis` as `healthy`, not `starting` or `unhealthy` | **DONE** — `fuelflow-redis` healthy (up since deploy) |
| 12 | Load the admin dashboard and confirm zero CSP violations | The build has 0 inline scripts so `script-src 'self'` should hold — should, verified locally, not observed in production | Browser console shows no `Content Security Policy` errors on login and on every tab | **DONE (at the edge)** — CSP header live on `app.palne.shop` with `script-src 'self'`; HSTS + `X-Frame-Options: DENY` present. A manual browser-console pass on every tab was not performed |
| 13 | Import a real voucher PDF **larger than 1 MB** through the admin origin | The nginx body-size limit was raised to match the backend; day-one 413s would surface as generic SPA errors | Import completes; no 413 in the nginx access log | **DONE** — 203 vouchers imported in production |
| 14 | Confirm `/health` returns 200 and that something actually watches it | Monitoring was `⬜ TODO` in the fraud analysis | `curl -f https://<host>/health` exits 0, **and** an alert destination is named and tested with a deliberate failure | **DONE (external) / depth pending** — correction 2026-09-11 (evening): UptimeRobot has been watching `/health` every 5 minutes with owner alerts since go-live (up 11 days at review time). The on-server Prometheus/Grafana/Loki stack (`deploy/docker-compose.observability.yml`) adds host/memory/business alerts — see DEPLOYMENT.md |
| 15 | Verify a Monobank webhook end-to-end in production with the real public key | "Not a placeholder" is not "the correct key" | One real sandbox-or-live payment transitions an order to `PendingFulfillment`; a request with a tampered body is rejected | **DONE** — real payments flow: 6 orders `Fulfilled` in production (webhook → fulfillment), positive path proven; tampered-body rejection is enforced in code with tests |
| 16 | Confirm the two `.gitleaks.toml` commit allowlist entries are documented and time-bound | The allowlist keeps the scanner's signal meaningful; it must not become permanent amnesia | Both entries carry an in-file comment naming FF-05 and the decision from #5 | **DONE** — allowlist documented in-file, re-review by 2026-11-30 |

---

## Post-deploy watchlist — week one

What to watch, why, and what number should make someone stop and look. Ordered by how much damage the failure does before you notice it.

> **Note 2026-09-11:** "week one" has passed; these remain the standing indicators. External uptime is watched by UptimeRobot (5-min, alerting the owner); the on-server Prometheus/Grafana/Loki stack (see DEPLOYMENT.md) covers memory/fulfillment/voucher-pool/error-burst alerts. The structural note at the bottom narrows accordingly: the **money-integrity watchlist queries** (double issuance, double assignment, refund totals) are still manual.

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
| SMS spend per day, and per phone number (SMS Club primary, Twilio fallback) | `SmsBudgetGuard` caps daily spend, but a cap being *hit* is the signal | Any day the ceiling is reached, or any single number receiving > 5 messages/hour |
| OTP send:verify ratio | Healthy is near 1:1. A pump shows as sends with no verifies — visible even when each source IP stays under its limit | Sustained > 3:1 over an hour |
| 429 rate by policy name | Many 429s on legitimate traffic means the limiters are too tight; **zero 429s ever** means they may not be engaged | Alert on a step change either way, not on an absolute count |

### Host survival — one server, shared fate

| Watch | Why | Threshold |
|---|---|---|
| Per-container memory against the `mem_limit`s | Limits were sized by reasoning, not observed under real load | Any container sustained > 80% of its limit |
| OOM kills (`dmesg` / Docker events) | Losing the API is an outage; losing Postgres mid-write is a data problem | **Any** occurrence |
| Disk free | Log rotation is configured but unobserved | < 20% free |
| `OutboxEvents` row count and growth rate | Reads are bounded, but the table still grows forever — it needs a retention policy before it needs one urgently | Growth without bound after week one → schedule pruning (14 rows as of 2026-09-11 — no pressure yet) |

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
| Unexpected OTA publishes | [FF-03](#ff-03--expo-ota-updates-are-unsigned--critical-confirmed-resolved-channel-disabled) is resolved by the channel being disabled; current builds never contact the update server. The only exposure is pre-121 builds, which rotation of the EAS token closes | Any publish not matching a planned release (only relevant if the channel is ever re-enabled) |
| `FUELFLOW_ACK_UNSIGNED_OTA` in `.github/workflows/ci.yml` | Now vestigial: the FF-03 gate passes hard while `expo.updates.enabled=false`. The env block can be deleted; the gate stays as the re-enable guard | — |
| CI gitleaks results | Green for the right reasons now. A *new* red is real; a green after someone edits the config is not automatically trustworthy | Any red → treat as a real leak until proven otherwise. Any `.gitleaks.toml` change → review as a security change |

**One structural note:** most items above are database queries or log greps nobody is currently running. UptimeRobot covers "is the API up"; the on-server Prometheus/Grafana/Loki stack covers process and business metrics (memory, fulfillment failures, voucher pool, error bursts — see DEPLOYMENT.md → "Logs and monitoring"). What still has no automation is the **data-integrity row set** — double issuance, double assignment, refund-vs-total — because they are reconciliation SQL queries, not scrapeable metrics. The highest-leverage remaining action is scheduling those three queries daily and alerting on **any** occurrence, because each is silent until it is expensive.

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
| **Mobile app internals** | secure-store consistency, `EXPO_PUBLIC_*` secrets in the bundle, `fuelflow://payment-result` deep-link trust, biometric gate, cert pinning, `__DEV__` branches in release | The archived fraud analysis (git history) raised a mobile signing-key concern that remains **UNVERIFIED**. Look at the deep-link handler first: if trusted as a payment outcome rather than a navigation hint, that is a money finding |
| **Refund 24-hour timeout job** | `RefundStatusSyncService` reconciliation logic | The refund command is well guarded; the background reconciliation running every minute was not audited |
| ~~**Effective merged production config**~~ | ~~env + `appsettings.Production.json` evaluated as one unit~~ | **Closed 2026-09-11**: production has booted since 2026-08-31 with `ValidateSecurityConfiguration` passing and no startup posture warnings (checklist #7) |

### Constraints that limit conclusions

- **Pre-rewrite git objects may be unrecoverable.** Evidence of a past filter-style rewrite exists (empty `.git/refs/original`). Reachable *and* unreachable objects were swept clean, but objects orphaned by that rewrite and garbage-collected since are gone and unauditable. Rotation, not scanning, is the mitigation — hence FF-05's attestation requirement.
- **Repository visibility:** was "undetermined"; **now confirmed private** (2026-09-11). The ever-public question remains with the owner — checklist #4.
- **Gitleaks commit-count discrepancy never reconciled.** The tool reported 672 commits scanned; `git rev-list --all --count` reports thousands more. Treat the tool's figure as its own claim. The independent blob-level sweep of the whole object database is the stronger evidence and stands.
- **Gitleaks ran in git mode only.** Uncommitted or gitignored files in a working tree have never been scanned (`--no-git` timed out during the audit and was not retried).
- **Static analysis throughout.** No deployed host, database, Monobank endpoint or Twilio account was contacted. Every closure means *the code contains the guard and tests pass*, not *the guard was observed stopping a live attack*. Concurrency guarantees were verified by reading SQL semantics plus the executed WP-4 integration tests, not by production observation.

---

## Open hardening (no demonstrated exploit — backlog, not gates)

Re-verified 2026-09-11 — all five still open.

1. **Project DTOs instead of EF entities across the rest of the API.** Done for the anonymous station/package endpoints. Returning an entity makes the response contract whatever the database model is *today*; adding a sensitive column then publishes it with no endpoint change and nothing for review to flag.
2. **Add a retention policy for `OutboxEvents`.** Reads are bounded; the table still grows forever. Prune processed events past a window before size becomes an incident. (14 rows in production as of 2026-09-11 — no pressure yet.)
3. **`SameSite=Strict` or a CSRF token for refresh.** Currently defended by a single Origin string-comparison (`AuthController.cs:127-128`). Prefer Strict if flows permit; otherwise a double-submit token is a cheap second layer.
4. **Raise `WorkerCount` above 1, deliberately.** One slow job currently stalls all fulfilment. If raised, the advisory-lock and conditional-update guarantees become load-bearing — change it with concurrency tests to match. (Still `1` in both `FuelFlow.API/Program.cs:81` and `FuelFlow.JobsWorker/Program.cs:107`.)
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
