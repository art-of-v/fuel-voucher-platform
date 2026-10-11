# Database & Solution Architecture Review — FuelFlow

**Reviewed at:** `origin/main` @ `62cee4f3` (working tree is one commit behind — all findings verified against `main`)
**Scope:** PostgreSQL 16 / EF Core 10 schema, 34 tables, 75 migrations, query surface, operational safety
**Method:** full read of the EF model + snapshot + all 36 Fluent configurations + all 75 migrations + `docs/*` + the external planning repo (`C:\Projects\fuel-voucher-platform-planning`). No live database was reachable in this environment, so every claim below is derived from code and migrations — nothing is inferred from `pg_stat_*`. Items that need a live DB are marked **[needs live DB]**.
**Nature of this document:** review only. Nothing was changed.

---

## A. Executive summary

This is a well-built database. The financial spine is sound: the reconciliation invariant (`received = delivered + refunded + outstanding`) is documented and implementable, the redemption transition is atomic, refunds are capped at one per order by a unique index, and the below-cost guard has a real column behind it. That is better than most production fintech schemas at this stage.

The problem is not the model — it is that **the database is not the authority on almost anything.** In 34 tables there is exactly **one** CHECK constraint, **zero** concurrency tokens, and **three** global query filters. Everything else — that a voucher in someone's hand must belong to an order, that money cannot be negative, that a status may only move in permitted directions — lives in C# handlers that are individually correct and collectively unguarded. The team has already been bitten twice by a default that could invert correctness (#929, #930, both documented at length in `DatabaseSetup.cs:65-78`); the schema has the same characteristic, one level down.

Three findings are genuinely dangerous rather than merely untidy:

1. **`fuel_vouchers.fuel_type_id` is `ON DELETE CASCADE`.** Deleting a fuel type destroys every voucher sold against it — paid-for paper. Commit `62cee4f3` correctly changed the *code* to archive instead of delete, but **the database-level cascade is still live** and still the sharpest edge in the schema. It is a loaded gun with no code currently pointing at it.
2. **`users` has `is_deleted` but no query filter.** A soft-deleted customer with an unexpired token still passes session validation (`SessionValidationMiddleware.cs:33-39` never selects `IsDeleted`), can still have their profile edited, and is still counted in the admin dashboard total (`GetDashboardQueryHandler.cs:22`). Deleting an account does not actually revoke it.
3. **Restore is broken for soft-deleted vouchers.** `RestoreVoucherCommandHandler.cs:20-21` loads *through* the `!IsDeleted` filter and never clears `IsDeleted`, so a soft-deleted voucher can never be restored — it returns "Voucher not found". A stated capability silently does not work.

Two further findings are quiet but will bite: `qr_parameters` accumulates an unbounded orphan row on every import that has any QR with a missing ECC level (a NULL-vs-NOT-NULL comparison that can never match, `ImportVouchersCommand.cs:333-340`), and **the retention job that is supposed to prune eight tables is a no-op by default** (`DataRetentionService.cs:13-15,69`).

None of this requires a rewrite. Roughly 80% of the value sits in about five days of work: add the missing constraints, add the query filter, fix the restore path, drop the dead indexes and dead columns, and turn on the prune job.

---

## B. Findings

| # | Problem | Severity | Type | Effort | Files |
|---|---|---|---|---|---|
| F-01 | `fuel_type_id` CASCADE destroys paid-for vouchers | **Critical** | Change | S | `ApplicationDbContextModelSnapshot.cs:3084-3088` |
| F-02 | `users.is_deleted` exists but nothing filters it | **Critical** | Add | S | `UserConfiguration.cs`, `SessionValidationMiddleware.cs:33-39` |
| F-03 | Restore cannot restore a soft-deleted voucher | **Critical** | Fix | S | `RestoreVoucherCommandHandler.cs:20-21,43` |
| F-04 | Retention job deletes nothing by default | **High** | Change | S | `DataRetentionService.cs:13-15,69` |
| F-05 | `qr_parameters` grows an orphan row per import | **High** | Fix | S | `ImportVouchersCommand.cs:333-340` |
| F-06 | One CHECK constraint in 34 tables, and it is not in the model | **High** | Add | M | `20261004203053_AddHeldVoucherMustHaveOrder.cs:22-29` |
| F-07 | Zero concurrency tokens; correctness rests on `rowsAffected` | **High** | Add | M | `FulfillmentService.cs` (8 sites) |
| F-08 | Money is stored three different ways | **High** | Change | M | `Refund.cs:8`, `VoucherRenewalItemConfiguration.cs:42` |
| F-09 | `orders.user_id` and 10 other uuid columns have no FK | **High** | Add | M | `OrderConfiguration.cs:19,102` |
| F-10 | No `idempotency` on exchange/renewal writes | **Medium** | Add | M | `ConfirmVoucherExchangeCommandHandler.cs:118-123` |
| F-11 | 12 redundant indexes, 1 on a column nothing writes | **Medium** | Delete | S | `FuelVoucherConfiguration.cs:166-192` |
| F-12 | `is_deleted` is unindexed yet every read filters on it | **Medium** | Add | S | `FuelVoucherConfiguration.cs:150` |
| F-13 | Two audit tables with unbounded `text` and no retention | **Medium** | Add | M | `ErrorLogConfiguration.cs`, `ProviderEventOutboxConfiguration.cs` |
| F-14 | Five uuid columns that look like FKs and are not | **Medium** | Add | S | `OperatorVoucherRenewalConfiguration.cs:21-28,60-62` |
| F-15 | `MonobankMerchant` stored as int in one table, varchar in another | **Medium** | Change | S | `MonobankStatementEntryConfiguration.cs:18-21` |
| F-16 | 8 columns in `PascalCase` on a snake_case schema | **Medium** | Change | S | `AddVoucherIssuedAndUsedTimestamps.cs:14-24` |
| F-17 | `qr_payload` stored twice; it is a bearer instrument | **Medium** | Change | S | `ImportVouchersCommand.cs:72-83` |
| F-18 | No partition plan for the fastest-growing tables | **Medium** | Add | L | `DataRetentionService.cs:83-138` |
| F-19 | No model-vs-migration drift gate in CI | **Medium** | Add | S | `.github/workflows/ci.yml` |
| F-20 | Two migration folders; two hand-written migrations | **Medium** | Add | S | `MigrationDiscoveryTests.cs:11-20` |
| F-21 | README's schema section lists 4 tables that don't exist | **Low** | Change | S | `README.md:248-268` |
| F-22 | No RPO/RTO and no PITR anywhere | **Low** | Add | M | `deploy/backup.sh:46-50` |
| F-23 | `fulfilled_voucher_id` and friends unindexed | **Low** | Add | S | `VoucherRenewalItemConfiguration.cs:31-35` |

Severity: **Critical** = can destroy money or history today. **High** = will cause an incident under normal growth. **Medium** = correctness or operability debt with a defined trigger. **Low** = hygiene.

---

## C. Findings in detail

### F-01. Deleting a fuel type silently destroys paid-for vouchers — **Critical**

**What exists today (facts).**

`fuel_vouchers.fuel_type_id` carries `ON DELETE CASCADE`, declared by EF convention from the `FuelType` navigation and written into the snapshot at `Migrations/ApplicationDbContextModelSnapshot.cs:3084-3088`:

```
b.HasOne("FuelFlow.SharedKernel.Domain.FuelTypeEntity", "FuelType")
    .WithMany()
    .HasForeignKey("FuelTypeId")
    .OnDelete(DeleteBehavior.Cascade)
    .IsRequired();
```

It originates in `Migrations/20260625144527_ReplaceFuelTypeEnumWithFuelTypeId.cs:111-117`. It is still present on `origin/main`.

**Why this is bad (plain words).**

A voucher is a piece of paper the customer already paid for. If someone deletes a row from the `fuel_types` table — through the admin API, through a support `psql` session, through any future "clean up the catalog" script — the database reaches into `fuel_vouchers` and deletes every voucher of that fuel that has not yet been redeemed. No error, no warning, no audit row. The customer paid; the record of what they bought is gone.

`62cee4f3` fixed the *application* path: `DeleteFuelTypeCommandHandler` now sets `IsDeleted = true` instead of calling `Remove`. That was the right call. But it changed only the code, not the constraint. The cascade is still there, still armed, and no longer covered by any test — because there is no longer any code path that deletes a fuel type. That is the worst possible state: a live hazard with zero test coverage.

**What I propose.**

Change the constraint to `RESTRICT` and keep the soft delete.

| Object | From | To | Why |
|---|---|---|---|
| `FK_fuel_vouchers_fuel_types_fuel_type_id` | `ON DELETE CASCADE` | `ON DELETE RESTRICT` | A fuel type with vouchers on it cannot be deleted at all |

Make it explicit in the configuration so the intent is documented rather than inferred — in `FuelVoucherConfiguration.cs`, alongside the existing `Restrict` declarations for `OrderId` (`:126`) and `SupplierId` (`:134`):

```csharp
builder.HasOne(e => e.FuelType)
    .WithMany()
    .HasForeignKey(e => e.FuelTypeId)
    .OnDelete(DeleteBehavior.Restrict);
```

**Migration plan (zero-downtime).**

1. **Pre-check (read-only, run on prod first).** If any fuel type already has vouchers, the `ALTER` will fail on a non-empty table — so find them out before you touch anything:
   `SELECT fuel_type_id, count(*) FROM fuel_vouchers GROUP BY fuel_type_id;`
2. **Step 1 — drop and recreate the constraint.** `ALTER TABLE fuel_vouchers DROP CONSTRAINT FK_fuel_vouchers_fuel_types_fuel_type_id;` then re-add as `RESTRICT`. Takes a lock on `fuel_vouchers` for the duration. At current volume this is sub-second. **[needs live DB]** — measure `fuel_vouchers` size first; if it has grown past a few million rows, do it in a maintenance window.
3. **Step 2 — nothing to backfill.** No data changes; the FK was already enforced for inserts.
4. **Step 3 — nothing to switch.** No read path changes.
5. **Step 4 — n/a.** This is the contract step itself.

**Risks and rollback.** If some script legitimately needs to purge a fuel type, it will now fail with a foreign-key error instead of succeeding destructively — that is the desired behaviour, but expect the error message to reach a human. Rollback is a single `ALTER` back to `CASCADE`. Verify with `DeleteFuelTypeSoftDeleteTests` (added in `62cee4f3`) plus one new integration test asserting that a direct `DELETE FROM fuel_types` throws.

**Estimate.** S complexity · low risk · ~1 hour including the test.

---

### F-02. Deleting a customer does not actually revoke them — **Critical**

**What exists today (facts).**

