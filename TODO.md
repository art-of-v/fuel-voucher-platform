# FuelFlow — Architecture Audit Todo

## ✅ Completed

### Data Integrity
- [x] FK: `fulfillments.voucher_id → fuel_vouchers` (Restrict)
- [x] FK: `fuel_vouchers.assigned_to_user_id → users` (SetNull)
- [x] FK: `fuel_vouchers.import_job_id → voucher_imports` (SetNull)
- [x] FK: `fuel_vouchers.qr_parameters_id → qr_parameters` (already existed)

### Database Design
- [x] Indexes: `stations(name, station_type)`, `fuel_types(name, station_id+name)`
- [x] Indexes: `fuel_packages(station_id+fuel_type_id)`, `order_line_items(provider, fuel_type_id)`
- [x] Soft delete (`IsDeleted`) on `FuelVoucher` + `Order` with `HasQueryFilter`
- [x] Admin handlers use `.IgnoreQueryFilters()` to see deleted items
- [x] Snake_case naming convention via `UseSnakeCaseNamingConvention()` (NuGet: `EFCore.NamingConventions`)
- [x] Removed 195 explicit `.HasColumnName()` calls from 21 config files

### Enums
- [x] `OrderStatus`, `MonobankStatus`, `VoucherStatus` → stored as `int` (removed `HasConversion<string>()`)

### Query Performance
- [x] `AsNoTracking()` on 4 read-only handlers that were missing it
- [x] `AsSplitQuery()` on 6 handlers with multiple `.Include()`
- [x] `UseQueryTrackingBehavior.NoTracking` as global default
- [x] Response caching (`[ResponseCache(Duration=300)]`) on 6 station/package/fuel-type GET controllers

### Code Quality
- [x] Seed data extracted from `OnModelCreating` → `Persistence/SeedData.cs`
- [x] FluentValidation pipeline: `ValidateAttribute` action filter + `AddValidatorsFromAssemblyContaining`
- [x] Pagination infrastructure: `PagedResult<T>`, `PagedRequest`, `ToPagedResultAsync()` extension
- [x] Applied pagination to `AdminStationsController` as reference pattern

### Operations
- [x] Health check: `/health` pings DB (returns 503 if unreachable)
- [x] Redis caching: `StackExchange.Redis` + `RedisCacheService` (config via `ConnectionStrings:Redis`)
- [x] Hangfire already on PostgreSQL storage (verified)

### Route / Mapping Fixes
- [x] `VouchersController` route: `api/Vouchers` → `api/voucher-catalog` (clashed with `VoucherController`)
- [x] `Order.MonobankPaymentUrl`: added `HasColumnName("monobank_payment_url")` + `HasMaxLength(500)`
- [x] Updated admin frontend import URL to match new route

### Migrations (5 total, sequential)
- `AddForeignKeyConstraints` — FKs + column rename
- `AddLookupTableIndexes` — new indexes
- `AddSoftDelete` — `IsDeleted` columns + query filters
- `ConvertEnumsToInt` — varchar → int
- `ApplySnakeCaseConvention` — convention metadata sync

### Build
- [x] Backend: 0 errors
- [x] Frontend (`npx tsc --noEmit`): clean

## ❌ Skipped (design decisions, not bugs)

| Item | Reason |
|---|---|
| Natural keys → surrogate UUIDs (stations, fuel_types, fuel_packages) | Massive refactor cascading across 10+ tables |
| DB-level cascading deletes on existing relationships | Current defaults are safe; changing risks breaking queries |
| Thin query handler ceremony | Style choice; 30+ handlers would churn with zero runtime benefit |
| Price precision inconsistency (`int` vs `decimal`) | Needs business decision on fractional UAH support |
| Merge outbox tables (`outbox_events` vs `provider_event_outbox`) | Transactional vs immutable audit — different concerns |

---

# Security & Test-Coverage Audit — 2026-08-15

## 🔴 High

