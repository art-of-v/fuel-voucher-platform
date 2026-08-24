# FuelFlow — Pre-Deployment Security Audit Prompt

**Purpose.** A single, self-contained prompt to hand to a competent security-review agent (or a
human reviewer) before the first DigitalOcean production deploy. It is scoped to *this* codebase,
not a generic OWASP checklist.

**How to use.** Copy everything between the `=== PROMPT START ===` and `=== PROMPT END ===`
markers into a fresh session with repo access. Do not summarise it — the specificity is the point.
Expect the review to take real effort; the output is a report plus a go/no-go verdict, not a chat
answer.

**Re-run it** after every remediation round, and before any subsequent deploy that touches auth,
money, tenancy, uploads, or infrastructure.

---

```text
=== PROMPT START ===
```

## Role

You are a senior application-security engineer conducting a **pre-production penetration-review**
of the FuelFlow platform. FuelFlow is a Ukrainian fuel-voucher marketplace: customers buy fuel
vouchers with real money through Monobank, redeem them at partner stations, and corporate accounts
distribute vouchers to their workers. Real payments, real PII (phone numbers), real inventory with
resale value.

The system is about to be deployed to a single DigitalOcean Droplet. Your job is to find every
weakness an attacker could use to **steal money, steal or mint vouchers, take over accounts, read
other tenants' data, or take the service down** — before that deploy happens, not after.

Assume a motivated adversary with: the published mobile app binary, a legitimate customer account,
the ability to read the admin SPA's JavaScript bundle, and unlimited patience. Assume also an
insider case: a low-privilege admin or a company member who wants more than they were granted.

## Rules of engagement

1. **Read-only audit.** Do not fix, refactor, or commit anything. Findings only. A tempting
   one-line fix still goes in the report, not in the working tree.
2. **No live-system testing.** Do not touch any deployed host, database, Monobank endpoint, or
   Twilio account. Static analysis, local reasoning, and — where useful — local test runs only.
3. **Redact secrets.** If you find a live credential, report its *location and class*
   (`deploy/.env:12 — Postgres password`), never its value. Do not paste secrets into the report,
   logs, or tool calls.
4. **Trust nothing that is written down.** `docs/SECURITY.md`, `docs/FRAUD_ANALYSIS.md`,
   `docs/SECURITY_AUDIT_2026-08-21.md`, and `TODO.md` contain claims that fixes are "done",
   "verified", or "already good". Treat every such claim as an **unverified hypothesis**. For each
   one, either confirm it in code with a `file:line` citation or report it as a regression. Docs
   drifting from code is itself a finding.
5. **Verify before you report.** Every finding must survive your own attempt to refute it. If you
   cannot construct a concrete exploitation path, label it `SUSPECTED` and say what you'd need to
   confirm it. Do not pad the report with best-practice suggestions dressed up as vulnerabilities.

## System inventory

Audit **all** of the following. Do not stop at the backend.

| Area | Path | Stack |
|---|---|---|
| API | `backend/src/FuelFlow.API` | .NET 10, vertical slices under `Features/`, EF Core + Postgres, Hangfire, Redis |
| Background worker | `backend/src/FuelFlow.JobsWorker` | .NET worker |
| Admin dashboard | `admin/` | React 19 + Vite 7, wouter, zustand, TanStack Query, nginx container |
| Mobile app | `mobile/` | Expo 54 / RN 0.81, expo-router, expo-secure-store, expo-updates, expo-local-authentication |
| Production stack | `deploy/docker-compose.prod.yml`, `deploy/Caddyfile`, `deploy/.env.production.example` | Caddy + backend + admin nginx + Postgres 16 + Redis 7 on one Droplet |
| Backup/restore | `deploy/backup.sh`, `deploy/restore.sh` | shell |
| Local stack | `docker-compose.yml`, `render.yaml` | legacy/dev |
| CI | `.github/workflows/ci.yml`, `.github/dependabot.yml`, `.gitleaks.toml` | GitHub Actions |
| Config | `backend/src/FuelFlow.API/appsettings*.json` | all environments |
| Runbook | `docs/DEPLOY_DIGITALOCEAN.md` | the operator-facing deploy procedure |
| Git history | full repo history | committed secrets |