Four entities have `is_deleted`: `users`, `orders`, `fuel_vouchers`, `company_members`. Only three have a global query filter. There are exactly **three** `HasQueryFilter` calls in the entire source tree:

- `CompanyMemberConfiguration.cs:47`
- `OrderConfiguration.cs:88`
- `FuelVoucherConfiguration.cs:150`

`UserConfiguration.cs` contains none. Its `HasFilter("is_deleted = false")` at `:101` is an *index* predicate on the phone-number unique index — not a row filter.

Ten call sites compensate by hand, e.g. `VerifyCodeCommand.cs:152`, `SendCodeCommand.cs:172`, `DeleteUserCommandHandler.cs:35`. But `SessionValidationMiddleware.cs:33-39` — the middleware that runs on **every authenticated request** — selects only `{ IsBanned, TokenVersion }`:

```csharp
.Where(u => u.Id == userId)
.Select(u => new { u.IsBanned, u.TokenVersion })
```

`IsDeleted` is neither selected nor checked. `GetDashboardQueryHandler.cs:22` runs `_context.Users.CountAsync(ct)` with no predicate, so the admin dashboard's user total includes deleted accounts.

**Why this is bad (plain words).**

Deleting an account should close the door. Here it opens a new one. A deleted customer keeps a working session until their token expires, keeps the ability to change their email and profile, and still appears in your user count. This is not a database-shaped problem exactly — it is that the database offers no mechanism to express "this row is gone" for `users`, so every reader has to remember, and one of them didn't.

**What I propose.**

Add the missing global query filter, exactly as the other three tables already have it, in `UserConfiguration.cs`:

```csharp
builder.HasQueryFilter(e => !e.IsDeleted);
```

Then add `IsDeleted` to the `SessionValidationMiddleware` projection and reject a deleted account outright:

```csharp
.Select(u => new { u.IsDeleted, u.IsBanned, u.TokenVersion })
...
if (account == null || account.IsDeleted || account.IsBanned) { /* reject */ }
```

Add `!u.IsDeleted` to the dashboard count.

**Migration plan.** None. This is model-only — no DDL, no data touched, no downtime. Deploy the code, and the filter takes effect on next boot.

**Risks and rollback.** The filter will change behaviour at ~10 sites that currently write `!u.IsDeleted` manually — those become redundant but stay correct, so this is a no-op for them. The real risk is a site that *wants* to see deleted users: the admin user list already filters explicitly (`GetAdminUsersQueryHandler.cs:16`), and the only such call site is the dashboard count, which we are fixing. Rollback is one line. `IgnoreQueryFilters()` is already used at 6 sites, all targeting `orders`/`fuel_vouchers`, so the escape hatch is proven in this codebase.

One follow-on worth deciding explicitly: with the filter in place, `referral_code`'s unique index (`UserConfiguration.cs:105`, filtered only on `referral_code IS NOT NULL`, not on `is_deleted`) permanently squats a deleted user's code. Decide whether a deleted account should release its referral code — probably yes, by adding `AND is_deleted = false` to the filter predicate.

**Estimate.** S complexity · low risk · ~2 hours plus tests.

---

### F-03. "Restore voucher" cannot restore a soft-deleted voucher — **Critical**

**What exists today (facts).**

`RestoreVoucherCommandHandler.cs:20-21` loads the voucher through the context:

```csharp
var voucher = await _context.FuelVouchers
    .FirstOrDefaultAsync(v => v.Id == command.VoucherId, cancellationToken);
```

`FuelVoucherConfiguration.cs:150` applies `HasQueryFilter(e => !e.IsDeleted)`, so a soft-deleted row is invisible — the handler returns `"Voucher not found"` at `:25`. And even if the row were reachable, the handler sets `Status` back to `Assigned` at `:43` but **never sets `IsDeleted = false`**, so the row would stay invisible. Both the read and the write are wrong; only fixing one is not enough.

**Why this is bad (plain words).**

The admin UI offers "restore". For any voucher that was soft-deleted — which is what `BulkActionVouchersCommandHandler.cs:42` does when an operator runs a bulk delete — the button reports the voucher does not exist. A safety feature that does not work is worse than no safety feature, because it teaches the operator that the data is gone when it is actually still there.

**What I propose.**

Make the handler load through the filter and clear both flags. The precedent is right there in the same folder — `DeletedUnpaidOrderCleanupService.cs:64` uses `IgnoreQueryFilters()`.

```csharp
var voucher = await _context.FuelVouchers
    .IgnoreQueryFilters()
    .FirstOrDefaultAsync(v => v.Id == command.VoucherId, cancellationToken);

if (voucher == null) return new RestoreVoucherResponse(false, "Voucher not found");

voucher.IsDeleted = false;          // <-- the missing half
voucher.Status = VoucherStatus.Assigned;
```

Also clear `UsedAtUtc` on the restore branch. Today `Used → Assigned` at `:43` leaves the redemption timestamp in place, so a restored voucher carries the timestamp of a redemption that was undone — and `IssuedAtUtc`/`UsedAtUtc` were added precisely to answer "when was this issued / redeemed" (`MULTI_COMPANY.md:71-72`).

**Migration plan.** None. Code only.

**Risks and rollback.** Restoring a voucher whose order was deleted in the meantime would violate `ck_voucher_held_has_order`, which requires `order_id` on an `Assigned` voucher — the write fails cleanly, which is correct. Rollback is trivial.

**Estimate.** S · low risk · ~1 hour plus a regression test that soft-deletes then restores.

---

### F-04. The retention job deletes nothing — **High**

**What exists today (facts).**

`DataRetentionService.cs` covers eight tables with sensible windows (`VerificationCodeRetentionDays = 7` … `ProviderEventRetentionDays = 365`, `:32-50`). But `:68-69`:

```csharp
var enabled = await _settings.IsDataRetentionEnabledAsync(cancellationToken);
var dryRun = !enabled;
```

and `:144-150` returns early on a dry run. The doc comment at `:13-15` states this is deliberate: *"Fail-safe by default: while `DataRetention:Enabled` is off (the default) the job runs read-only."* No compose file or `.env` template enables it.

Voucher-domain tables are not covered at all: `voucher_imports`, `voucher_import_errors`, `voucher_exchanges`, `voucher_renewal_items`, `operator_voucher_renewals`, `qr_parameters`, and soft-deleted `fuel_vouchers` all grow monotonically. There is no `PARTITION BY` anywhere in 75 migrations.

**Why this is bad (plain words).**

The job is a smoke alarm with the battery taped in. It will log exactly how many rows *would* be deleted, every night, forever, and delete nothing. Combined with no partitioning, `error_logs` and `provider_event_outbox` (all `text`, `:13` and `AddErrorLogs`) are the two tables that grow fastest and are least constrained. There is also a precedent for how this ends: on 2026-09-27 the box hit 100% disk and Postgres crash-looped (`DEPLOYMENT.md:374-380`).

**What I propose.**

1. **Turn the job on in staging**, verify the dry-run counts against reality for one week, then enable in production. The dry-run mode exists precisely for this — it has never been used for its stated purpose.
2. **Add voucher-domain tables to the retention set** with explicit windows and a hard rule: never prune a table that an audit report reads. Concretely — `voucher_import_errors` and `voucher_imports` can go at 365 days (matching the provider audit window), `qr_parameters` at 365 days (a QR config older than that will never be re-derived), but `voucher_exchanges`, `voucher_renewal_items` and `operator_voucher_renewals` must **not** be pruned — they are the settlement record (`VOUCHER_LIFECYCLE.md:4-6`: *"the system itself is the record"*).
3. **Before any of that, measure.** Run the index/size queries in §F-18 before choosing a partitioning story. **[needs live DB]**

**Migration plan.** Setting change only, no DDL. The retention additions are code in an existing job; they run in batches of 1000 (`:28`).

**Risks and rollback.** A wrong window destroys audit evidence irreversibly — which is why the dry-run first is not optional. `DataRetentionIntegrationTests.cs` covers the existing tables; new tables need the same. Rollback is flipping `DataRetention:Enabled` off.

**Estimate.** S for the switch · M for new coverage · 1 day including the staging soak.

---

### F-05. Every import leaks a `qr_parameters` row — **High**

**What exists today (facts).**

`ImportVouchersCommand.cs:333-340`:

```csharp
var normalizedEcc = string.IsNullOrEmpty(eccKey) ? null : eccKey;
qrParams = await _context.QrParameters
    .FirstOrDefaultAsync(
        p => p.EccLevel == normalizedEcc && ...
```

`qr_parameters.ecc_level` is `NOT NULL` (`QrParameters.cs`, `varchar(1)`). When `normalizedEcc` is null, EF translates `p.EccLevel == null` to `ecc_level IS NULL` — which can never match a `NOT NULL` column. The lookup therefore always misses, and `:347` writes `EccLevel = normalizedEcc ?? string.Empty`, i.e. `''`.

The unique index `IX_qr_parameters_ecc_level_version_mask_pattern_encoding_mode` cannot catch it either: Postgres treats NULLs as distinct in a btree, so rows differing only in NULL `version`/`mask_pattern`/`encoding_mode` never conflict.

**Why this is bad (plain words).**

Any import PDF where at least one voucher has no readable ECC level adds one more junk row to `qr_parameters`, forever, with no error and no log. The table is meant to hold about a dozen rows — one per QR configuration the platform has ever seen. Instead it grows with the number of imports. Nothing breaks today; it just quietly becomes a table nobody dares delete from.

**What I propose.**

Fix the lookup to use the empty string the row is actually written with, so the second import finds the first import's row:

```csharp
var normalizedEcc = eccKey;  // '' when absent — matches what is persisted at :347
```

Better still, make the column honest. `varchar(1)` with a sentinel `''` is a null in disguise. Either allow NULL (and add `NULLS NOT DISTINCT` to the index, Postgres 15+) or keep `NOT NULL` and document `''` as "unreadable". I recommend the former — it is the correct model, and this project is already on 16.

**Migration plan.**

1. **Step 1** — deploy the code fix. No DDL, no downtime. Stops the leak immediately.
2. **Step 2** — backfill/clean: `DELETE FROM qr_parameters WHERE ecc_level = '' AND NOT EXISTS (SELECT 1 FROM fuel_vouchers WHERE qr_parameters_id = qr_parameters.id);` Reversible (the rows are unreferenced by construction). Run the count first.
3. **Step 3** — later, if desired: `ALTER COLUMN ecc_level DROP NOT NULL` plus `NULLS NOT DISTINCT`. Only after step 2, and only if the empty-string semantics are worth revisiting. **[needs live DB]** to confirm no real rows carry `''`.

**Risks and rollback.** The code fix alone is safe and is the whole fix. The optional column change is the risky part — it changes what "distinct" means and could surface duplicates that have been silently coexisting. Do step 2 first and re-run the import suite before touching it.