- [x] **OTP brute-force hardening** — see “Already fixed” below
- [x] **Cap refund amount** — see “Already fixed” below
- [x] **Guard the silent FakeSmsService fallback** — see “Already fixed” below

## 🟡 Medium

- [x] **Upgrade the reconciliation test** — `test/reconciliation-ledger-coverage` (fa7ac720): ledger fields, earned-margin revenue, `REFUNDED`/`PARTIAL_REFUNDED` match statuses, per-item refund fields. Note: `TotalRevenueKopecks` actually holds UAH-scale margin — naming quirk left untouched
- [x] **Refund integration test** — `test/manual-refund-integration` (7e8bf291): 4 Testcontainers tests covering manual refund → `CancelInvoiceAsync` → `RefundStatusSyncService` confirm/failure/24h-timeout → order status flip; auto-refund path already covered by `FulfillmentConcurrencyIntegrationTests`
- [x] **Mobile CI job** — merged #306: `mobile` CI job runs `npm run typecheck` (`tsc --noEmit`); no mobile test suite exists yet
- [ ] **Verify Render env overrides** — `appsettings.Production.json` points `Monobank:WebhookUrl` / `RedirectUrl` at `api.fuelflow.com`; `Monobank__WebhookUrl` env var confirmed present on Render (2026-08-16) — still need to confirm RedirectUrl override after next deploy (wrong webhook URL = payment callbacks silently lost)
- [x] **Refresh-token reuse detection** — `security/refresh-token-reuse-detection` (17d3be32): `family_id` on refresh_tokens; replay of a rotated token revokes the whole family + SECURITY log; migration backfills empty-GUID families; 3 new handler tests
- [ ] **Device-signature scope mismatch** — code merged on both sides (#304 mobile signs `/api/purchases` + `/bulk`, #305 backend enforces), but DEPLOY GATED: backend must NOT be redeployed on Render until the app build with #304 is released, else store installs get 401 at checkout. Escape hatch: `DeviceAuth__RequireSignatureForEndpoints__0=/api/orders/checkout` env override. Verified 2026-08-16: production still runs pre-#305 build (unsigned `/api/purchases` request reached MVC validation)

## 🟢 Low

- [x] **Checkout idempotency bucket** — `fix/checkout-idempotency-bucket` (756ca95c): dedup now matches only PendingPayment orders (prefix lookup); new orders get a per-attempt GUID suffix; column widened 100→150
- [x] **`MonobankInvoiceRequest.Amount` is `int`** — `fix/monobank-amount-long` (649264ec): widened to `long` (+ status/cancel-list response models to match the webhook DTO)
- [x] ~~Misleading FakeSmsService log line~~ — fixed in the `security/sms-production-guard` PR (hint only logged for the 000000 dev-bypass code)

## ⚪ Accepted (no action)

- `000000` DevBypass OTP — intentional during the testing period to avoid Twilio costs. Revisit when flipping `Auth:DevBypass` off

## ✅ Already fixed during/before this audit

- [x] OTP brute-force hardening — forwarded headers, CSPRNG codes, per-code 5-attempt lockout — merged #300
- [x] Refund amount capped at unfulfilled order value — merged #301
- [x] Production startup guard against silent FakeSmsService fallback + fixed misleading log line — merged #302
- [x] Test-phone allowlist (`Auth:TestPhones`, fixed QA codes, no SMS cost) — merged #303
- [x] Removed `[ResponseCache]` from `/api/packages` (stale prices after admin edits) — merged #297
- [x] Fuel price stored in UAH, not kopecks (#296) + DB data repair for `okko-95` / KLO `rest`
- [x] Centralized `Money.ToKopecks`/`FromKopecks` + ARCHITECTURE.md convention (#295)
- [x] inotify crash fix — #294's `DisableReloadOnChange` AppContext switch turned out to be a phantom (switch doesn't exist in .NET 10); real fix merged #308 via `DOTNET_hostbuilder__reloadConfigOnChange=false` + regression test