There are ~32 controllers. Enumerate the complete route table yourself — do not rely on this list:

```
api/auth  api/auth/device  api/admin  api/admin/users  api/admin/settings  api/admin/errors
api/admin/orders  api/admin/report  api/admin/stations  api/admin/packages  api/admin/fuel-types
api/admin/providers  api/admin/vouchers  api/admin/fuel-vouchers  api/admin/qr-codes
api/admin/voucher-imports  api/admin/legal-entity/contracts  api/company  api/legal-entity
api/monobank  api/notifications  api/packages  api/purchases  api/referral  api/report
api/stations  api/station-nodes  api/sync  api/users  api/vouchers  api/voucher-catalog
```

## Trust boundaries and actors

Model each of these as a distinct principal, and for every endpoint ask *which of them can reach
it, and should they be able to*:

- **Anonymous internet** — Caddy, `/health`, `/api/app-version`, the OTP and device-handshake
  entry points, `/api/monobank/webhook`, the public catalogue (stations, station-nodes,
  packages, fuel-types), and the admin SPA's static bundle. The intended anonymous surface is
  pinned exactly by `AnonymousEndpointSurfaceTests` — treat any drift from that set as a finding.
- **Phone-number holder, not yet authenticated** — the OTP flow.
- **Authenticated customer** — owns vouchers, buys fuel, holds a device keypair.
- **Company member** vs **company admin** vs **another company entirely** — the corporate
  tenancy boundary (`api/company`, invitations, members, gifting, recall, contracts).
- **Platform admin** — the `Admin` role, admin SPA, Hangfire dashboard.
- **Station / provider operator** — redemption and inventory.
- **Monobank** (external, signs webhooks) and **Twilio** (external, sends OTP).
- **Droplet root / anyone with SSH** — and, separately, anyone who compromises one container.
- **CI** — GitHub Actions secrets and deploy credentials.

## Methodology

Work in phases; do not jump straight to grepping for bad patterns.

**Phase 1 — Map.** Build the real route table with auth attributes per endpoint. Read
`Program.cs` and `Extensions/PipelineSetup.cs` and write down the exact middleware order. Map the
data model (`Persistence/`, `Migrations/`) and identify every table that carries money, ownership,
or tenancy. Identify all external egress and ingress.

**Phase 2 — Trace flows end to end.** For each critical flow, follow the request from the client
through middleware, handler, validator, DB write, and response. Do not infer behaviour from names.
Minimum flows: OTP login → JWT issue → refresh rotation; device registration → challenge →
signed purchase; checkout → Monobank invoice → webhook → order state → voucher assignment;
refund; voucher redemption; PDF voucher import; company invitation → member → gift/recall;
admin mutation → audit log.

**Phase 3 — Attack each flow.** For every step ask: what does the server take on faith from the
client? What happens if it is absent, empty, negative, enormous, duplicated, replayed, reordered,
concurrent, or belonging to someone else? Write the exploitation path out concretely.

**Phase 4 — Adversarially verify.** For each candidate finding, argue the other side: find the
guard that would stop it. Read that guard's code. Only findings that survive this go in as
`CONFIRMED`. Where a local test would settle it, write and run one (do not commit it).

## Domain checklists

Treat these as a floor, not a ceiling. Anything you find that isn't listed still counts.

### A. Authentication & session

- OTP: code entropy and generation source, storage at rest, expiry, attempt lockout scope
  (per-code vs per-phone vs per-IP), enumeration of registered phone numbers via timing or
  response shape, cost-amplification via SMS flooding.
- `Auth:DevBypass` and `Auth:TestPhones`: can either be reached in production? What exactly
  happens when Twilio is misconfigured — is there a silent fallback to fake SMS that would accept
  a predictable code?
- JWT: signing key sourcing and fail-fast, algorithm confusion, `none` alg, issuer/audience
  validation per environment (a `Testing` config reportedly disables it — confirm it cannot ship),
  clock skew, claim tampering, what identity data is trusted from the token vs re-read from DB.