**Estimate.** S · low risk · ~1 hour plus a test asserting a second import creates no new `qr_parameters` row.

---

### F-06. One CHECK constraint in 34 tables, and the model doesn't know it exists — **High**

**What exists today (facts).**

Exactly one CHECK constraint exists in the database (`20261004203053_AddHeldVoucherMustHaveOrder.cs:25-29`):

```sql
ALTER TABLE "fuel_vouchers" ADD CONSTRAINT "ck_voucher_held_has_order"
CHECK (status NOT IN ('Assigned', 'Used', 'Blocked') OR "order_id" IS NOT NULL)
```

There is **no `HasCheckConstraint` call anywhere in the source** — not in any of the 36 configuration classes, not in the model snapshot. EF therefore does not know this constraint exists: it will never drop it, never regenerate it, and a regenerated `migrations script` cannot reproduce it from the model.

It is also written with raw enum member names as strings, directly coupled to `HasConversion<string>()` (`FuelVoucherConfiguration.cs:55-59`). Rename a `VoucherStatus` member and the CHECK silently stops matching — no compile error, no model error, no test failure. And `status varchar(20)` is exactly the length of `VerifiedWithWarnings`; there is zero headroom.

**Why this is bad (plain words).**

The database currently guarantees exactly one business rule out of dozens it could guarantee. It guarantees it in a way that is invisible to the tool that manages the schema, and it guarantees it against a list of status names typed out by hand.

Concretely, the following are all accepted by the database today: a negative `surcharge_uah`, a negative `cost_per_liter`, a voucher with `liters = 0`, a `refunds.amount` of −50000, two `voucher_exchanges` rows for the same `old_voucher_id`, an `operator_voucher_renewal` with no `acting_user_id`, and any 20-character string in `status` at all. Money paths are exactly where a database constraint is worth ten code reviews.

**What I propose.**

Express the existing constraint in the model so EF owns it, and add the handful of constraints that are cheap, unambiguous, and protect money.

| Table | Constraint | Type | Rationale |
|---|---|---|---|
| `fuel_vouchers` | `ck_voucher_held_has_order` | **move into the model** | EF must own it — see above |
| `fuel_vouchers` | `CHECK (liters > 0)` | add | Zero-litre fuel is never a real product |
| `fuel_vouchers` | `CHECK (cost_per_liter IS NULL OR cost_per_liter >= 0)` | add | Negative cost breaks blended-cost averaging |
| `fuel_vouchers` | `CHECK (provider_expiration_date >= customer_expiration_date)` | add | The customer promise can never exceed the provider's own term |
| `orders` | `CHECK (price >= 0)` | add | Money in |
| `order_line_items` | `CHECK (quantity > 0 AND unit_price >= 0 AND liters > 0)` | add | Line-item arithmetic is computed in app code, not enforced |
| `refunds` | `CHECK (amount > 0)` | add | A refund of 0 or negative is never legitimate |
| `voucher_exchanges` | `CHECK (surcharge_uah >= 0)`, `CHECK (old_voucher_id <> new_voucher_id)` | add | `0` legitimately means "free compensation" and stays allowed; self-exchange does not |
| `operator_voucher_renewals` | `CHECK (surcharge_uah >= 0)`, `CHECK (new_expiration > old_expiration)`, `CHECK (branch IN ('extend','replace'))` | add | `branch` is documented as exactly these two values (`OperatorVoucherRenewal.cs:36`) |
| `voucher_renewal_items` | `CHECK (amount_paid IS NULL OR amount_paid >= 0)` | add | |
| `qr_parameters` | `CHECK (ecc_level IN ('L','M','Q','H'))`, `CHECK (version BETWEEN 1 AND 40)`, `CHECK (mask_pattern BETWEEN 0 AND 7)` | add | The doc comments already promise these ranges (`QrParameters.cs:7,10,14`) and nothing enforces them |

Express them in EF Core 10 via `builder.ToTable(t => t.HasCheckConstraint(...))`, which puts them in the model and the snapshot rather than in hand-written SQL. That matters: EF 10 migrations are transaction-wrapped by default, so these are safe, unlike the raw-SQL route the current constraint had to take for idempotent-script reasons.

**Migration plan (zero-downtime).**

1. **Step 1 — validate against live data before adding anything.** Every constraint above can fail on data that already violates it. Run the predicates as `SELECT count(*)` first, per table. If any returns non-zero, that is a **finding in itself** — investigate before constraining. **[needs live DB]**
2. **Step 2 — add constraints as `NOT VALID`.** Postgres skips the scan of existing rows but enforces them on all new writes:
   `ALTER TABLE orders ADD CONSTRAINT ck_orders_price_nonneg CHECK (price >= 0) NOT VALID;`
   This is the key trick: zero downtime, zero table lock, and the new data is protected from the moment the constraint lands.
3. **Step 3 — `VALIDATE CONSTRAINT`** once the count check from step 1 is clean. This takes a `SHARE UPDATE EXCLUSIVE` lock — it does not block reads or writes. Safe to run online.
4. **Step 4 — drop and recreate `ck_voucher_held_has_order`** so EF's migration owns it identically (`NOT VALID` → `VALIDATE`).
5. **Step 5** — only now add the `.HasCheckConstraint(...)` calls to the configurations and generate the corresponding EF migration, so future migrations cannot accidentally drift from reality.

**Risks and rollback.** A `VALIDATE` on a large table holds a lock that queues behind long-running queries — watch for it on `fuel_vouchers` specifically. Rollback is `ALTER TABLE ... DROP CONSTRAINT`, instant. The `NOT VALID` sequence means you can always stop after step 2 and have partial protection with zero risk.

**Estimate.** M · medium risk (the validation step) · 2–3 days including the data audit and tests.

---

### F-07. No concurrency control anywhere — **High**

**What exists today (facts).**

Zero occurrences of `IsConcurrencyToken`, `UseXminAsConcurrencyToken`, or `IsRowVersion` in the entire source. The project instead relies on `BeginTransactionAsync` plus checking the row count from `ExecuteSqlInterpolatedAsync` — 8 sites in `FulfillmentService.cs` (lines 127, 411, 737, 771, 882, 1262, 1285, 1315) — plus optimistic checks in `ConfirmOperatorRenewalCommandHandler`, `ConfirmVoucherExchangeCommandHandler`, `GiftVouchersCommand`, `FireWorkerCommand`, `SetUserRoleCommandHandler`.

This is correct work, and `FulfillmentConcurrencyIntegrationTests.cs` exists. It is also, in the words of `DatabaseSetup.cs:72-74`, "a default that can invert correctness".

**Why this is bad (plain words).**

The voucher claim is a race: two customers pay at the same second, both look for "the oldest-expiring available voucher of this fuel", and both find the same one. The system handles this today by writing `UPDATE ... WHERE status = 'Available'` and checking that exactly one row came back. That works.

