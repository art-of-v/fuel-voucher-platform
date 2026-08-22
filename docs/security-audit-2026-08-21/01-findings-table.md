# 3. Findings table

Severity per the brief's rubric. **Confidence:** CONFIRMED = a concrete exploitation path was traced and the guard that would have stopped it was read and found absent or insufficient. SUSPECTED = no concrete path demonstrated. **Status:** FIXED = remediated in this working tree, with the fix cited in the detail section. OPEN = requires a human/operational action.

## Critical

| ID | Title | Sev | Conf | Area | File | Impact | Status |
|----|-------|-----|------|------|------|--------|--------|
| FF-03 | Expo OTA updates are unsigned | Critical | CONFIRMED | Supply chain / mobile | `mobile/app.json`, `eas.json` | Holder of the EAS token replaces app JS on every install, including payment screens | **OPEN** |
| FF-01 | Voucher catalogue readable without authentication | Critical | CONFIRMED | Authz | `Features/Vouchers/Import/GetVouchersQuery.cs` | Anonymous dump of voucher inventory incl. QR payloads — bearer instruments redeemable at the pump | FIXED |
| FF-02 | Bulk checkout quantity unvalidated → int overflow | Critical | CONFIRMED | Money | `BulkCheckoutCommandHandler.cs` | Crafted quantity overflows the total into a negative/near-zero price; vouchers for ~0 ₴ | FIXED |

## High

| ID | Title | Sev | Conf | Area | File | Impact | Status |
|----|-------|-----|------|------|------|--------|--------|
| FF-05 | Monobank merchant token committed to git history | High | SUSPECTED (live) | Secrets | `3cb50dc:…/appsettings.Production.json` | If never rotated, a reader of history can act as the merchant against Monobank's API | **OPEN** (owner attestation) |
| FF-06 | `/api/purchases/simulate` reachable in production and bypassed the order state machine | High | CONFIRMED | Money | `SimulatePaymentCommandHandler.cs` | Admin resurrects a `Fulfilled` order → fulfilment issues a **second** set of vouchers for one payment | FIXED |
| FF-04 | Trailing-slash device-signature bypass | High | CONFIRMED | Device binding | `DeviceSignatureMiddleware.cs` | `POST /api/purchases/` skips signature verification entirely; a stolen token alone buys | FIXED |
| FF-07 | Per-phone OTP rate limit never functioned | High | CONFIRMED | Abuse / cost | `RateLimiterSetup.cs` | SMS pump against one number; unbounded Twilio spend at $0.2268/SMS to UA | FIXED |
| FF-08 | API published on the admin origin | High | CONFIRMED | Infrastructure | `admin/nginx.conf`, `deploy/Caddyfile` | Whole API surface reachable through the admin hostname, sidestepping origin-scoped controls | FIXED |

## Medium

| ID | Title | Sev | Conf | Area | File | Impact | Status |
|----|-------|-----|------|------|------|--------|--------|
| FF-22 | Secret scanner reported "no leaks" while systematically blind | Medium | CONFIRMED | CI / secrets | `.gitleaks.toml` | False assurance: real high-entropy secrets suppressed by a 1,476-word stopword allowlist | FIXED |
| FF-09 | PDF page dimensions unclamped on import | Medium | CONFIRMED | DoS / import | `PdfRenderer.cs` | One crafted PDF allocates gigabytes; worker OOM takes fulfilment down | FIXED |
| FF-10 | QR decode size unclamped | Medium | CONFIRMED | DoS / import | `ImportVouchersCommand.cs` | Same class as FF-09 via the QR path | FIXED |
| FF-11 | QR payloads written to logs and error rows | Medium | CONFIRMED | Data exposure | `DatabaseLoggerProvider.cs`, import paths | Anyone with log or error-log read access harvests redeemable vouchers | FIXED |
| FF-12 | No container memory limits or log rotation | Medium | CONFIRMED | Availability | `deploy/docker-compose.prod.yml` | One runaway container evicts Postgres on a single Droplet; unbounded logs fill the disk | FIXED |
| FF-13 | CSV formula injection in admin exports | Medium | CONFIRMED | Frontend | `admin/src/lib/utils.ts` + 2 tabs | Attacker-chosen request path lands in a cell; opening the export runs a formula on the admin's machine | FIXED |
| FF-14 | Backups unencrypted on-host; restore never exercised | Medium | CONFIRMED | Operations | `deploy/backup.sh` | Host compromise yields the whole customer/voucher database; unverified backup is a belief | FIXED (restore drill **OPEN**) |
| FF-24 | Expired vouchers preferentially assigned | Medium | CONFIRMED | Fulfilment | `FulfillmentService.cs` | Customers paid for and received already-expired vouchers | FIXED |
| FF-25 | Three unbounded `OutboxEvents` full-table loads | Medium | CONFIRMED | Availability | `FulfillmentService.cs` | Table growth degrades then OOMs the worker | FIXED |
| FF-26 | No global rate limiter | Medium | CONFIRMED | Abuse | `RateLimiterSetup.cs` | Any endpoint without a named policy was entirely unlimited | FIXED |
| FF-23 | Voucher import runs synchronously in-request | Medium | CONFIRMED | Availability | `VouchersController.cs` | A large PDF ties up a request thread for minutes | PARTIAL (see detail) |

