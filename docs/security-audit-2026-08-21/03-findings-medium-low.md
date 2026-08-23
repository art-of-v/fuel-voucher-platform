# 4b. Findings in detail — Medium, Low, Info

---

# Medium

## FF-22 — The secret scanner reported "no leaks" while systematically blind — **Medium, CONFIRMED, FIXED**

This is the finding that most changes how the other secret findings should be read, so it goes first among the Mediums.

**What the docs claimed.** `docs/REMEDIATION_PRIORITIES.md:49-51` states that *"the gitleaks job will stay red until F4's git-history purge is complete."* Empirically **false** — the job is green across the full history. The docs describe a red gate that does not exist, which means nobody was waiting on it.

**Why green was meaningless.** The config was `[extend] useDefault = true` and nothing else. The default ruleset's catch-all is `generic-api-key`, and its allowlist contains **1,476 stopwords**. The rule drops any finding whose captured value *contains one of those words as a substring*. The perverse consequence: the longer and more random a secret is, the more likely it contains some three-to-five-letter English fragment, and so **the stronger the secret, the more likely it is silently suppressed.** A scanner with that property does not report absence of secrets; it reports absence of *short* secrets.

**Fix, and how it was tested.** Rules were added around FuelFlow's own configuration key names — `Jwt:Secret`, `Monobank:Token`, `Twilio:AuthToken`, `Session:Secret` — with `secretGroup`, an entropy floor, and a `regexTarget = "match"` allowlist for genuine placeholders. Every rule carries an in-file comment recording why it exists and which false positives were observed.

Then it was proven rather than assumed, with three controls:

| Control | Result |
|---|---|
| Positive — 4 synthetic real-shaped secrets | **4/4 caught**, each by the intended rule ID |
| Negative — 5 placeholders (`CHANGE_ME`, `postgres:postgres@localhost`, `USER:PASSWORD`, `***REDACTED***`, `${POSTGRES_PASSWORD}`) | **5/5 correctly ignored** |
| Regression — full real history | **no leaks found** — so CI will not go red on merge. The tool reported 672 commits scanned; `git rev-list --all --count` reports **3,465**, and that discrepancy is unexplained — see the caveat in [07-coverage-statement.md](07-coverage-statement.md) |

**Documented limitation.** The entropy floor is 3.0, chosen because placeholder-shaped strings cluster below it (`postgres`, `password`, `PASSWORD` all score 2.75; `***REDACTED***` scores 2.41) and generated secrets cluster at 3.5–4.5. The cost is stated in the config file itself: a real but **weak** credential — a dictionary word, a short repeat — scores under 3.0 and will not be caught. The rule is not a substitute for never committing a live credential.

---

## FF-09 / FF-10 — PDF page dimensions and QR decode size unclamped — **Medium, CONFIRMED, FIXED**

**Attack.** A PDF declares its own page dimensions. The renderer allocated a bitmap sized from those declared values with no ceiling, so a small crafted file — hundreds of bytes — asks for a multi-gigabyte allocation. On a single Droplet where the API, worker, Postgres and Redis share one memory budget, that OOM does not merely fail the import: it can take the database down with it. FF-10 is the same defect reached through the QR-decode path.

The upload path requires an admin, so this is insider or stolen-session territory — but the *supplier voucher PDF* is by definition a file that arrives from outside the company and gets uploaded by a trusting admin. That is a realistic delivery vector, not a hypothetical one.

**Guard sought.** `Program.cs:79-82` caps the *request body* at 25 MB (`FormOptions.MultipartBodyLengthLimit` and `Kestrel.Limits.MaxRequestBodySize`). That bounds bytes on the wire; it does nothing about declared page geometry. A 200-byte PDF passes that check and still asks for gigabytes.

**Fix.** Per-dimension and total-pixel ceilings before allocation, with the file rejected rather than clamped-and-rendered so a truncated import cannot be mistaken for a successful one. `Features/Vouchers/Import/Services/PdfRenderer.cs`, `Features/Vouchers/Import/ImportVouchersCommand.cs`.

---

## FF-11 — QR payloads written to logs and error rows — **Medium, CONFIRMED, FIXED**

**Attack.** Import diagnostics logged the decoded QR payload, and failed-row records stored it. A QR payload *is* the redeemable instrument. So the log store and the error-log table became a second, less-protected copy of the voucher inventory — reachable by anyone with log read access, which in practice is a broader group than "people allowed to see voucher codes": the admin error-log tab, log aggregation, and anyone doing incident triage.

