# 7. Post-deploy watchlist — week one

What to watch, why it is on the list, and what number should make someone stop and look. A watchlist without thresholds is a wish, so each item names one.

Ordered by how much damage the failure does before you notice it.

---

## Money and voucher integrity

| Watch | Why | Threshold |
|---|---|---|
| Orders whose voucher count exceeds the paid quantity | The [FF-06](02-findings-critical-high.md#ff-06--apipurchasessimulate-production-reachable-and-bypassed-the-order-state-machine--high-confirmed-fixed) class of failure — double issuance for one payment. The endpoint is closed, but this is the invariant that matters, and it should be checked directly rather than trusted | **Any** occurrence. This should be structurally impossible; one instance means a path I did not find |
| Vouchers assigned twice, or assigned while not `Available` | The advisory lock + conditional `UPDATE ... WHERE status='Available'` (`FulfillmentService.cs:474-481`) should make this impossible. Verify the guarantee rather than the code | **Any** occurrence |
| Sum of refunds per order vs. the order total | Over-refund is the most direct money-out path that survives an authenticated attacker. Capped at `RefundOrderCommandHandler.cs:115`, blocked at `:68`, raced-safe at `:175-198` — confirm the invariant in data, not in code | Any order where refunds > paid |
| Orders stuck in `PendingFulfillment` | `WorkerCount = 1` (`Program.cs:67`) with a 1-minute cron. Backlog means the worker is wedged or [FF-25](03-findings-medium-low.md#ff-25--three-unbounded-outboxevents-full-table-loads--medium-confirmed-fixed) is biting | > 10 minutes old, or count rising across three consecutive checks |
| Expired vouchers delivered to customers | [FF-24](03-findings-medium-low.md#ff-24--expired-vouchers-preferentially-assigned-to-paying-customers--medium-confirmed-fixed) is fixed, but this is the customer-visible symptom and it will reach you as a complaint before it reaches you as a metric | Any voucher assigned with `ExpiresAt` in the past |

## Cost — this is where an attacker hurts you cheaply

| Watch | Why | Threshold |
|---|---|---|
| Twilio spend per day, and per phone number | [FF-07](02-findings-critical-high.md#ff-07--per-phone-otp-rate-limit-never-functioned--high-confirmed-fixed): the per-phone limit never functioned at all. `SmsBudgetGuard` now caps daily spend, but a cap being *hit* is the signal | Any day the `SmsBudgetGuard` ceiling is reached, or any single number receiving > 5 messages/hour |
| OTP send:verify ratio | A healthy ratio is near 1:1. A pump shows as sends with no verifies — visible even when each source IP stays under its limit | Sustained > 3:1 over an hour |
| 429 rate by policy name | Tells you whether [FF-26](03-findings-medium-low.md#ff-26--no-global-rate-limiter--medium-confirmed-fixed)'s global limiter is set correctly. Both directions matter: many 429s on legitimate traffic means it is too tight; **zero 429s ever** means it may not be engaged | Alert on a step change either way, not on an absolute count |

## Host survival — one Droplet, shared fate

| Watch | Why | Threshold |
|---|---|---|
| Per-container memory against the new `mem_limit`s | [FF-12](03-findings-medium-low.md#ff-12--no-container-memory-limits-or-log-rotation--medium-confirmed-fixed). The limits are new and were sized by reasoning, not by observation under real load | Any container sustained > 80% of its limit |
| OOM kills (`dmesg` / Docker events) | Losing the API is an outage; losing Postgres mid-write is a data problem | **Any** occurrence |
| Disk free | Log rotation is configured but unobserved; a full disk on a Postgres host is its own incident | < 20% free |
| `OutboxEvents` row count and growth rate | [FF-25](03-findings-medium-low.md#ff-25--three-unbounded-outboxevents-full-table-loads--medium-confirmed-fixed) is fixed, but the table still grows forever. It needs a retention policy before it needs one urgently | Growth without bound after week one → schedule pruning |
| `/health` non-200 | It probes the database via `GetPendingMigrationsAsync()` and returns 503 on failure — a real dependency check | Any 503, or any gap in probing (a monitor that stops reporting is not a pass) |

## Auth and abuse

| Watch | Why | Threshold |
|---|---|---|
| Failed device-signature rejections on `/api/purchases` | If enforcement is on, a spike means either an attack or an unreleased client version — and it is [FF-04](02-findings-critical-high.md#ff-04--trailing-slash-device-signature-bypass--high-confirmed-fixed)'s regression canary | Any sustained rate above baseline |
| Requests to `/api/purchases/` **with** a trailing slash | Nothing legitimate sends this. It is the [FF-04](02-findings-critical-high.md#ff-04--trailing-slash-device-signature-bypass--high-confirmed-fixed) probe signature | **Any** occurrence — treat as targeted, not accidental |
| 404s from the admin origin's `$fuelflow_api_allowed` deny path | [FF-08](02-findings-critical-high.md#ff-08--api-published-on-the-admin-origin--high-confirmed-fixed). These are attempts to reach the API through the admin hostname — or an admin SPA feature I missed in the allow-list | Any occurrence: check whether it is an attacker or a broken dashboard feature |
| Refresh attempts rejected on Origin | The CSRF defence for a `SameSite=None` refresh cookie (`AuthController.cs:127-128`) | Any sustained rate |
| `Auth:TestPhones` logins | Each is a permanent password for that number | Any login from a test phone that you did not perform |
| Startup logs on every deploy | `ValidateSecurityConfiguration` warnings are the only signal that DeviceAuth is off or that test phones are live. They are `Log.Warning`, so they will scroll past unless something looks | Grep every deploy's startup log for `SECURITY:` |

## Supply chain

| Watch | Why | Threshold |
|---|---|---|
| Unexpected OTA publishes | [FF-03](02-findings-critical-high.md#ff-03--expo-ota-updates-are-unsigned--critical-confirmed-open) is open. Until signing ships this log is the **only** detection for the highest-severity finding in this report | Any publish not matching a planned release. Review daily, not weekly |
| `npm audit` / `nuget audit` once egress permits | Both were unreachable during this audit — [FF-33](03-findings-medium-low.md#ff-33--sshnet-202510-high-severity-cve--info-confirmed-open-accepted) is accepted on incomplete information | Run in week one and re-triage |
| CI gitleaks results | It is green for the right reasons now ([FF-22](03-findings-medium-low.md#ff-22--the-secret-scanner-reported-no-leaks-while-systematically-blind--medium-confirmed-fixed)). A *new* red is real; a new green after someone edits the config is not automatically trustworthy | Any red → treat as a real leak until proven otherwise. Any `.gitleaks.toml` change → review the diff as a security change |

---

## One structural note

Most items above are database queries or log greps that nobody is currently running, because `docs/FRAUD_ANALYSIS.md:34` (monitoring) is `⬜ TODO`. **A watchlist with no monitoring is a to-do list.** The highest-leverage single action in week one is standing up alerting for the four "Any occurrence" thresholds — double issuance, double assignment, OOM kills, and unexpected OTA publishes — because each of those is silent until it is expensive.