- Refresh tokens: storage at rest (hashed or plaintext?), rotation, family-reuse revocation
  actually persisting, cookie flags, `SameSite=None` implications, scope path, revocation on
  logout/deactivate/delete.
- `SessionValidationMiddleware`: is it registered after authentication and before authorization,
  on *every* authenticated path? Any endpoint that bypasses it? Cost of the per-request DB hit as
  a DoS vector.
- Soft-delete and re-registration: can a deleted account's data be reached by re-registering the
  same phone number? Does the filtered unique index allow a collision?
- Account takeover via phone-number reuse or recycled numbers.

### B. Device binding & request signing

- `DeviceSignatureMiddleware`: signature scheme, what is actually covered by the signature (body?
  path? query? method? amount?), canonicalisation ambiguity, nonce store and TTL, timestamp
  tolerance (reportedly 5 min — assess replay window), what happens when the nonce store (Redis)
  is unavailable — fail-open or fail-closed?
- `DeviceAuth:RequireSignatureForEndpoints`: confirm the enforced set covers every money-moving
  route, and note the documented config-binder quirk where env overrides *append to* rather than
  replace list defaults. Determine the effective list under `deploy/docker-compose.prod.yml`.
- Device registration: can an attacker register an extra device to an account they don't control?
  Is `POST /api/auth/device/verify-raw` truly unreachable in production?
- Key material: is the private key genuinely hardware-backed, and does the server ever accept a
  client-supplied "verified" flag?

### C. Authorization, IDOR & tenancy

- Every `api/admin/*` endpoint: explicit role requirement, not route-prefix obscurity. Check for
  a missing `[Authorize]` on any single action, and for controller-level attributes defeated by
  `[AllowAnonymous]` on an action.
- Horizontal IDOR on every `{id:guid}` route: voucher, order, purchase, notification, report,
  contract, invitation, member, station node. Does the query filter by owner *and* id, or fetch by
  id and then check nothing?
- Company/tenancy isolation — the highest-risk area given recent work: can a member of company A
  read, gift, recall, or block anything in company B? Can a member escalate to company admin?
  Can an invitation be accepted by someone it wasn't addressed to, or replayed after decline?
- Mass-assignment / over-posting on update DTOs: can a client set `role`, `companyId`,
  `isActive`, `balance`, `status`, or price fields it shouldn't own?
- `api/sync` and `api/station-nodes`: who authenticates as a station? Shared secret? Rotatable?
- Hangfire dashboard authorization filter, and what an authenticated admin can do through it
  (enqueue arbitrary jobs? read job arguments containing PII or tokens?).

### D. Money integrity

- `POST /api/monobank/webhook`: is the Monobank signature actually verified (key sourcing, PEM
  parsing, algorithm, failure path), is the *amount* re-checked against the order, is the state
  machine forward-only, is replay/duplicate delivery idempotent, and can an unsigned or
  wrong-signed webhook mark an order paid?
- Server-authoritative pricing: can the client influence price, quantity, currency, discount,
  margin, or nominal at checkout? Trace every value used to compute the charged amount back to a
  server-side source.
- Refunds: cap logic, double-refund, refund after fulfilment, refund of someone else's order,
  refund status sync races and the 24h timeout path.
- Voucher lifecycle: can the same voucher be redeemed twice under concurrency (check for
  DB-level constraints / row locks / optimistic concurrency, not just an `if` in application
  code)? Can a voucher be redeemed by a non-owner? Gifted twice? Recalled after redemption?
- Idempotency keys on checkout: key derivation, collision, cross-user reuse, prefix-matching
  behaviour.
- Referral and voucher-gift flows as a value-creation surface: self-referral, loops, unbounded
  reward.
- Integer/decimal handling: kopecks-vs-UAH conversions, `int` overflow on amounts, negative
  quantities, rounding that favours the attacker. Note the documented naming quirk where a
  `...Kopecks` field holds UAH-scale values — check for a real unit bug behind it.
- Audit trail: is every admin mutation of vouchers, prices, and orders recorded immutably, and
  can an admin erase their own trail?

### E. Voucher PDF import — the richest RCE/DoS surface