This compounds FF-01: even after the API stopped serving payloads, the logs were still handing them out.

**Fix.** Payloads are truncated/fingerprinted rather than logged in full, so a failed import is still diagnosable (you can tell *which* row failed) without the log becoming a redemption source. Import paths and `DatabaseLoggerProvider.cs`.

---

## FF-12 — No container memory limits or log rotation — **Medium, CONFIRMED, FIXED**

**Attack / failure mode.** Everything runs on one Droplet. With no per-container memory limit, any container that grows — via FF-09, FF-25, or an ordinary leak — competes with Postgres for the same RAM, and the kernel OOM killer's choice is not ours to make. Losing the API is an outage; losing Postgres mid-write is a data problem. Separately, unrotated container logs fill the disk, and a full disk on a Postgres host is its own incident.

**Fix.** Explicit `mem_limit` per service sized so the sum leaves headroom for the host, plus `logging.options.max-size` / `max-file` on every service. `deploy/docker-compose.prod.yml`.

---

## FF-13 — CSV formula injection in admin exports — **Medium, CONFIRMED, FIXED**

**Attack.** The audit and error-log tabs export to CSV. Rows contain attacker-influenced strings — most directly the **request path**, which an unauthenticated attacker fully controls simply by making a request. Request `GET /=cmd|'/c calc'!A1` and the string lands in the log; an admin later exports the log and opens it in Excel or LibreOffice; the spreadsheet evaluates the cell because it starts with `=`.

The subtlety that makes this real: **CSV quoting does not help.** A quoted field is still parsed as a formula by the spreadsheet after unquoting. Correct escaping produces a valid CSV file that still executes. The trigger characters are `=`, `+`, `-`, `@`, tab and CR.

**Impact.** Code execution on an administrator's workstation, initiated by an unauthenticated attacker, in a context where the admin has just authenticated to the admin panel.

**Fix.** A neutralising helper in `admin/src/lib/utils.ts` prefixes an apostrophe to any field beginning with a trigger character, applied in `AuditTab.tsx` and `ErrorLogsTab.tsx`. Covered by tests in `admin/src/lib/utils.test.ts` (part of the 14 passing vitest cases).

---

## FF-14 — Backups unencrypted on-host; restore never exercised — **Medium, CONFIRMED; FIXED in tooling, restore drill OPEN**

**Two separate problems.**

*Unencrypted on-host.* A dump sitting on the same Droplet as the database is not a backup against the most likely catastrophe — host compromise — it is a convenience copy that hands the attacker the entire customer and voucher database in one file, no queries needed.

*Never restored.* An untested backup is a belief. The failure modes that only appear at restore time are exactly the ones that matter: wrong `pg_dump` format for the `pg_restore` invocation, missing roles, an extension absent from the target, a truncated upload that succeeded silently.

**Fix (tooling).** `deploy/backup.sh` and `deploy/restore.sh`: `age` asymmetric encryption so the Droplet holds only the **public** key and a compromised host cannot decrypt its own backups; off-host upload; a `pg_restore --list` TOC validation step so a corrupt archive is detected at backup time rather than discovered at recovery time.

**Still OPEN.** No restore has been performed. This is one of the two NO GO blockers, and it is not a code problem — it needs someone to restore to a scratch database and confirm row counts. It is also this project's own stated launch gate: `docs/FRAUD_ANALYSIS.md:33` marks automated backup `⬜ TODO`, and `:36` says *"production is not 'live' until all five above are done and verified."*

---

## FF-24 — Expired vouchers preferentially assigned to paying customers — **Medium, CONFIRMED, FIXED**

**Attack — or rather, no attacker required.** Voucher selection ordered by expiry ascending, so fulfilment handed out the *closest-to-expiry* stock first. Nothing filtered out vouchers whose expiry had already passed. The ordering therefore actively preferred already-expired vouchers: a customer paid, received a voucher, and the pump refused it.

Worth stating plainly because it inverts the usual reading of "expired": FIFO on expiry is normally correct inventory practice. It becomes a defect precisely because the `expired` case was never excluded, which turns a sensible ordering into a preference for worthless stock.

**Impact.** Not a security breach — a money-and-trust defect that produces refund load, support cost, and the specific customer experience most likely to end a fuel-voucher business's reputation. Included because the brief's scope is "money integrity," and delivering nothing for payment is a money-integrity failure.