What does not work is any path that does *not* do this. A read-modify-write through the change tracker — load, change a property, `SaveChanges` — issues `UPDATE ... WHERE id = @id` with no status predicate, so the second writer silently overwrites the first. `DatabaseSetup.cs:65-78` documents this exact failure shipping twice (#929 supplier endpoints, #930 renewal cost reduction — "invoices for fuel a customer had already paid for"). Those two are fixed. The pattern is still available to the next person who writes a handler.

**What I propose.**

Add a database-enforced concurrency token to the tables that hold redeemable or monetary state. Postgres has it built in and it needs no schema change: `xmin` is a system column that changes on every row update.

In the configurations for `FuelVoucher`, `Order`, `Refund`, `VoucherExchange`, `VoucherRenewalItem`, `OperatorVoucherRenewal`:

```csharp
builder.Property(e => e.Version)
    .HasColumnName("xmin")
    .HasColumnType("xid")
    .ValueGeneratedOnAddOrUpdate()
    .IsConcurrencyToken();
```

with a shadow property `Guid Version` on the entity (or `.Property<byte[]>("Version")`).

Then, in every handler that must not lose a write, catch `DbUpdateConcurrencyException` and return a clean conflict instead of a 500.

**Migration plan (zero-downtime).**

1. **Step 1** — deploy the concurrency tokens with **no token enforcement yet**. Adding a concurrency token without handling the exception makes every conflicting write throw. Land the property alone first; nothing changes.
2. **Step 2** — instrument. Log every `DbUpdateConcurrencyException` for one release. This tells you *which* handlers actually race in production, instead of guessing.
3. **Step 3** — convert the handlers the log names, one per PR, each with a test that fires two concurrent writes and asserts exactly one wins.

No DDL at any point — `xmin` already exists on every row and needs no column, no index, no backfill. **This is the cheapest high-value change in the entire review.**

**Risks and rollback.** Step 2 is the one that matters: do not skip it. A concurrency token added without a handler fix turns a silent wrong-write into a user-visible 500. Rollback is removing the `IsConcurrencyToken()` call.

**Estimate.** M · low risk with the staged approach · 3–5 days for step 1–2, then ongoing per handler.

---

### F-08. Money is stored three different ways — **High**

**What exists today (facts).**

| Column | Type | Unit |
|---|---|---|
| `orders.price` | `numeric(12,2)` | decimal UAH |
| `order_line_items.unit_price` / `line_total` | `numeric(12,2)` | decimal UAH |
| `fuel_vouchers.cost_per_liter` | `numeric(10,4)` | decimal UAH/litre |
| `voucher_renewal_items.amount_paid` | **`numeric(18,2)`** | decimal UAH |
| `refunds.amount` | **`integer`** | **kopecks** |
| `monobank_statement_entries.amount_kopecks` | **`bigint`** | **kopecks** |

`Refund.cs:8` declares `public int Amount`. Conversion happens at the Monobank boundary via `Money.ToKopecks` / `ToKopecksLong` (`SharedKernel/Money/Money.cs:16-26`), and `StatementMatcher.cs:77-97` divides back by 100 — with the comment at `:93-96`: *"a rounding decision here is what makes a correct payment report as a mismatch."*

`refunds.amount` is `int`: max ≈ 21,474,836 kopecks ≈ ₴214,748. `monobank_statement_entries.amount_kopecks` is `bigint` for the same unit, one table over.

Meanwhile `AGENTS.md` lists "Price precision inconsistency (`int` vs `decimal`)" as an **open** trade-off — but `Migrations/20260930035000_ConvertMoneyColumnsToNumeric.cs:13-77` already converted the domain columns to `numeric(12,2)`, and `Money.cs`'s header documents the resolved rule. The trade-off entry and `README.md:218-238` both predate that decision.

**Why this is bad (plain words).**

Two different numbers for the same amount sit one join apart. `StatementMatcher.cs:97` compares `amount == order.Price` where one side arrived via a `long` division and the other straight from `numeric(12,2)`. That comparison is load-bearing for the money report, and it depends on a rounding convention documented in a comment rather than in the schema. Nothing in the database would catch a unit mistake — `numeric` happily stores `12345` meaning ₴12,345 or ₴123.45.

**What I propose.**

Do **not** normalise the units — that is the trade-off the team already made deliberately, kopecks at the wire boundary only, and it is the right call. Normalise the *types* and the *documentation*.

1. **`refunds.amount`: `integer` → `bigint`.** This is the one real defect. It is a one-line `AlterColumn` plus a CLR type change, and it removes a 21-million-kopeck ceiling for no behavioural change. The precision trade-off entry in `AGENTS.md` does not cover this — it is about `int` vs `decimal`, not about `int` vs `long` for the same unit.
2. **`voucher_renewal_items.amount_paid`: `numeric(18,2)` → `numeric(12,2)`.** Eighteen digits is ~10¹⁶ UAH. It was presumably chosen defensively; it makes the column 3 orders of magnitude wider than the `numeric(10,2)` surcharges it is compared against, and it is the only `numeric(18,*)` in the schema. The planning repo (`PRICING_IMPLEMENTATION_PROMPT.md:169-174`) says totals are "2 dp, no rounding to whole UAH" — `numeric(12,2)` matches exactly.
3. **Standardise on one unit per column and name it.** Every kopeck column ends in `_kopecks` (one already does — that convention is right, apply it to `refunds.amount` → `amount_kopecks`). Every UAH column ends in `_uah` (two already do — `surcharge_uah`).
4. **Update `AGENTS.md` and `README.md`** to record the resolved decision, so the trade-off stops being re-litigated.

**Migration plan (zero-downtime).**

1. **Step 1** — `ALTER TABLE refunds ALTER COLUMN amount TYPE bigint;` Expanding `int`→`bigint` is a metadata-only change in Postgres: instant, no table rewrite. Zero downtime. Then add a new column `amount_kopecks`, dual-write from the application, backfill, and switch reads — only if you also want the rename, which I'd defer.
2. **Step 2** — `voucher_renewal_items.amount_paid`: `numeric(18,2)` → `numeric(12,2)` is also lossless for all real data (nothing can exceed 10¹⁰ UAH), but Postgres *will* rewrite the table. **[needs live DB]** — check `voucher_renewal_items` size; if large, use `NOT VALID`-style expansion instead, or accept a brief lock.

**Risks and rollback.** Step 1 is safe. Step 2 rewrites a table — measure first. Both roll back with an `ALTER` in the other direction (the `bigint` one is instant; the `numeric` one would rewrite again).

**Estimate.** M · low risk · 1 day.

---

### F-09. Eleven columns that look like foreign keys and are not — **High**

**What exists today (facts).**

`orders.user_id` is `NOT NULL`, indexed (twice), and filtered on by essentially every order query — and has **no FK**. `OrderConfiguration.cs` declares relationships only for `LegalEntityId` (`:90-93`) and `SourceOrderId` (`:97-100`). `snapshot:3023-3034` confirms no `HasOne(…User)`.

The same pattern, same class of problem:

| Column | Target | FK | Index |
|---|---|---|---|
| `orders.user_id` | `users` | ✗ | ✓ |
| `refunds.user_id` | `users` | ✗ | ✗ |
| `refunds.created_by_user_id` | `users` | ✗ | ✗ |
| `purchase_batches.import_job_id` | `voucher_imports` | ✗ | ✓ |
| `purchase_batches.entered_by_user_id` | `users` | ✗ | ✗ |
| `app_settings.updated_by_user_id` | `users` | ✗ | ✗ |
| `error_logs.resolved_by_user_id` | `users` | ✗ | ✗ |
| `provider_event_outbox.changed_by_user_id` | `users` | ✗ | ✗ |
| `fuel_packages.price_updated_by_user_id` | `users` | ✗ | ✗ |
| `purchase_batches.fuel_type_id` | `fuel_types` | ✗ | ✓ |
| `refresh_tokens.device_id` | `devices.device_id` | ✗ | ✓ |

Note `purchase_batches.fuel_type_id` cannot have an FK *today* even if you wanted one — `fuel_types.id` is `text` and `purchase_batches.fuel_type_id` is also `text` (so it would work), but `order_line_items.fuel_type_id` is `varchar(100)` while `fuel_types.id` is `text`, and **no FK is expressible across that type mismatch**.

**Why this is bad (plain words).**

A database foreign key is a promise that "this value always points at a real row". Without it, the promise is kept only by the application remembering to check. If a user row is ever hard-deleted, `orders.user_id` keeps pointing at nothing — and the order, its lines, its fulfillment and its refund are all still there, still counted in revenue reports, still reconciled, with no way to tell whose order it was.

The `device_id` case is different and worth calling out separately: `refresh_tokens.device_id` and `push_tokens.device_id` deliberately hold the app-supplied device key rather than the `devices.id` surrogate, so the absence of an FK there is *correct by design*. I recommend leaving both alone and documenting why.

**What I propose.**

1. **Add the FKs that matter and are cheap:** `orders.user_id`, `refunds.user_id`, `purchase_batches.import_job_id`. All three point at `NOT NULL` uuid columns with matching types; all three need `ON DELETE RESTRICT` or `SET NULL`.
   - For the `*_user_id` audit columns (`refunds.created_by_user_id`, `error_logs.resolved_by_user_id`, `provider_event_outbox.changed_by_user_id`, `app_settings.updated_by_user_id`, `purchase_batches.entered_by_user_id`, `fuel_packages.price_updated_by_user_id`) — these record *who did it*, and the planning repo is explicit that audit history must keep naming a real party (`SUPPLIERS_AND_VOUCHER_COST.md:59-62`: *"a hard delete would either be blocked by the FK or silently orphan the history"*). Add `ON DELETE RESTRICT` so a user cannot be deleted while their actions are on record. That is the correct answer for audit columns and it also solves the orphan problem.
   - Add supporting indexes on the FK columns that lack them (`refunds.user_id`, `refunds.created_by_user_id`).
2. **Align `order_line_items.fuel_type_id` with `fuel_types.id`.** Both should be `text` (or both `varchar(100)` — pick one). Then the FK becomes expressible and a line item can no longer name a fuel type that does not exist. This is a `text`↔`varchar` change, i.e. a table rewrite on `order_line_items` — **[needs live DB]** to size it.
3. **Leave `refresh_tokens.device_id` / `push_tokens.device_id` alone**, and add a one-line comment in each configuration explaining that the FK is intentionally absent because the column holds the client-supplied key, not the surrogate.
4. **Make `users` non-deletable at the database level** once F-02 is in: the account lifecycle should be `is_deleted`, and if a hard `DELETE FROM users` is ever issued it should fail rather than orphan a revenue record.

**Migration plan (zero-downtime).**

1. **Step 1 — pre-flight.** For each FK, find the orphans: `SELECT count(*) FROM orders o LEFT JOIN users u ON u.id = o.user_id WHERE u.id IS NULL;` If non-zero, that is a data-integrity finding in its own right — fix the rows before adding the constraint. **[needs live DB]**
2. **Step 2 — add `NOT VALID`.** `ALTER TABLE orders ADD CONSTRAINT fk_orders_user FOREIGN KEY (user_id) REFERENCES users(id) ON DELETE RESTRICT NOT VALID;` Zero table scan, zero downtime, new writes enforced immediately.
3. **Step 3 — `VALIDATE CONSTRAINT`** once clean. `SHARE UPDATE EXCLUSIVE` lock, does not block reads or writes.
4. **Step 4** — add the `HasOne(...).HasForeignKey(...).OnDelete(...)` to the configurations and generate the EF migration.

**Risks and rollback.** Step 2 is the only one with any risk at all, and it is near-zero. Steps 1 and 3 are read-only. Rollback is `DROP CONSTRAINT`.

**Estimate.** M · low risk · 1–2 days.

---

### F-10. Exchange and operator-renewal writes have no idempotency key — **Medium**

**What exists today (facts).**

`orders.idempotency_key` is unique (filtered, `OrderConfiguration.cs:107-109`) and `refunds.order_id` is unique. But `voucher_exchanges` has **no unique constraint at all** — nothing prevents two rows for the same `old_voucher_id`, and nothing prevents `old_voucher_id == new_voucher_id` (a voucher exchanged for itself). Idempotency is enforced only in application code (`ConfirmVoucherExchangeCommandHandler.cs:118-123`).

`operator_voucher_renewals` likewise: no unique on `voucher_id`, so the same customer voucher can be renewed by an operator any number of times; `branch` (`varchar(16)`, documented as exactly `extend`/`replace`) has no CHECK; `new_expiration >= old_expiration` is unchecked; `surcharge_uah >= 0` is application-only (`ConfirmOperatorRenewalCommandHandler.cs:71-72`).

**Why this is bad (plain words).**

Double-clicking "confirm exchange" can, in principle, retire the same voucher twice. The code checks first, so it usually catches this — but the check and the write are not in the same atomic step, so it is a race, not a guarantee. An operator renewing the same voucher twice is not even an error worth preventing in a UI sense; it is a settlement discrepancy somebody reconciles by hand three months later.

**What I propose.**

| Table | Constraint | Type |
|---|---|---|
| `voucher_exchanges` | `UNIQUE (old_voucher_id)` | add |
| `voucher_exchanges` | `CHECK (old_voucher_id IS DISTINCT FROM new_voucher_id)` | add |
| `voucher_exchanges` | `CHECK (surcharge_uah >= 0)` | add |
| `operator_voucher_renewals` | `CHECK (branch IN ('extend','replace'))` | add |
| `operator_voucher_renewals` | `CHECK (new_expiration > old_expiration)` | add |
| `operator_voucher_renewals` | `CHECK (surcharge_uah >= 0)` | add |

I am deliberately **not** proposing `UNIQUE (voucher_id)` on `operator_voucher_renewals` — renewing the same voucher twice may be legitimate, and `VOUCHER_RENEWAL.md:370` explicitly lists *"is there a cap on how many times one underlying supplier voucher may be renewed?"* as an **open business question**. Do not encode an unanswered question as a constraint.

**Migration plan.** Same `NOT VALID` → `VALIDATE` sequence as F-06. Pre-check the `UNIQUE` against live data first: `SELECT old_voucher_id, count(*) FROM voucher_exchanges GROUP BY old_voucher_id HAVING count(*) > 1;` **[needs live DB]**

**Risks and rollback.** `UNIQUE (old_voucher_id)` is a real behavioural constraint — if an exchange legitimately retires a voucher already in an exchange row, it will now fail. That is a business question, not a technical one; raise it before shipping.

**Estimate.** M · medium risk · 1 day.

---

### F-11. Twelve redundant indexes, one guarding a column nothing writes — **Medium**

**What exists today (facts).**

`fuel_vouchers` has 20 indexes (`FuelVoucherConfiguration.cs:166-192`). Four are strict left-prefix duplicates — every query they can serve is better served by the wider index:

| Redundant | Superseded by |
|---|---|
| `IX_fuel_vouchers_status` | `IX_fuel_vouchers_status_is_test_data` |
| `IX_fuel_vouchers_fuel_type_id` | `IX_fuel_vouchers_fuel_type_id_status` |
| `IX_fuel_vouchers_assigned_to_user_id` | `IX_fuel_vouchers_assigned_to_user_id_status` |
| `IX_fuel_vouchers_legal_entity_id` | `IX_fuel_vouchers_legal_entity_id_worker_user_id_status` |

Plus `fuel_vouchers.provider` ⊂ `(provider, fuel_type_id, liters, status)`.

The same pattern elsewhere: `orders.user_id` ⊂ both `(user_id, status)` and `(user_id, created_at_utc)`; `notifications.user_id` ⊂ `(user_id, is_read)`; `verification_codes.phone_number` ⊂ `(phone_number, is_used, expires_at_utc)`; `refresh_tokens.user_id` ⊂ two composites; `company_invitations.worker_user_id` ⊂ `(worker_user_id, status)`; `fuel_types.station_id` ⊂ `(station_id, name)`; `fuel_packages.station_id` ⊂ `(station_id, fuel_type_id)`.

Separately, `IX_fuel_vouchers_external_id` is a unique filtered index over a column **that nothing in the codebase ever writes** — `ExternalId`, `FuelSubtype`, `RedemptionRules` and `ImageUrl` are never assigned on the entity anywhere. The index is 100% NULLs forever.

**Why this is bad (plain words).**

Every index is a second copy of the data that must be written on every insert and update, and that the query planner has to consider. `fuel_vouchers` is the busiest table in the system — every import, every sale, every redemption touches it. Twelve of its twenty indexes are copies of other indexes on the same table.

The dead ones are worse than wasteful. `external_id`, `fuel_subtype`, `redemption_rules` and `image_url` are four columns that cost storage on every row and are never populated. If someone later writes a feature that populates `external_id`, they will reasonably assume the unique index has been protecting them from duplicates all along. It has not.

**What I propose.**

1. **Drop the 12 redundant indexes** listed above. Before doing so on production, confirm each is truly unused: **[needs live DB]**
   ```sql
   SELECT relname, indexrelname, idx_scan FROM pg_stat_user_indexes
   WHERE idx_scan = 0 AND indexrelname LIKE 'IX_%';
   ```
   Note that `idx_scan` resets on restart and on `pg_stat_reset()`, so give it at least one full business cycle. An index with `idx_scan = 0` over a month is safe to drop; one over ten minutes is not.
2. **Drop the four dead columns** (`external_id`, `fuel_subtype`, `redemption_rules`, `image_url`) **and** the `external_id` unique index. This is a genuine "delete something" recommendation — they are unreachable from any code path.
3. **Then re-measure.** With the noise gone, the remaining index set becomes readable and the ones that matter stand out.

**Migration plan (zero-downtime).**

1. **Step 1 — observe.** `pg_stat_user_indexes` over ≥ 30 days (see reset caveat above).
2. **Step 2 — drop redundant indexes only.** `DROP INDEX CONCURRENTLY` does not take a lock that blocks writes. Do this **outside a migration** — EF migrations wrap in a transaction, and `CONCURRENTLY` cannot run inside one. Use a separately-run, idempotent SQL script, like `20260919113317_AddMissingDriftedTables.cs` already does.
3. **Step 3 — drop the dead columns.** `ALTER TABLE fuel_vouchers DROP COLUMN external_id, DROP COLUMN fuel_subtype, ...` is metadata-only and instant. Add the removals to a normal EF migration so the model and database stay in sync — but do the actual index drops as raw SQL in step 2.

**Risks and rollback.** Re-creating an index is `CREATE INDEX CONCURRENTLY` and takes minutes on a large table. Get the size first: **[needs live DB]**. To be safe, drop **half** the redundant indexes, observe for a week, then the other half.

**Estimate.** S complexity · low risk · 1 day plus a week of observation.

---

### F-12. Every read filters on `is_deleted`, and no index contains it — **Medium**

**What exists today (facts).**

`fuel_vouchers` has `is_deleted` with a global query filter (`FuelVoucherConfiguration.cs:150`) and **zero** indexes on it, alone or composite. Every single read of the table therefore scans an index and filters on an unindexed boolean at the heap, or worse, takes the widest available index and filters.

Because the four-prefix-superseded indexes in F-11 are being removed anyway, the right move is to make `is_deleted` part of the indexes that remain.

**Why this is bad (plain words).**

The stock monitor and the expiry-loss job both scan this table nightly. They filter on status *and* on "not deleted". The planner can use a status index and then throw away deleted rows one at a time, or scan the table. As soft-deleted rows accumulate — and nothing purges them — the wasted work grows forever.

**What I propose.**

Make the surviving indexes deletion-aware:

| From | To |
|---|---|
| `(fuel_type_id, status)` | `(fuel_type_id, status, is_deleted)` |
| `(status, is_test_data)` | `(status, is_test_data, is_deleted)` |
| `(legal_entity_id, worker_user_id, status)` | `(legal_entity_id, worker_user_id, status, is_deleted)` |

Keep `(assigned_to_user_id, status)` as is — assignment already implies the voucher is live in the ways that matter, and this index is on the customer-hot path.

**Migration plan.** `CREATE INDEX CONCURRENTLY` + `DROP INDEX CONCURRENTLY` (both outside a transaction). Build the new index first, then drop the old — never the reverse.

**Risks and rollback.** `CREATE INDEX CONCURRENTLY` that fails leaves an `INVALID` index behind, which still costs write amplification but is not used for reads. Check for and drop invalid indexes: `SELECT * FROM pg_index WHERE NOT indisvalid;` **[needs live DB]**

**Estimate.** S · low risk · half a day plus index build time.

---

### F-13. Audit tables are unbounded `text` with no retention — **Medium**

**What exists today (facts).**

`error_logs`: every column is unbounded `text`, including `message`, `exception_message`, `stack_trace`, `user_name` (PII), and `resolved_by_user_name`. Indexes on `logged_at_utc`, `level`, `source`, `trace_id`. No FK on `resolved_by_user_id`.

`provider_event_outbox`: `aggregate_type`, `aggregate_id`, `event_type`, `summary`, `changed_by_user_name` all `text`; `new_value` is `jsonb NOT NULL` — but many rows are a JSON-encoded *plain string*, not an object (`RefundOrderCommandHandler.cs:234,267` serializes `$"{amount} kopecks..."` directly). No index on `event_type` or `aggregate_type`, although the audit query has to pick one. No FK on `changed_by_user_id`.

Both are covered by `DataRetentionService` (90d / 365d) — which deletes nothing by default (F-04).

**Why this is bad (plain words).**

`error_logs` stores whole stack traces in full-text columns with a 90-day window. That is the right data to keep and the wrong way to store it at volume. `provider_event_outbox` is the audit trail the planning repo calls the system of record (`VOUCHER_LIFECYCLE.md:4-6`) — and its retention depends entirely on a job that is switched off.

**What I propose.**

1. **Bound the unbounded columns.** `message varchar(2000)`, `exception_message varchar(4000)`, `stack_trace text` (genuinely unbounded, acceptable), `summary varchar(500)`, `user_name`/`resolved_by_user_name`/`changed_by_user_name varchar(200)`. Most of these already have that width on sibling tables — `support_messages.message` is `varchar(4000)`, `refunds.error_message` is `varchar(500)`. This is consistency, not invention.
2. **Index `provider_event_outbox (event_type, changed_at_utc DESC)`** — the audit screen filters by event type and orders by time. No index on `event_type` means the admin audit tab chooses between a scan and a sort.
3. **Add the `*_user_id` FKs** from F-09 — audit columns pointing at users should use `RESTRICT`.
4. **Consider partitioning `provider_event_outbox` by month** once retention is enabled — see F-18.

**Migration plan.** `ALTER TABLE ... ALTER COLUMN ... TYPE varchar(n)` on a `text` column **rewrites the table**. **[needs live DB]** to size `error_logs`. If it is large, do it as: add the new column, dual-write, backfill in batches, swap. `error_logs` is append-only and low-value-per-row, so an alternative is to drop the `stack_trace` on rows older than the window and leave the column type alone.

**Risks and rollback.** Truncating data that an audit or incident review needs is the risk here. Take a `pg_dump` of both tables before step 1 and keep it with the restore drill that F-22 says is still owed.

**Estimate.** M · medium risk · 2 days.

---

### F-14. Five audit tables point at vouchers and users with nothing enforcing it — **Medium**

**What exists today (facts).**

| Column | Points at | FK | Index |
|---|---|---|---|
| `voucher_renewal_items.fulfilled_voucher_id` | `fuel_vouchers` | ✗ | ✗ |
| `operator_voucher_renewals.replacement_voucher_id` | `fuel_vouchers` | ✗ | ✗ |
| `operator_voucher_renewals.acting_user_id` | `users` | ✗ | ✗ |
| `operator_voucher_renewals.customer_user_id` | `users` | ✗ | ✓ |
| `voucher_exchanges.acting_user_id` | `users` | ✗ | ✗ |

`VoucherRenewalItemConfiguration.cs:31-35` documents the intent for the first: *"Bare done-marker column, not a navigation… No FK"* — the reasoning is that the `source_voucher_id` FK already pins the row. That reasoning is sound for the *renewal* but not for the *replacement*: `replacement_voucher_id` is the only pointer to where the customer's fuel went, and if that voucher is destroyed (see F-01) the audit chain silently dead-ends.

**Why this is bad (plain words).**

These are the columns that answer "where did this customer's fuel go?" If the answer is a dangling id, the audit trail the planning repo treats as the system of record cannot answer it. The planning repo is explicit that the chain must be walkable: *"the audit chain is deliberately kept: the voucher's `Fulfillment` row and its `voucher_renewal_items` link to the replacement still record where it went"* (`VOUCHER_RENEWAL.md:160-162`).

**What I propose.**

1. Add FKs to `replacement_voucher_id`, `fulfilled_voucher_id`, `acting_user_id` (×2) and `customer_user_id`, all `ON DELETE SET NULL` — they are nullable audit links, so a null is the correct degraded state, and the FK still catches a typo at write time.
2. Add indexes on `fulfilled_voucher_id`, `replacement_voucher_id`, and both `acting_user_id` columns.
3. For `customer_user_id` use `RESTRICT` rather than `SET NULL`: the planning repo says counterparty history must keep naming a real party (`SUPPLIERS_AND_VOUCHER_COST.md:59-62`), and this is a record of who was renewed, not a convenience pointer.

**Migration plan.** `NOT VALID` → `VALIDATE`, as in F-09. Pre-check for dangling ids: `SELECT count(*) FROM operator_voucher_renewals r LEFT JOIN fuel_vouchers v ON v.id = r.replacement_voucher_id WHERE r.replacement_voucher_id IS NOT NULL AND v.id IS NULL;` **[needs live DB]**

**Estimate.** S–M · low risk · half a day.

---

### F-15. The same enum is stored two ways — **Medium**

**What exists today (facts).**

`MonobankMerchant` is `varchar(20)` on `orders` (`OrderConfiguration.cs:56-59`) and `integer` on `monobank_statement_entries` (`MonobankStatementEntryConfiguration.cs:18-21`, `HasConversion<int>()`). It is the only `int`-stored enum in the database; the other nine are strings.

The reconciliation query `GetStatementReconciliationQueryHandler.cs:37` filters the int column while the order side is a string, and the unique key `IX_monobank_statement_entries_merchant_statement_id` mixes an int with a `varchar(200)`.

Related, same class of problem: `orders.monobank_status` is a typed enum (`varchar(50)`), `refunds.monobank_status` is **free text** (`varchar(30)`, written straight from the provider response at `RefundOrderCommandHandler.cs:225`), and `monobank_statement_entries.status` is free text in the acquirer's vocabulary.

**Why this is bad (plain words).**

The same concept has two storage formats, so a query that joins the two sides has to convert. If a new merchant is added as a string somewhere and an int elsewhere, the two rows for the same merchant stop matching and the reconciliation report quietly reports a mismatch. Today that is prevented only by the enum existing once in C# — one code change away from breaking.

**What I propose.**

1. **`monobank_statement_entries.merchant`: `integer` → `varchar(20)`**, matching `orders`. Rationale: a string enum survives reordering and insertion of new members; an int enum silently changes meaning when someone reorders the C# declaration. That is the only migration where int is the *better* choice, and here the value set is tiny and stable (`Live`/`Sandbox`) — but the inconsistency is the cost, and consistency wins.
2. **Do the same for `refunds.monobank_status` and `monobank_statement_entries.status`** — make them stored enums like `orders.monobank_status`, or at minimum document why they are deliberately free text (the acquirer's vocabulary may legitimately grow). Right now nobody knows which.

**Migration plan.**

1. **Step 1 — backfill.** `UPDATE monobank_statement_entries SET merchant_text = CASE merchant WHEN 0 THEN 'Live' WHEN 1 THEN 'Sandbox' END;`
2. **Step 2 — dual-write** from the application.
3. **Step 3 — switch reads**, drop the old column.

This needs the Expand/Migrate/Contract shape because it rewrites every row in a table that is never purged. **[needs live DB]** for the size. If the table is small, a single in-place `ALTER` is fine — get the row count first.

**Risks and rollback.** Getting the int→string mapping wrong corrupts reconciliation. Write the backfill as an explicit `CASE` (never `merchant::text`) and verify with `SELECT merchant, merchant_text, count(*) GROUP BY 1,2` before switching reads.

**Estimate.** M · medium risk · 1 day.

---

### F-16. Eight columns in `PascalCase` on an otherwise snake_case schema — **Medium**

**What exists today (facts).**

| Table | Column | Migration |
|---|---|---|
| `fuel_vouchers` | `IssuedAtUtc` | `20261010202352_AddVoucherIssuedAndUsedTimestamps.cs:14-24` |
| `fuel_vouchers` | `UsedAtUtc` | same |
| `orders` | `LastWebhookProcessedAtUtc` | `20260803065911_AddOrderWebhookTracking.cs:15,21` |
| `orders` | `LastWebhookModifiedDateUtc` | same |
| `users` | `PendingEmail` | `20260922155333_AddPendingEmailToUser.cs:15` |
| `users` | `PendingEmailTokenHash` | same |
| `users` | `PendingEmailExpiresAtUtc` | same |

The pattern: the developer forgot `HasColumnName(...)`, so EF used the CLR property name. The snapshot confirms it — `b.Property<DateTime?>("IssuedAtUtc").HasColumnType(...)` with no `.HasColumnName(...)` call.

**Why this is bad (plain words).**

Every column in the schema is `snake_case` except these eight. Anyone writing a report, a `psql` query, or a new migration has to remember which ones are which. It is also the visible fingerprint of the `HasColumnName` convention being opt-in rather than enforced: `UseSnakeCaseNamingConvention()` is never called anywhere, so every one of the 34 entity configurations has to remember it, and eight times out of ~250 columns somebody did not.

**What I propose.**

Two options, and the choice matters:

**Option A — rename the eight columns to `snake_case`.** Explicit `HasColumnName("issued_at_utc")` in the configuration, plus a migration that renames. Preserves the manual convention.

**Option B — adopt `UseSnakeCaseNamingConvention()` and delete all ~250 `HasColumnName` calls.** EF then derives every column name from the CLR property, so this class of mistake becomes structurally impossible.

**I recommend Option B**, and I want to be explicit that this is a judgement call against the AGENTS.md guidance to prefer existing patterns. The reasoning: the convention is already `snake_case` everywhere — Option B does not change the target schema, it changes *how it is produced*. It deletes ~250 lines of boilerplate, it removes an entire bug class, and it makes future migrations safer. The cost is one large mechanical PR touching every configuration file, and the risk that a column that was *intentionally* named differently gets renamed by accident. Mitigate by generating the migration, diffing it against the current snapshot, and confirming it produces **zero** column changes other than the eight.

Whichever you choose, do not do this at the same time as F-01 through F-09. It is a large diff and it will make the real changes hard to review.

**Migration plan (zero-downtime).** `ALTER TABLE ... RENAME COLUMN "IssuedAtUtc" TO issued_at_utc;` is metadata-only and instant. Safe to run with traffic. Do it in one migration, all eight.

**Risks and rollback.** Any raw SQL referencing the old names breaks. Grep first: `scripts/*.sql`, `backend/tests/**/Test Data/*.sql`, and `deploy/*.sh`. The reset script `scripts/reset-test-data.sql` and the renewal fixtures are the likely hits. Rename is trivially reversible.

**Estimate.** A (rename 8): S · 2 hours. B (adopt the convention): M · 3–5 days including the zero-diff verification.

---

### F-17. `qr_payload` is stored twice, and it is a bearer instrument — **Medium**

**What exists today (facts).**

`qr_payload` is `text NOT NULL` with no length cap, **and** carries a unique btree index (`IX_fuel_vouchers_qr_payload`, `FuelVoucherConfiguration.cs:169-170`) — so the full payload is stored twice per voucher.

The import code is explicit about what this column is (`ImportVouchersCommand.cs:72-83`): *"whoever holds it can have the voucher redeemed at the pump"* and *"FuelFlow cannot revoke one that leaks."* `deploy/backup.sh:16-25` calls the dump *"the single highest-value file on the server"* for exactly this reason.

Meanwhile `voucher_import_errors.raw_text` stores the full parsed region text **verbatim** (`ImportVouchersCommand.cs:251,283,308`) and is *not* passed through the `DescribeQrPayload` hash that `error_message` deliberately goes through (`:84-93`). The comment at `:72-82` explains why the hash exists — *"copies redeemable material into two stores with a wider audience and a longer retention than the voucher table itself"* — and then `raw_text` does precisely the thing the hash was added to prevent.

**Why this is bad (plain words).**

The QR payload is a key. Anyone holding it can take the fuel. Right now it is stored once as data and once in an index, which doubles how many places it exists. And a rejected voucher's QR text gets copied into the error table in plain form, into a table that the admin UI surfaces to a wider audience and that no retention job prunes.

**What I propose.**

1. **Bound the column.** `qr_payload text` → `varchar(2000)`. A QR payload that does not fit in 2000 characters will not scan at a pump anyway. This makes the btree index entry a bounded, `TOAST`-free value and stops pathological payloads from bloating the index.
2. **Hash `raw_text` on write**, using the existing `DescribeQrPayload` logic, or truncate `raw_text` to the first ~500 characters. Keep enough to debug the parse failure — which is what it is for.
3. **Do not drop the unique index.** DB-global dedup on QR payload is a genuine security control (`SECURITY.md:207`). It is worth the storage.

**Migration plan.** `ALTER TABLE fuel_vouchers ALTER COLUMN qr_payload TYPE varchar(2000);` rewrites the table. **[needs live DB]** for size. Check for payloads over 2000 chars first: `SELECT max(length(qr_payload)) FROM fuel_vouchers;` **[needs live DB]**

**Risks and rollback.** Truncating would break redemption. Verify the max length before changing the type; if some payloads are genuinely longer, use `text` and skip this item — the index duplication is then just a storage cost, not a correctness problem. The `raw_text` change is code-only and safe.

**Estimate.** M · medium risk · 1 day.

---

### F-18. No partitioning plan for the fastest-growing tables — **Medium**

**What exists today (facts).**

No `PARTITION BY` exists in any of the 75 migrations. `DataRetentionService.cs:83-138` covers eight tables; the voucher-domain tables, `monobank_statement_entries`, `support_messages` and `refunds` are not covered at all and grow monotonically.

`monobank_statement_entries` in particular is append-only, never purged (zero `Remove`/`RemoveRange` calls in the codebase), holds `raw_payload text`, and is the acquirer's immutable evidence — the planning repo is explicit that a re-import must never rewrite it (`acquiring-statement-118.md:127,171-174`).

**Why this is bad (plain words).**

Deleting rows from a Postgres table does not give the space back to the operating system. A nightly job that deletes a million error logs makes the queries faster but leaves the table exactly as large on disk, with the freed space reused only internally. Over months, every retained table becomes the high-water mark it ever reached, plus dead tuples that `VACUUM` has to keep scanning.

Partitioning by month solves this properly: dropping a partition is instant and returns the files to the OS. It also makes the retention job trivially safe.

**What I propose.**

**Do not partition anything yet.** Measure first — this is the one item where acting before measuring is actively counterproductive. **[needs live DB]** — run these against production:

```sql
-- 1. Biggest tables on disk
SELECT relname, n_live_tup, n_dead_tup,
       pg_size_pretty(pg_total_relation_size(relid)) AS total
FROM pg_stat_user_tables ORDER BY pg_total_relation_size(relid) DESC LIMIT 15;

-- 2. Bloat: dead tuples as a share of the live ones
SELECT relname, n_live_tup, n_dead_tup,
       round(100.0 * n_dead_tup / NULLIF(n_live_tup + n_dead_tup, 0), 1) AS dead_pct,
       last_autovacuum
FROM pg_stat_user_tables ORDER BY n_dead_tup DESC LIMIT 15;

-- 3. Indexes that have never been read — run this AFTER F-11's 30-day window
SELECT relname, indexrelname, idx_scan, pg_size_pretty(pg_relation_size(indexrelid))
FROM pg_stat_user_indexes WHERE idx_scan = 0 ORDER BY pg_relation_size(indexrelid) DESC;

-- 4. Autovacuum settings actually in force (the defaults may not suit bulk-import tables)
SELECT relname, reloptions FROM pg_class WHERE relname IN
  ('fuel_vouchers','error_logs','monobank_statement_entries','voucher_import_errors');

-- 5. Unused indexes that are unique (safe to drop) and FKs with no index
SELECT conrelid::regclass AS tbl, conname FROM pg_constraint c
WHERE c.contype='f' AND NOT EXISTS (
  SELECT 1 FROM pg_index i WHERE i.indrelid=c.conrelid AND i.indkey[0]=c.conkey[1]);
```

Then:

- **Below ~5M rows / ~50 GB:** skip partitioning. Tune `autovacuum_vacuum_scale_factor` per table instead — cheaper, reversible, solves the actual problem.
- **Above that,** partition the top two or three by month: almost certainly `fuel_vouchers` and `monobank_statement_entries`. Convert via `pg_partman` or a manual create-and-swap during a maintenance window. `fuel_vouchers` is hard to convert because it has 8 FKs pointing at it and 4 pointing into it — **`pg_partman` handles this, a manual `ALTER TABLE ... ATTACH PARTITION` does not.**
- **Enable `DataRetention:Enabled` (F-04) either way.** Partitioning without retention just means you drop partitions you would otherwise have kept.

**Risks and rollback.** Converting a table to partitioned is not reversible in place. This is genuinely an "eventually" item and the effort estimate is a guess until the measurements exist.

**Estimate.** L · high risk · 1–2 weeks including the measurement round-trip. **Do not schedule before F-04.**

---

### F-19. No drift gate in CI — **Medium**

**What exists today (facts).**

`.github/workflows/ci.yml` runs Build, unit tests, Testcontainers integration tests, admin/mobile/website builds, and deploys. There is **no** `dotnet ef migrations has-pending-model-changes` step, and no `dotnet ef migrations script --idempotent` check — grep for `has-pending-model-changes` across the workflows returns nothing.

The integration fixture *does* run a real `Migrate()` from zero on a fresh container (`TestDatabaseFixture.cs:86`), which catches migrations that fail to apply. It does not catch a model change that has no migration — the app boots, `Migrate()` applies everything it knows, and the missing column surfaces at runtime as a query error.

**Why this is bad (plain words).**

This is exactly the failure the team already hit once, and documented. `MigrationDiscoveryTests.cs:11-20`: *"A hand-written migration has no such file, so it compiles, sits in the folder, and is silently invisible: `Migrate()` never runs it… That is not hypothetical. Two hand-written migrations shipped that way and stayed invisible on every environment — one of them the migration the model snapshot already claimed was applied, so the constraint it adds would never exist on a fresh database, with nothing failing to say so."*

The mitigation is a regression test pinning two specific migration IDs. That catches those two. It does not catch the next one.

**What I propose.**

Add one step to the Backend job in `ci.yml`, after the build:

```yaml
- name: Verify migrations cover the model
  working-directory: backend
  run: dotnet ef migrations has-pending-model-changes --project src/FuelFlow.API --no-build
```

It exits non-zero when the model and the snapshot disagree, which is precisely the "someone changed an entity and forgot the migration" case. Cost: ~20 seconds of CI.

**Migration plan.** None. This is a CI change.

**Risks and rollback.** It will fail the build the first time it runs if the snapshot is *currently* drifted — which is itself worth knowing. Run it locally before opening the PR. Rollback is removing the step.

**Estimate.** S · very low risk · 1 hour.

---

### F-20. Migrations live in two folders, two of them are invisible — **Medium**

**What exists today (facts).**

74 migration files across two locations: `backend/src/FuelFlow.API/Migrations/` (72) and `backend/src/FuelFlow.API/Features/Auth/Migrations/` (2, namespace `FuelFlow.API.Features.Auth.Migrations`). Same assembly, so EF discovers both — but a developer looking for migrations finds one folder.

Two migrations have **no `.Designer.cs`**: `20261007073119_AddFulfillmentOrderForeignKey.cs` and `20261007091958_RenameOrderCleanupSettingKeys.cs`. Both carry explicit `[DbContext]`/`[Migration]` attributes, which is why they work — but per `MigrationDiscoveryTests.cs:11-20`, a hand-written migration missing those attributes is silently invisible, and that has already happened.

80 `migrationBuilder.Sql(...)` calls across the migration set, including `20260919113317_AddMissingDriftedTables.cs` — a migration whose *name* documents that production had schema the EF model did not know about.

**Why this is bad (plain words.**

EF finds migrations by scanning assembly attributes, not by folder. So a migration in the wrong folder works, a migration in the right folder without attributes does not, and neither failure mode is visible in the filesystem. The repo already has a test pinning two known-invisible migrations; the structural cause is two folders plus a hand-written-migration convention that has no compile-time check.

**What I propose.**

1. **Move the two `Features/Auth/Migrations/` files** into `Migrations/`. Pure filesystem move — EF discovers by attribute, so this is behaviour-neutral and makes the folder list honest. Verify by running the integration suite afterwards (it migrates from zero, so a mistake surfaces immediately).
2. **Add a guard to `MigrationDiscoveryTests`** that asserts the migration count and asserts every `.cs` in `Migrations/` has a matching `.Designer.cs` **or** an explicit `[Migration]` attribute. That closes the class of bug the existing test documents, rather than pinning two instances of it.
3. **Document the raw-SQL rule in `README.md`** — the reason `20260919113317` uses `DO $$` and `IF NOT EXISTS` (Postgres has no `ADD CONSTRAINT IF NOT EXISTS`, and `DO $$` breaks `dotnet ef migrations script --idempotent`) is currently only explained in a code comment.

**Migration plan.** None for the DB. Moving files is safe *provided* the integration suite passes.

**Risks and rollback.** If a moved file somehow does not get discovered, `Migrate()` would skip it — but the migration is already recorded in `__EFMigrationsHistory` on every existing environment, so it would only affect fresh databases. The Testcontainers suite migrates from zero and catches exactly this. `git revert` restores.

**Estimate.** S · low risk · half a day.

---

### F-21. The README's schema section is wrong — **Low**

**What exists today (facts).**

`README.md:248-268` lists tables that do not exist: `phone_verifications` (the table is `verification_codes`), `audit_log` (dropped in `20260729115004`; the audit tab reads `provider_event_outbox` — `AuditController.cs:21,58`), `referrals` (two columns on `users`), `purchases` (gone). It also names `fuel_vouchers.qr_image`, which is `image_url`.

Twelve real tables are missing from the list, several carrying money logic: `monobank_statement_entries` (`AmountKopecks`), `purchase_batches`, `voucher_exchanges` (`SurchargeUah`), `operator_voucher_renewals`, `voucher_renewal_items`, `provider_event_outbox`, `suppliers`, `roles`, `user_contracts`, `app_settings`, `push_tokens`, `support_messages`.

The same drift affects `DEPLOYMENT.md:707` ("handlers writing `audit_log`"), the money-type table at `README.md:221-223` (says `int`, converted to `numeric(12,2)` in `20260930035000`), and the AGENTS.md trade-off entry that still lists the resolved int-vs-decimal question as open.

**Why this is bad (plain words).**

A schema document that names four tables that do not exist and omits twelve that do will send the next engineer — human or AI — looking for things that are not there. It also undermines the migrations-as-canonical-source-of-truth position the README itself states at `:244-245`.

**What I propose.**

Regenerate the "Database schema" section from the model snapshot. A short script reading `ApplicationDbContextModelSnapshot.cs` `ToTable(...)` lines and emitting a table list with one line of purpose each would keep it honest permanently. If a script is overkill, hand-write it and add a CI check that fails when the table list in the README drifts from the snapshot — that is ~15 lines and it has a real payoff.

Also fix, in the same pass: the money-unit table (`README.md:218-238`), the `AGENTS.md` trade-off row, and `DEPLOYMENT.md:707`.

**Estimate.** S · zero risk · half a day.

---

### F-22. No RPO, no RTO, no PITR — **Low**

**What exists today (facts).**

`deploy/backup.sh` does a nightly `pg_dump -Fc` at 03:20, verifies it parses, encrypts it with an `age` public key the server cannot decrypt, copies to Cloudflare R2, and prunes to 14 days on-box / 30 days off-site. This is genuinely well built — the fail-closed behaviour at `:60-76` ("a backup script that quietly degrades to plaintext when a tool is missing is worse than no backup") is the right instinct.

But: a repo-wide grep for `RPO|RTO|point-in-time|PITR|wal-g|basebackup` returns **zero** matches. No WAL archiving is configured anywhere. The implied RPO is up to ~24 hours. `DEPLOYMENT.md:91-93` documents the rollback for a bad migration as *"restore a dump from before the deploy"* — there is no down-migration path.

And the restore drill is still owed, stated in three places: `SECURITY.md:230` ("still owed — one documented restore drill"), `DEPLOYMENT.md:943` (`- [ ] One documented restore drill`), `DEPLOYMENT.md:291` (*"an untested backup is a hope, not a backup"*).

**Why this is bad (plain words).**

Nightly backups mean that if something corrupts the database at 14:00, up to 11 hours of trading is gone. For a system handling voucher sales, that is the difference between an incident and a catastrophe. The backup itself is excellent; the gap is the 24-hour window and the fact that nobody has ever tested a restore.

**What I propose.**

1. **Run the restore drill.** Highest value, zero schema cost, and it has been owed for months. Restore the latest off-site dump into a scratch database on a disposable box and time it. That measured number is your RTO.
2. **Enable WAL archiving + PITR.** `archive_mode = on` with an off-box archive destination, plus `wal-g` or plain WAL shipping. This is the only way to get the RPO down from 24 hours to minutes. It requires a `pg_basebackup`-based backup to replace (or complement) `pg_dump` for full backups — `pg_dump` is a valid full backup, but WAL replay needs a consistent base.
3. **Write down the RPO/RTO.** One page in `DEPLOYMENT.md`, filled in with the measured numbers from step 1.

**Note.** This is infrastructure rather than schema, and it is listed under "best practices for a high-load fintech system" because a 24-hour RPO on a payment system is a business-level risk, not a DBA preference. It is Low severity only because the existing backup is solid and the exposure is a frequency question, not a correctness one.

**Estimate.** M for PITR · 1 week including the restore drill.

---

### F-23. Several FK-like columns have no index — **Low**

**What exists today (facts).**

| Table | Column | Index |
|---|---|---|
| `voucher_renewal_items` | `fulfilled_voucher_id` | ✗ |
| `operator_voucher_renewals` | `replacement_voucher_id` | ✗ |
| `operator_voucher_renewals` | `acting_user_id` | ✗ |
| `voucher_exchanges` | `acting_user_id` | ✗ |
| `refunds` | `invoice_id` | ✗ |
| `notifications` | (`created_at_utc`) | ✗ |

The `refunds.invoice_id` case is the one that matters: `GetStatementReconciliationQueryHandler.cs:65` filters `refunds.Where(r => invoiceIds.Contains(r.InvoiceId))` — a sequential scan of the entire refunds table inside the reconciliation endpoint, which is exactly the query an operator runs when something is already wrong.

The `notifications` case: the real read path is `WHERE user_id = @id ORDER BY created_at_utc DESC` (`GetNotificationsQueryHandler.cs:19-21`), but the only indexes are `(user_id)` and `(user_id, is_read)` — neither orders by time.

**Why this is bad (plain words).**

An unindexed foreign key is fine until something deletes or updates the parent row — then Postgres has to scan the whole child table to find rows that point at it. And a filter with no matching index means the database reads every row and throws most of them away, which gets worse as the table grows and never gets better.

**What I propose.**

- `refunds (invoice_id)` — directly serves the reconciliation query.
- `notifications (user_id, created_at_utc DESC)` — replaces the redundant `notifications.user_id` (F-11) rather than adding to it.
- The four `uuid` columns from F-14 get their indexes there, as part of adding the FKs.

**Migration plan.** `CREATE INDEX CONCURRENTLY`, outside a migration transaction.

**Estimate.** S · low risk · half a day.

---

## D. What NOT to change, and why

**Natural keys → surrogate UUIDs (`stations`, `fuel_types`, `fuel_packages`).** Listed in AGENTS.md as an accepted trade-off, and I agree it should stay. `fuel_types.id = 'okko-95'` is read from supplier PDFs and appears in vouchers, order lines, purchase batches, and fuel packages. Converting to UUIDs would mean a mapping table or a crosswalk column on every one of those, and would break the import parser. The cost is real (`text` PKs have larger indexes than `uuid` PKs) but it is a one-time cost that is already paid.

**Merge `outbox_events` and `provider_event_outbox`.** Different concerns, as AGENTS.md says: one is transactional (written in the same commit as the state change, dispatched by a worker), the other is immutable audit. Merging them would force one retention policy on two very different data lifetimes. Keep them apart.

**Kopecks at the Monobank boundary.** The domain stores decimal UAH, kopecks exist only at the wire. `Money.cs` documents the rule and `README.md:230-238` documents the rationale. This is correct — do not normalise units (see F-08, which is about *types*, not units).

**`contracts.user_id` is redundant with `legal_entities.user_id`.** Yes, and it should stay. The contract is the record of what a specific user signed for a specific legal entity; deriving it from `legal_entities` would break the moment a legal entity is transferred between users.

**`refresh_tokens.role_name_at_issue` and the `*_user_name` denormalisations.** Deliberate. The role snapshot means a promotion does not silently upgrade a live session (`RefreshToken.cs:23-31`); the name snapshots mean audit rows keep a readable actor even after a rename. With F-09's `RESTRICT` FKs on the `_user_id` columns, this becomes safe rather than merely intentional.

**`fuel_vouchers.provider` duplicating `fuel_type_id → fuel_types.station_id`.** The planning repo is explicit that the brand is *printed on the voucher* and `BrandFuelPairing.cs:7-11` documents that a mismatch "just stops being sellable with no error anywhere". Normalising it would remove a genuine validation signal. Keep it; add the CHECK that it matches (F-06) only if the import guarantees it — verify first.

**The single `DbContext`.** A second context would not help here. The `IImportVouchersDbContext` interface-facade is a narrow testing seam, not a second unit of work.

**`Maximum Pool Size=5`.** Small, but it is deliberate and documented in code (`DatabaseSetup.cs:46-48`) and it is overridable from the connection string. At current scale, 5 is fine. Revisit when connection latency or the number of worker processes grows — not before.

**`fulfillments.id` being `integer` identity while its siblings are `uuid`.** Inconsistent, but `fulfillments` is a junction table that grows one row per voucher per order and is never read by id from outside. A `bigserial` is smaller and faster than a `uuid` here. Changing it buys nothing.

---

## E. Roadmap

### Stage 1 — Now (the data-loss and access bugs)

**Target: 1–2 days.** No downtime. No schema rewrites.

| # | Item | Why first |
|---|---|---|
| F-01 | `fuel_type_id` CASCADE → `RESTRICT` | Can destroy paid-for vouchers. Armed, untested. |
| F-02 | `users` query filter + session check | Deleted accounts still work. |
| F-03 | Fix the restore path | A feature that silently does nothing. |
| F-05 | Fix the `qr_parameters` leak | One-line fix, stops unbounded growth. |
| F-19 | CI drift gate | Prevents the next invisible migration. |

F-19 goes first even though it is the least urgent — it is the gate that makes the rest of this roadmap safe to execute over several weeks.

### Stage 2 — This quarter (integrity and operability)

**Target: 2–3 weeks.** All DDL is `NOT VALID` → `VALIDATE`, so no downtime.

| # | Item | Notes |
|---|---|---|
| F-06 | CHECK constraints, in the model | **Pre-check every predicate against live data first — non-zero counts are findings in themselves.** |
| F-09 | The missing foreign keys | Same `NOT VALID` sequence; `RESTRICT` on audit columns. |
| F-07 | Concurrency tokens, staged | Ship the property first, log conflicts for a release, then fix handlers. |
| F-04 | Enable retention, add voucher-domain coverage | Staging soak with dry-run first; never prune settlement tables. |
| F-11 | Drop redundant + dead indexes | Needs a 30-day `pg_stat` observation window — start it now. |
| F-08 | `refunds.amount` → `bigint`; `amount_paid` → `numeric(12,2)` | Instant for the first; measure the second. |
| F-10 | Exchange/renewal constraints | Raise the `UNIQUE (old_voucher_id)` question with the business first. |
| F-14, F-23 | Audit FKs + missing indexes | Cheap, same pattern as F-09. |
| F-21, F-20 | Fix the docs, consolidate migrations | Zero risk, keeps the next engineer honest. |

Run the F-18 measurement queries **now**, in parallel with Stage 1 — the results decide whether F-18 is a tuning exercise or a two-week project.

### Stage 3 — When there is time (structure and scale)

**Target: quarter-plus.** Each is independently justifiable.

| # | Item | Trigger |
|---|---|---|
| F-18 | Partitioning, if the measurements justify it | `fuel_vouchers` or `monobank_statement_entries` > ~5M rows or ~50 GB |
| F-15 | Unify `MonobankMerchant` storage | Do it with the two-merchant routing epic, not separately |
| F-13 | Bound the audit `text` columns | `error_logs` approaching 10 GB |
| F-16 | snake_case convention (option B recommended) | Alone in its own PR — do not bundle with anything above |
| F-22 | PITR + the restore drill | **Start the drill immediately even if PITR waits** — it is owed and costs nothing but an hour |

**Sequencing note.** F-16 touches every configuration file and would make any concurrent PR unreviewable. Do not start it while F-06/F-09 are in flight.

---

## F. Questions for the team

1. **Is `voucher_number` / `qr_payload` uniqueness meant to survive soft delete?** Today a soft-deleted voucher permanently occupies the unique namespace (`FuelVoucherConfiguration.cs:166-170` have no partial filter, unlike `users.phone_number` and `orders.idempotency_key`), while the import duplicate check runs *through* the `!IsDeleted` filter (`ImportVouchersCommand.cs:314-316`). The result: re-importing a soft-deleted voucher passes the check and then fails on the index. Add `WHERE is_deleted = false` to both, or document that soft-deleted voucher numbers are permanently burned?
2. **Should a contract have an expiry?** `contracts` is `{Id, UserId, LegalEntityId, StationId, CreatedAtUtc}` and `user_contracts` adds `SignatureData` and `SignedAtUtc`. No status, no validity window, no `ContractStatus` enum — and no doc describes one. Is this intentional?
3. **Can one supplier voucher be renewed more than once?** `VOUCHER_RENEWAL.md:370` lists this as an open question, and I have deliberately not proposed a `UNIQUE (voucher_id)`. The answer determines whether F-10 can include that constraint.
4. **What is the RPO the business actually needs?** (F-22.) If the answer is "an hour", PITR is a requirement rather than an improvement, and it belongs in Stage 1.
5. **Is Hangfire's schema separation wanted?** Hangfire's ~7 tables live in the application database, created outside EF Core, and are not mentioned in any doc. Not a problem today at this scale; worth a deliberate decision before it becomes one.
6. **`suppliers.edr_ipn` is nullable and not unique while `legal_entities.edrpou` is `UNIQUE`.** Same registry code, same jurisdiction, opposite constraints. Which is right?

---

## Appendix — verification notes

- All findings verified against `origin/main` @ `62cee4f3`. The working tree at `f2d19e57` is one commit behind; **F-01 is the only finding that commit changes**, and it changes the code path only — the `ON DELETE CASCADE` constraint is still in the snapshot at `62cee4f3` (`ApplicationDbContextModelSnapshot.cs:3084-3088`), still armed and now untested.
- Independently confirmed by direct search, not only by reading the sub-reports: exactly 3 `HasQueryFilter` calls in `src` (`CompanyMemberConfiguration.cs:47`, `OrderConfiguration.cs:88`, `FuelVoucherConfiguration.cs:150`); exactly 0 `HasQueryFilter` in `UserConfiguration.cs`; exactly 0 occurrences of `IsConcurrencyToken` / `UseXminAsConcurrencyToken` / `IsRowVersion` in `src`; exactly 1 `HasFilter` on an index in `UserConfiguration.cs` (`:101`) which is an index predicate, not a row filter; `SessionValidationMiddleware.cs:35-36` selects only `{IsBanned, TokenVersion}`; `GetDashboardQueryHandler.cs:22` counts `Users` unfiltered; `OrderConfiguration.cs` declares relationships only for `LegalEntityId` and `SourceOrderId`.
- **No live database was reachable** in this environment (ports 5432 and 5433 closed, no running containers). Every recommendation involving table size, index usage, lock duration, or data-violation counts is marked **[needs live DB]** and depends on the queries in §F-18 step 1 / §F-11. Before executing Stage 2, run those.
- Claims I could not verify and did not assert: actual row counts, actual index usage, actual disk usage, and whether the CHECK constraints proposed in F-06 would pass on existing data. All four are one query each.