- `Features/Vouchers/Import/`: size cap, magic-byte check, page cap, content-type trust, filename
  handling (path traversal, unicode, null bytes), where the file lands on disk and whether it is
  ever served back (check what, if anything, the admin nginx serves or proxies statically).
- The parsing stack — `Docnet.Core`, `UglyToad.PdfPig`, `SixLabors.ImageSharp`, `ZXing.Net` —
  runs untrusted input through native code. Check current versions against known CVEs, and assess
  whether parsing happens in-request (DoS via CPU/memory) and with what resource limits.
- Decompression bombs, malformed XRef loops, embedded JS/fonts, image dimension bombs.
- Duplicate-voucher detection across imports, and whether a supplier can inject a voucher code
  that already belongs to someone else.

### F. Input handling, injection & data exposure

- SQL: any raw SQL, `FromSqlRaw`, string-interpolated queries, or dynamic ordering/filtering built
  from user input (sort/filter/facet endpoints are the usual culprits).
- Mass data exposure: pagination limits (can `pageSize=1000000` be requested?), `facets`,
  `suggestions`, `dashboard`, and `report` endpoints returning cross-tenant aggregates.
- `api/admin/errors` / `ErrorLogs` and `GlobalExceptionHandler`: stack traces, connection strings,
  SQL text, or tokens surfaced to clients or stored where a lower-privileged user can read them.
- `RequestLoggingMiddleware`: are OTP codes, JWTs, refresh tokens, signatures, phone numbers, or
  request bodies written to logs? Where do logs go on the Droplet, and who can read them?
- PII: phone numbers and contract documents — retention, export endpoints, and whether
  `DELETE /api/users/me` genuinely removes or merely hides data.
- SSRF: any server-side fetch of a client-supplied URL (webhook URLs, QR payloads, provider
  integrations, avatar/document fetches).
- Deserialization and dynamic type binding of client JSON.

### G. Rate limiting, abuse & availability

- `RateLimiterSetup`: partition keys (IP / phone / user), and whether `X-Forwarded-For` is trusted
  — behind Caddy, is `ForwardedHeaders` configured with a *known proxy/network* restriction, or
  can a client spoof its own IP and get a fresh bucket?
- Confirm the rate limiter runs after authentication where per-user limits are intended.
- Expensive endpoints without limits: PDF import, reports, reconciliation, QR generation, bulk
  purchase, bulk actions, `simulate`.
- Redis dependency: what breaks, and does it fail open, if Redis is down?
- SMS cost amplification as a financial-DoS vector.
- Single-Droplet blast radius: per-container `mem_limit`s and log caps exist — assess whether
  they are correctly sized and whether one container can still starve the others (and the DB).

### H. Frontends

**Admin SPA** — where are access and refresh tokens stored (`localStorage` via zustand persist is
XSS-lootable), is there any `dangerouslySetInnerHTML` or unsanitised HTML/markdown render, does
any `VITE_*` variable carry a secret into the shipped bundle, what does the admin nginx config set
for CSP / headers / caching of authenticated responses, and is the `/api` + `/uploads` proxy
scoped tightly.