**Fix.** Expired stock is excluded from assignment, and the ordering retained for the remaining valid stock. `FulfillmentService.cs`.

---

## FF-25 — Three unbounded `OutboxEvents` full-table loads — **Medium, CONFIRMED, FIXED**

**Failure mode.** Three separate queries materialised the entire `OutboxEvents` table into memory. Correct at launch with an empty table; a slow, silent, guaranteed availability failure as the table grows, because outbox rows accumulate for every order forever. The job gets slower each day, then one day the worker OOMs — and on this single-Droplet topology (FF-12) that OOM is contagious.

This is the failure mode that will not show up in staging or in week one, which is why it is in the report despite being invisible today.

**Fix.** All three reads are bounded and paged, filtered server-side rather than in memory. `FulfillmentService.cs`.

---

## FF-26 — No global rate limiter — **Medium, CONFIRMED, FIXED**

**Attack.** Named policies existed for OTP and a few other routes. Every endpoint *without* an explicit `[EnableRateLimiting]` was entirely unlimited — which is most of the 34 controllers, including catalogue reads and order lookups. Unlimited request volume against endpoints that each execute database queries is a straightforward availability and cost attack requiring no account.

**Guard sought.** `RateLimiterOptions.GlobalLimiter`. Not set.

**Fix.** A global partitioned limiter as a floor under every endpoint, with the named policies still applying on top where they are stricter. Deliberately generous, so it bounds abuse without breaking legitimate bursts. `RateLimiterSetup.cs`.

---

## FF-23 — Voucher import runs synchronously in-request — **Medium, CONFIRMED, PARTIAL**

**Failure mode.** A multi-page voucher PDF is rendered and QR-decoded inside the HTTP request, occupying a request thread for minutes. Concurrent imports multiply that. With `WorkerCount = 1` (`Program.cs:67`) there is no elastic capacity behind it.

**Why PARTIAL, stated plainly.** The correct fix is to move import to a background job and return a job handle — a change to the admin SPA's upload flow as well as the backend, which is beyond what this remediation pass should carry unreviewed. What was done instead: **`ImportConcurrencyGuard`** (new file) bounds how many imports can run at once, so the pathological case (several large PDFs in parallel) cannot exhaust the request thread pool, and 300 s proxy timeouts at both edges so a legitimate large import is not cut off mid-flight. A single large import still blocks a thread for minutes. `Features/Vouchers/Import/ImportConcurrencyGuard.cs`, `VouchersController.cs`.

---

# Low

## FF-19 — Stale forwarded-headers trust collapsed rate-limit partitions — **Low, CONFIRMED, FIXED**

The forwarded-headers configuration was written for a one-hop platform (Render). The new topology is Caddy → container, and both Caddy's `reverse_proxy` and nginx's `$proxy_add_x_forwarded_for` **append** to `X-Forwarded-For`. `ForwardLimit` defaults to 1, so with a two-hop chain the resolved `RemoteIpAddress` is the *proxy*, not the client — every client shares one rate-limit partition. Combined with a cleared trust list (both `KnownProxies` and `KnownIPNetworks` empty disables the check entirely), the header becomes client-spoofable, letting an attacker choose their own partition.

Rated Low rather than Medium only because the two halves partly cancel: the collapse makes limits over-strict, the spoofability makes them evadable, and neither yields a clean exploit on its own. It matters because **every per-IP limit in the system depends on this value being right**, including FF-07's and FF-26's. `RateLimiterSetup.cs`, `ServiceSetup.cs`.

## FF-20 — `Auth:DevBypass` was one environment variable from full auth bypass — **Low, CONFIRMED, FIXED**

The flag simultaneously swaps `ISmsService` for `FakeSmsService` (OTP codes reach only the log stream, so log read access equals login-as-anyone), sets every OTP and global limit to `int.MaxValue`, and drops the Hangfire dashboard's authorization filter. One stray env var in a deploy config is a complete authentication bypass *plus* an unauthenticated job-management console.

Low because it requires an operator mistake rather than an attacker action — but it is precisely the mistake that a hurried first deploy makes. Now a hard startup refusal in Production, with the reasoning recorded in the code at `Program.cs:187-200`.

## FF-21 — `DeviceAuthOptions` defaults fail open — **Low, CONFIRMED, FIXED**

