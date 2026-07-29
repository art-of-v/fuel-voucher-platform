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