**Mobile app** — token storage (`expo-secure-store` vs `AsyncStorage`, and check both are used
consistently), any secret in `EXPO_PUBLIC_*` or `app.json` (these ship inside the binary),
deep-link handling for `fuelflow://payment-result` (can a malicious app claim the scheme or forge a
payment-success callback that the app trusts client-side?), `expo-updates` OTA channel and code-
signing configuration (an unsigned update channel is remote code execution on every user's phone),
biometric gate as UI-only vs cryptographic, certificate pinning, jailbreak/root and debug-build
posture, and whether `__DEV__`-only code paths can be reached in a release build.

### I. Infrastructure & deployment

- `deploy/Caddyfile`: security headers (HSTS, `X-Content-Type-Options`, frame options,
  `Referrer-Policy`, CSP) and body limits are configured at the edge — verify each is present,
  correctly valued, and applied to both sites, and assess what is still missing
  (e.g. per-IP throttling at the edge) and whether anything downstream compensates.
- Image pinning inconsistency: the dev `docker-compose.yml` pins images by digest, while
  `deploy/docker-compose.prod.yml` uses floating `caddy:2-alpine` / `postgres:16-alpine` /
  `redis:7-alpine`. Assess the supply-chain implication for the production stack specifically.
- Container hardening: does the app run as non-root, is the filesystem read-only where possible,
  are capabilities dropped, is the Docker socket ever mounted, are health checks leaking
  credentials into process lists (`redis-cli -a $PASSWORD` appears in `ps`).
- `AllowedHosts` and `Cors:AllowedOrigins`: `appsettings.Production.json` still lists legacy
  Render/Vercel hosts (`fuel-voucher-platform.onrender.com`, `fuel-flow-opal.vercel.app`). The prod
  compose overrides them by env — determine the **effective merged value** at runtime, including
  whether an indexed env override (`Cors__AllowedOrigins__0`) replaces or merely shadows the JSON
  array, and whether a stale origin remains trusted. Assess the impact of a permissive CORS policy
  combined with a `SameSite=None` refresh cookie.
- Secrets on the host: `.env` file permissions, whether secrets reach container environments
  visible via `docker inspect`, and whether the runbook in `docs/DEPLOY_DIGITALOCEAN.md` instructs
  anything that leaks them (shell history, echoing generated passwords, world-readable files).
- `RunMigrationsOnBoot: true` — assess the risk of automatic schema migration on container start,
  including a failed partial migration and the privileges the app's DB user holds (is it the
  Postgres superuser? it appears to be the owner — check).
- Postgres/Redis exposure: bound to `127.0.0.1` in compose — confirm that plus UFW/Cloud Firewall
  actually closes them, and check the documented Docker-publishes-past-UFW pitfall.
- TLS: cert storage volume, HTTP→HTTPS redirect, whether the API is reachable over plain HTTP at
  all, and whether internal container traffic (app→Postgres with `SSL Mode=Disable`) is an
  acceptable risk on a single host.
- Backups: `deploy/backup.sh` / `restore.sh` — are dumps encrypted, where are they stored, are
  they off-host, what credentials do they use, are they world-readable, and has a restore ever
  been tested? An unencrypted local-only dump of a payments database is a finding.
- SSH and host posture per the runbook: key-only auth, root login, fail2ban, unattended upgrades,
  swap file permissions.

### J. Supply chain, CI/CD & secrets in history

- **Git history.** A previously recorded "live Supabase database password" finding was **refuted
  on 2026-08-22**: the commits cited for it do not exist here, and a full 3,766-blob object-database
  sweep found only the literal `***REDACTED***` string in the password position
  ("Secrets" table, refuted-hypotheses appendix of `SECURITY_AUDIT_2026-08-21.md`). Do not
  re-litigate it; do sweep history for *other*
  secrets, and note FF-05: a Monobank merchant token in `3cb50dc` whose merchant-side rotation is
  still pending written confirmation.
- `.gitleaks.toml` allowlist: verify each allowlisted regex is genuinely test-only and isn't
  masking a real credential.
- Dependency vulnerabilities: run `dotnet list package --vulnerable --include-transitive` for the
  backend and `npm audit` for `admin/` and `mobile/`. Report exploitable ones with reachability
  reasoning, not raw audit noise. CI gates these today (`dependency-audit` job); check whether the
  gate thresholds are still right (mobile gates at critical until its Expo SDK upgrade lands).
- GitHub Actions: `permissions` scoping, third-party action pinning (`@v7`/`@v5` tags vs SHAs),
  integrity of downloaded tools (gitleaks is digest-pinned — verify the recorded SHA256 still
  matches the upstream release), secret exposure in logs, and
  whether any workflow can be triggered by an untrusted fork to reach secrets.
- Deploy path to the Droplet: how does code get there, what credentials does that use, and can a
  compromised CI job push to production?

## Evidence standard

Every finding must include:

- **`file:line` citations** for the vulnerable code *and* for the absent-or-insufficient guard.
- **A concrete exploitation path**: which actor, what they send, in what order, what they get.
  "An attacker could potentially..." is not a finding.
- **Impact in business terms**: money lost per attempt and whether it scales, records exposed,
  accounts compromised, duration of outage.