`DeviceAuth:Enabled` defaulted to `false`, and when false `DeviceSignatureMiddleware` returns before checking anything — device binding on `/api/purchases` silently does not exist. Shipping that *by omission* was the failure mode worth blocking.

The fix is deliberately **not** a hard refusal, because of the operator's deploy-order constraint: enabling enforcement before a signing mobile build is in users' hands locks them out of checkout. So Production now refuses to start unless the operator states the choice explicitly — either `DeviceAuth__Enabled=true`, or `DeviceAuth__AcknowledgeDisabledInProduction=true`, which boots with a `SECURITY:` warning naming the consequence. The decision is recorded rather than defaulted. `DeviceAuthOptions.cs`, `Program.cs:213-238`.

## FF-16 — Legacy raw refresh-token comparison — **Low, CONFIRMED, FIXED**

Refresh tokens are hashed at rest, but a legacy branch still compared the raw value, so pre-hashing tokens remained redeemable by value. A stale database copy therefore stayed useful to an attacker after the hashing migration that was supposed to neutralise it. `RefreshTokenCommand.cs`.

## FF-17 — No security response headers — **Low, CONFIRMED, FIXED**

CSP, HSTS, `X-Frame-Options`, `X-Content-Type-Options`, `Referrer-Policy` added at the Caddy edge. Verified rather than assumed: the rebuilt admin bundle contains **0 inline scripts**, so `script-src 'self'` holds and the CSP will not break the dashboard on first load. `deploy/Caddyfile`.

## FF-18 — `MONOBANK_REDIRECT_URL` had no `:?` guard — **Low, CONFIRMED, FIXED**

An unset variable substituted empty and the stack booted a payment flow whose post-payment return URL was blank — customers pay and land nowhere, with no startup error. Now `${MONOBANK_REDIRECT_URL:?...}`, which fails the compose run. `deploy/docker-compose.prod.yml`.

## FF-27 — `InvalidOperationException.Message` echoed to clients — **Low, CONFIRMED, FIXED**

The global handler special-cased `InvalidOperationException` and returned its `.Message` in the response body. That message is written for developers and routinely contains internal state, entity names and identifiers — including, from EF Core, fragments describing the query or entity that failed. Free reconnaissance on any 500. `GlobalExceptionHandler.cs`.

## FF-28 — 78-character dev JWT secret committed, shared by every clone — **Low, CONFIRMED, FIXED**

Location and class only: `appsettings.Development.json`, key `Jwt:Secret`, class JWT HMAC signing key, 78 characters. Anyone with the repository can mint valid tokens against any developer's or CI's local instance. Low because it is development-only and Production has a hard placeholder refusal at `AuthSetup.cs:31-42` — but it is a *shared* signing key across everyone who has ever cloned, and it is exactly the tripwire a scanner should fire on. Replaced with a non-secret placeholder that the local dev flow reads from the environment.

## FF-29 — nginx `client_max_body_size` would 413 voucher imports — **Low, CONFIRMED, FIXED**

The backend accepts 25 MB (`Program.cs:79-82`); nginx's default is 1 MB. Real voucher PDFs exceed 1 MB, so imports through the admin origin would have failed with a 413 that the SPA surfaces as a generic error — an availability paper-cut on the core business operation, discovered on day one of real use. Now `client_max_body_size 25m`, matched to the backend limit. `admin/nginx.conf`.

## FF-15 — `TokenVersion` latent trap — **Low, SUSPECTED, FIXED (documented)**

`TokenVersion` exists on the user record but is not compared during refresh. Nothing is broken today. The trap is future: the field's name promises bulk revocation, so a later change that increments it to "log everyone out" will appear to work and will not. SUSPECTED because there is no exploitation path now — the risk is a plausible future change built on a false assumption. Fixed by documenting the field's actual (non-)behaviour at the definition and at `RefreshTokenCommand.cs`, so the next person reads the truth instead of inferring it from the name.

---

# Info

## FF-30 — Dead `/uploads/` nginx route — **Info, CONFIRMED, FIXED**

A `location /uploads/` block pointing at a path that no longer exists. No exploitation path; removed because dead edge configuration is how a future directory-serving mistake gets made silently. `admin/nginx.conf`.

## FF-31 — Anonymous, cacheable station endpoints returned raw entities — **Info, CONFIRMED, FIXED (2026-08-22)**

Both actions on `StationController` are anonymous, inherit class-level `[ResponseCache(Duration = 300)]`, and returned **EF entities** rather than DTOs. Nothing sensitive was on those entities, so there was no finding to exploit and it is correctly rated Info.