## Low

| ID | Title | Sev | Conf | Area | File | Impact | Status |
|----|-------|-----|------|------|------|--------|--------|
| FF-19 | Stale forwarded-headers trust collapsed rate-limit partitions | Low | CONFIRMED | Rate limiting | `RateLimiterSetup.cs` | With a 2-hop proxy chain every client shared one partition — or could spoof its own | FIXED |
| FF-20 | `Auth:DevBypass` was one env var from full auth bypass | Low | CONFIRMED | Auth | `Program.cs` | Fake SMS + all limits `int.MaxValue` + unauthenticated Hangfire console | FIXED |
| FF-21 | `DeviceAuthOptions` defaults fail open | Low | CONFIRMED | Device binding | `DeviceAuthOptions.cs`, `Program.cs` | Device binding silently absent by omission | FIXED |
| FF-16 | Legacy raw refresh-token comparison | Low | CONFIRMED | Auth | `RefreshTokenCommand.cs` | Pre-hashing tokens still accepted by value | FIXED |
| FF-17 | No security response headers | Low | CONFIRMED | Frontend | `deploy/Caddyfile` | Missing CSP/HSTS/frame-options on the admin origin | FIXED |
| FF-18 | `MONOBANK_REDIRECT_URL` had no `:?` guard | Low | CONFIRMED | Config | `deploy/docker-compose.prod.yml` | Empty var boots a payment flow with a broken return URL | FIXED |
| FF-27 | `InvalidOperationException.Message` echoed to clients | Low | CONFIRMED | Info disclosure | `GlobalExceptionHandler.cs` | Internal state and identifiers leak in 500 bodies | FIXED |
| FF-28 | 78-char dev JWT secret committed, shared by every clone | Low | CONFIRMED | Secrets | `appsettings.Development.json` | Any clone holder mints valid dev tokens; also a scanner tripwire | FIXED |
| FF-29 | nginx `client_max_body_size` would 413 voucher imports | Low | CONFIRMED | Availability | `admin/nginx.conf` | Default 1 MB rejects real voucher PDFs through the admin origin | FIXED |
| FF-15 | `TokenVersion` latent trap | Low | SUSPECTED | Auth | `RefreshTokenCommand.cs` | Field exists but is not enforced; future use may not revoke as expected | FIXED (documented) |

## Info

| ID | Title | Sev | Conf | Area | File | Impact | Status |
|----|-------|-----|------|------|------|--------|--------|
| FF-30 | Dead `/uploads/` nginx route | Info | CONFIRMED | Infrastructure | `admin/nginx.conf` | Route to a path that no longer exists | FIXED |
| FF-31 | Anonymous, cacheable `/api/stations/fuel-types` returns the raw entity | Info | CONFIRMED | Data exposure | `StationController.cs` | Not a leak today; any admin-only field later added to the entity becomes an anonymous cached leak with no endpoint change | OPEN (hardening) |
| FF-32 | Connection-URI credentials matched no scanner rule | Info | CONFIRMED | CI / secrets | `.gitleaks.toml` | `postgres://user:pass@host` was uncovered by both the default ruleset and the new rules | FIXED |
| FF-33 | `SSH.NET 2025.1.0` High-severity CVE (GHSA-q939-rpr3-3284) | Info | CONFIRMED | Supply chain | `FuelFlow.IntegrationTests.csproj` | Test-project-only transitive dependency; impact bounded to CI | OPEN (accepted) |

---

## Counts

| Severity | Total | Fixed | Open |
|----------|-------|-------|------|
| Critical | 3 | 2 | **1** (FF-03) |
| High | 5 | 4 | **1** (FF-05, owner attestation) |
| Medium | 11 | 10 | 1 partial (FF-23) + FF-14 restore drill |
| Low | 10 | 10 | 0 |
| Info | 4 | 2 | 2 (accepted / hardening) |

**One Critical and one High remain open. Per the brief's exit criteria that is a NO GO.**