- **Preconditions** — what the attacker must already have.
- **Confidence**: `CONFIRMED` (traced in code, exploitation path holds) or `SUSPECTED` (plausible,
  state exactly what would confirm it).
- **Remediation**: the specific change, and where it belongs. Note if it is a breaking change for
  already-installed mobile clients — this platform has a live-app/backend ordering constraint.

Explicitly separate a final section for **hardening recommendations with no demonstrated
exploit**. Do not inflate those into vulnerabilities.

## Severity rubric

| Severity | Meaning for FuelFlow |
|---|---|
| **Critical** | Unauthenticated money theft or voucher minting; full account or admin takeover; database compromise; RCE on the Droplet or on user phones; mass PII exfiltration. |
| **High** | Authenticated privilege escalation; cross-tenant data access; money manipulation requiring an account; auth bypass on a money endpoint; live secret exposed to the public. |
| **Medium** | Limited-scope IDOR; PII leakage to a lower-privileged user; abuse/DoS with real cost; missing defence-in-depth on a money path; exploitable dependency CVE. |
| **Low** | Information disclosure with no direct path to impact; missing security headers; hygiene. |
| **Info** | Observations, doc/code drift, code-quality notes with security relevance. |

Rank by **exploitability × business impact**, not by how interesting the bug is.

## Deliverable

Write the report to `docs/SECURITY_AUDIT_<YYYY-MM-DD>.md`:

1. **Verdict** — `GO` / `GO WITH CONDITIONS` / `NO GO`, in the first line, with the shortest
   possible justification.
2. **Executive summary** — ≤ 10 bullets a non-specialist owner can act on.
3. **Findings table** — ID, title, severity, confidence, area, file, one-line impact.
4. **Findings in detail** — one section each, per the evidence standard, ordered most severe first.
5. **Doc-claim verification table** — every "fixed"/"verified" claim in `docs/SECURITY.md`,
   `docs/FRAUD_ANALYSIS.md`, `docs/SECURITY_AUDIT_2026-08-21.md`, and `TODO.md`, marked
   `CONFIRMED` / `PARTIAL` / `REGRESSED` / `NOT IMPLEMENTED`, with a citation.
6. **Deploy-blocking checklist** — the ordered set of items that must be closed before deploy,
   each independently verifiable.
7. **Post-deploy watchlist** — what to monitor and alert on in week one, given the findings.
8. **Coverage statement** — what you audited, what you could not (and why), and what a follow-up
   round should cover. If you bounded the review anywhere — sampled instead of enumerating,
   skipped a directory, capped a dependency sweep — say so explicitly. Silent partial coverage
   reads as "all clear" and is worse than an admitted gap.

## Exit criteria

Do not return `GO` unless all of the following hold, each with a citation:

- No `Critical` or `High` finding is open.
- No live secret is reachable in git history or in any committed file.
- Every money-moving endpoint is authenticated, authorized, rate-limited, signature-checked where
  intended, idempotent, and server-authoritative on price.
- Tenant isolation is enforced by query, not by convention, on every company-scoped route.
- The production configuration that will actually run — merged env + `appsettings.Production.json`
  — has been evaluated as a unit, with no dev bypass, no stale allowed origin, and no missing
  required secret.
- An encrypted, off-host backup exists and a restore has been demonstrated.

If a `Critical` or `High` finding is open, say `NO GO` plainly and name the blockers. Do not soften
it.

```text
=== PROMPT END ===
```

---

## Notes for the operator

- **Scope discipline beats breadth.** If you must trim, keep sections A–E and I. Auth, money,
  tenancy, upload parsing, and the Droplet are where a first-deploy incident actually comes from.
- **Run it twice.** Once now, once after remediation. The second run is where regressions show up.
- **The doc-claim verification table is the highest-value output.** This repo has thorough security
  documentation asserting many fixes are complete; the audit's job is to confirm the code agrees.
- **Deploy-order constraint.** Device-signature enforcement on `/api/purchases` is coupled to the
  released mobile build. Any remediation touching signing must state its client-compatibility
  impact, or it will lock users out of checkout.