It was reported because of the shape of the future failure: the day someone adds a cost, margin or supplier field to the entity, it becomes an anonymously readable, publicly cacheable leak **with no change to the endpoint** — nothing in review would flag it, because the endpoint was not touched.

**Fixed, and wider than first reported.** Two corrections to my own earlier assessment:

1. I had recorded this as deferred because a response-shape change carries **client impact**. That was wrong, and checking rather than assuming settled it: `mobile/src/core/types/api.ts` declares exactly the fields the new DTOs keep. `PublicStationResponse` drops only `CreatedAtUtc`/`UpdatedAtUtc`, which no client reads. The wire format the app actually consumes is unchanged.
2. The finding named `/api/stations/fuel-types`, but `/api/stations` had the identical defect, and fixing one would have been half a fix. Both now project explicit DTOs — `PublicStationResponse` (9 fields) and `PublicFuelTypeResponse` (5 fields).

A third problem surfaced while making the change: the `fuel-types` action was calling `GetAdminFuelTypesQueryHandler` — the *admin* handler — from an anonymous endpoint. It now uses a new `GetPublicFuelTypesQueryHandler`, registered in `ServiceSetup.cs`. That was not in the original finding; it was found by reading the code path rather than the route table.

The reason for the DTOs is recorded as a class comment on the controller, so the constraint travels with the code instead of living only here.

Related and worth knowing: class-level `[ResponseCache]` applies to every action on the controller, and while `ResponseCachingMiddleware` will not *store* a response bearing an `Authorization` header, the `Cache-Control: public, max-age=300` header is still emitted to shared caches regardless.

## FF-32 — Connection-URI credentials matched no scanner rule — **Info, CONFIRMED, FIXED**

A credential in `postgres://user:pass@host` form was covered by neither the gitleaks default ruleset nor the key-name rules added for FF-22. Info rather than Medium because the sweep found no live credential in this shape (see [09-refuted.md](09-refuted.md)) — the gap was in coverage, not in outcome.

Closed with two entropy-gated rules, `fuelflow-db-connection-uri` and `fuelflow-ado-connection-password`. During validation the ADO rule produced **7 findings against real history**; rather than allowlist them blindly I read each line with values masked and found every one was a connection-string *builder* (`$"...Password={password};..."` — e.g. `d5866b4:Program.cs:122`, `0fc8669:DatabaseSetup.cs:28`), not a literal. An interpolation-token exclusion was added, and the reasoning plus the specific false positives are recorded in `.gitleaks.toml` so the next person does not have to re-derive them.

## FF-33 — `SSH.NET 2025.1.0` High-severity CVE — **Info, CONFIRMED, FIXED (2026-08-22)**

GHSA-q939-rpr3-3284 / CVE-2026-48798, CVSS 7.1: `ScpClient.Download`'s recursive-directory handling allows path traversal, exploitable by a **malicious SSH server** against a client that initiates a directory download. Affected `<= 2025.1.0`; first patched in **2026.0.0**. It surfaced as 2 of the 3 build warnings (`NU1903`), reached only as a transitive dependency of `FuelFlow.IntegrationTests.csproj` — not referenced by the API, the worker, or anything that ships. Impact was bounded to the CI runner, and Testcontainers never performs an SCP directory download, so the vulnerable code path was never reached.

**Fixed** by pinning `SSH.NET` to `2026.0.0` with a direct `PackageReference` in the test project, which overrides the transitive resolution. A comment above the pin records the CVE, that the path is not reachable here, and the condition for removing the pin.

**Verified empirically rather than by assumption:** `NU1903` is NuGet's own audit warning, so its disappearance is the tool confirming the override took effect. The build went from 3 warnings to 1. The remaining warning is an obsolete `PostgreSqlBuilder()` call at `TestDatabaseFixture.cs:14` — not a vulnerability, and left alone deliberately because it has no security value and the integration suite cannot be executed here to verify a change to it.

**A correction to this report.** Earlier text gave the affected version as "SSH.NET 2025.10". The resolved version was **2025.1.0**. The advisory range and the conclusion are unaffected.

The original reason for accepting rather than fixing — `npm audit` and `nuget audit` unreachable under restricted egress, so the full advisory picture was unknown — still applies to *other* dependencies. It did not apply to this one, because the advisory's patched version was determinable directly. **Running both audits remains on the week-one watchlist.**
