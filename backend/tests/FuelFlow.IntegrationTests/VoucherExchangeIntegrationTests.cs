using FluentAssertions;
using FuelFlow.Features.Orders.SharedModels;
using FuelFlow.Features.Providers;
using FuelFlow.Features.Vouchers;
using FuelFlow.Features.Vouchers.BulkActionVouchers;
using FuelFlow.Features.Vouchers.Exchange;
using FuelFlow.Features.Vouchers.PurchaseBatchCost;
using FuelFlow.Features.Vouchers.SharedModels;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using FuelFlow.SharedKernel.Observability;
using Hangfire;
using Hangfire.Common;
using Hangfire.States;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace FuelFlow.IntegrationTests;

/// <summary>
/// Testcontainers coverage for the operator→provider exchange path (planning #104). Unlike the pure
/// pairing helper (unit-tested on <see cref="VoucherExchangePairing"/>),
/// <see cref="ConfirmVoucherExchangeCommandHandler"/> leans on a real transaction that chains the
/// reuse handlers (SetBatchCost's SaveChanges-first reprice + BulkAction's cost-gate activate) and
/// flips old-voucher statuses, so it only exercises faithfully against real Postgres. The migration
/// seeds fuel types/packages for "okko-95"/"wog-95", so those reprice without extra seeding.
/// </summary>
[Collection("Integration Tests")]
public sealed class VoucherExchangeIntegrationTests : IClassFixture<TestDatabaseFixture>
{
    private readonly TestDatabaseFixture _fixture;

    public VoucherExchangeIntegrationTests(TestDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task HappyPath_EqualCounts_ExpiresOldsActivatesNewsAndWritesPairedRows()
    {
        var actingUserId = Guid.NewGuid();
        var importId = Guid.NewGuid();
        var old1 = Guid.NewGuid();
        var old2 = Guid.NewGuid();
        var new1 = Guid.NewGuid();
        var new2 = Guid.NewGuid();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        using (var seed = CreateContext())
        {
            await seed.Database.MigrateAsync();
            await ResetDataAsync(seed);

            SeedUser(seed, actingUserId);
            SeedImport(seed, importId);
            // Old stock near/at expiry (operator-owned: no assignment / worker).
            seed.FuelVouchers.Add(Stock(old1, "okko", "okko-95", 50m, today.AddDays(3), VoucherStatus.Available));
            seed.FuelVouchers.Add(Stock(old2, "okko", "okko-95", 50m, today.AddDays(1), VoucherStatus.Available));
            // Freshly imported replacements (Imported = not yet on sale, awaiting cost + activate).
            seed.FuelVouchers.Add(Stock(new1, "okko", "okko-95", 50m, today.AddMonths(6), VoucherStatus.Imported, importId));
            seed.FuelVouchers.Add(Stock(new2, "okko", "okko-95", 50m, today.AddMonths(6), VoucherStatus.Imported, importId));

            await seed.SaveChangesAsync();
        }

        ConfirmVoucherExchangeResult result;
        using (var ctx = CreateContext())
        {
            result = await BuildHandler(ctx).HandleAsync(new ConfirmVoucherExchangeCommand(
                OldVoucherIds: new List<Guid> { old1, old2 },
                NewImportId: importId,
                Costs: new List<ExchangeFuelCost> { new("okko-95", 30m) },
                SurchargeUah: 200m,
                InvoiceNumber: "INV-104",
                InvoiceDate: today,
                ActingUserId: actingUserId,
                ActingUserName: "Operator"));
        }

        result.Success.Should().BeTrue(result.Error);
        result.ExpiredCount.Should().Be(2);
        result.NewActivatedCount.Should().Be(2);
        result.PairedCount.Should().Be(2);
        result.UnpairedOldCount.Should().Be(0);
        result.UnpairedNewCount.Should().Be(0);
        // planning #136 — the 200 UAH surcharge must raise the repriced cost, not just sit in the audit
        // row: base 30 + 200/100 new L = 32. (Pool is the new batch only; olds are now Expired.)
        result.BlendedCostPerLiter.Should().Be(32m);

        using var verify = CreateContext();

        var olds = await verify.FuelVouchers.AsNoTracking().Where(v => v.Id == old1 || v.Id == old2).ToListAsync();
        olds.Should().OnlyContain(v => v.Status == VoucherStatus.Expired);

        var news = await verify.FuelVouchers.AsNoTracking().Where(v => v.Id == new1 || v.Id == new2).ToListAsync();
        news.Should().OnlyContain(v => v.Status == VoucherStatus.Available);

        var exchanges = await verify.VoucherExchanges.AsNoTracking().ToListAsync();
        exchanges.Should().HaveCount(2);
        exchanges.Should().OnlyContain(x => x.ExchangeBatchId == result.ExchangeBatchId);
        exchanges.Should().OnlyContain(x => x.NewVoucherId != null);
        exchanges.Should().OnlyContain(x => x.SurchargeUah == 200m && x.CostPerLiterApplied == 32m); // surcharge folded into the applied cost
        exchanges.Should().OnlyContain(x => x.InvoiceNumber == "INV-104");

        var batch = await verify.PurchaseBatches.AsNoTracking()
            .FirstAsync(b => b.ImportJobId == importId && b.FuelTypeId == "okko-95");
        batch.CostPerLiter.Should().Be(32m); // reprice bakes in the surcharge

        var audit = await verify.Set<ProviderEventOutbox>().AsNoTracking()
            .Where(e => e.EventType == "VoucherExchanged").ToListAsync();
        audit.Should().ContainSingle(e => e.AggregateId == result.ExchangeBatchId.ToString());
    }

    [Fact]
    public async Task ZeroSurcharge_RepricesAtBaseCostWithNoInflation()
    {
        // planning #136 guard: with no surcharge the fold is a no-op — the repriced and applied cost
        // must stay exactly the operator-entered base (we must not accidentally inflate at zero).
        var actingUserId = Guid.NewGuid();
        var importId = Guid.NewGuid();
        var old1 = Guid.NewGuid();
        var new1 = Guid.NewGuid();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        using (var seed = CreateContext())
        {
            await seed.Database.MigrateAsync();
            await ResetDataAsync(seed);

            SeedUser(seed, actingUserId);
            SeedImport(seed, importId);
            seed.FuelVouchers.Add(Stock(old1, "okko", "okko-95", 50m, today.AddDays(1), VoucherStatus.Available));
            seed.FuelVouchers.Add(Stock(new1, "okko", "okko-95", 50m, today.AddMonths(6), VoucherStatus.Imported, importId));

            await seed.SaveChangesAsync();
        }

        ConfirmVoucherExchangeResult result;
        using (var ctx = CreateContext())
        {
            result = await BuildHandler(ctx).HandleAsync(new ConfirmVoucherExchangeCommand(
                new List<Guid> { old1 }, importId,
                new List<ExchangeFuelCost> { new("okko-95", 30m) },
                SurchargeUah: 0m, null, null, actingUserId, "Operator"));
        }

        result.Success.Should().BeTrue(result.Error);
        result.BlendedCostPerLiter.Should().Be(30m); // no surcharge → cost unchanged

        using var verify = CreateContext();
        var exchanges = await verify.VoucherExchanges.AsNoTracking().ToListAsync();
        exchanges.Should().OnlyContain(x => x.SurchargeUah == 0m && x.CostPerLiterApplied == 30m);
        var batch = await verify.PurchaseBatches.AsNoTracking()
            .FirstAsync(b => b.ImportJobId == importId && b.FuelTypeId == "okko-95");
        batch.CostPerLiter.Should().Be(30m);
    }

    [Fact]
    public async Task Flexible_MoreOldThanNew_LeftoverOldExpiresWithNullNewVoucher()
    {
        var actingUserId = Guid.NewGuid();
        var importId = Guid.NewGuid();
        var old1 = Guid.NewGuid();
        var old2 = Guid.NewGuid();
        var new1 = Guid.NewGuid();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        using (var seed = CreateContext())
        {
            await seed.Database.MigrateAsync();
            await ResetDataAsync(seed);

            SeedUser(seed, actingUserId);
            SeedImport(seed, importId);
            seed.FuelVouchers.Add(Stock(old1, "okko", "okko-95", 50m, today.AddDays(-2), VoucherStatus.Expired));
            seed.FuelVouchers.Add(Stock(old2, "okko", "okko-95", 50m, today.AddDays(2), VoucherStatus.Available));
            seed.FuelVouchers.Add(Stock(new1, "okko", "okko-95", 50m, today.AddMonths(6), VoucherStatus.Imported, importId));

            await seed.SaveChangesAsync();
        }

        ConfirmVoucherExchangeResult result;
        using (var ctx = CreateContext())
        {
            result = await BuildHandler(ctx).HandleAsync(new ConfirmVoucherExchangeCommand(
                new List<Guid> { old1, old2 }, importId,
                new List<ExchangeFuelCost> { new("okko-95", 28m) },
                150m, null, null, actingUserId, "Operator"));
        }

        result.Success.Should().BeTrue(result.Error);
        result.PairedCount.Should().Be(1);
        result.UnpairedOldCount.Should().Be(1);
        result.UnpairedNewCount.Should().Be(0);

        using var verify = CreateContext();

        var exchanges = await verify.VoucherExchanges.AsNoTracking().ToListAsync();
        exchanges.Should().HaveCount(2); // one row per OLD
        exchanges.Count(x => x.NewVoucherId == new1).Should().Be(1);
        exchanges.Count(x => x.NewVoucherId == null).Should().Be(1);

        var news = await verify.FuelVouchers.AsNoTracking().FirstAsync(v => v.Id == new1);
        news.Status.Should().Be(VoucherStatus.Available);
    }

    [Fact]
    public async Task Flexible_MoreNewThanOld_LeftoverNewEntersStockWithNoRow()
    {
        var actingUserId = Guid.NewGuid();
        var importId = Guid.NewGuid();
        var old1 = Guid.NewGuid();
        var new1 = Guid.NewGuid();
        var new2 = Guid.NewGuid();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        using (var seed = CreateContext())
        {
            await seed.Database.MigrateAsync();
            await ResetDataAsync(seed);

            SeedUser(seed, actingUserId);
            SeedImport(seed, importId);
            seed.FuelVouchers.Add(Stock(old1, "okko", "okko-95", 50m, today.AddDays(1), VoucherStatus.Available));
            seed.FuelVouchers.Add(Stock(new1, "okko", "okko-95", 50m, today.AddMonths(6), VoucherStatus.Imported, importId));
            seed.FuelVouchers.Add(Stock(new2, "okko", "okko-95", 50m, today.AddMonths(6), VoucherStatus.Imported, importId));

            await seed.SaveChangesAsync();
        }

        ConfirmVoucherExchangeResult result;
        using (var ctx = CreateContext())
        {
            result = await BuildHandler(ctx).HandleAsync(new ConfirmVoucherExchangeCommand(
                new List<Guid> { old1 }, importId,
                new List<ExchangeFuelCost> { new("okko-95", 26m) },
                100m, null, null, actingUserId, "Operator"));
        }

        result.Success.Should().BeTrue(result.Error);
        result.PairedCount.Should().Be(1);
        result.UnpairedOldCount.Should().Be(0);
        result.UnpairedNewCount.Should().Be(1);
        result.NewActivatedCount.Should().Be(2); // both news go on sale even though one is unpaired

        using var verify = CreateContext();

        var exchanges = await verify.VoucherExchanges.AsNoTracking().ToListAsync();
        exchanges.Should().ContainSingle(); // one row per OLD only — leftover new gets no row
        exchanges.Single().NewVoucherId.Should().NotBeNull();

        var news = await verify.FuelVouchers.AsNoTracking().Where(v => v.Id == new1 || v.Id == new2).ToListAsync();
        news.Should().OnlyContain(v => v.Status == VoucherStatus.Available);
    }

    [Fact]
    public async Task Validation_AssignedOldVoucher_IsRejectedAndNothingChanges()
    {
        var actingUserId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var importId = Guid.NewGuid();
        var assignedOld = Guid.NewGuid();
        var new1 = Guid.NewGuid();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        using (var seed = CreateContext())
        {
            await seed.Database.MigrateAsync();
            await ResetDataAsync(seed);

            SeedUser(seed, actingUserId);
            SeedUser(seed, customerId);
            SeedImport(seed, importId);
            // The customer's voucher must look legitimately held: an order delivered it, so
            // ck_voucher_held_has_order is satisfied and the rejection below is provably about the
            // exchange rules, not about an impossible row.
            var purchaseOrderId = SeedPurchaseOrder(seed, customerId);
            var assigned = Stock(assignedOld, "okko", "okko-95", 50m, today.AddDays(1), VoucherStatus.Assigned, orderId: purchaseOrderId);
            assigned.AssignedToUserId = customerId; // a customer's voucher — out of scope for v1
            seed.FuelVouchers.Add(assigned);
            seed.FuelVouchers.Add(Stock(new1, "okko", "okko-95", 50m, today.AddMonths(6), VoucherStatus.Imported, importId));

            await seed.SaveChangesAsync();
        }

        ConfirmVoucherExchangeResult result;
        using (var ctx = CreateContext())
        {
            result = await BuildHandler(ctx).HandleAsync(new ConfirmVoucherExchangeCommand(
                new List<Guid> { assignedOld }, importId,
                new List<ExchangeFuelCost> { new("okko-95", 30m) },
                200m, null, null, actingUserId, "Operator"));
        }

        result.Success.Should().BeFalse();
        result.Error.Should().NotBeNullOrEmpty();

        using var verify = CreateContext();
        var old = await verify.FuelVouchers.AsNoTracking().FirstAsync(v => v.Id == assignedOld);
        old.Status.Should().Be(VoucherStatus.Assigned); // untouched
        var newV = await verify.FuelVouchers.AsNoTracking().FirstAsync(v => v.Id == new1);
        newV.Status.Should().Be(VoucherStatus.Imported); // never activated
        (await verify.VoucherExchanges.AsNoTracking().AnyAsync()).Should().BeFalse();
        (await verify.PurchaseBatches.AsNoTracking().AnyAsync(b => b.ImportJobId == importId)).Should().BeFalse();
    }

    [Fact]
    public async Task Validation_UnknownImport_IsRejected()
    {
        var actingUserId = Guid.NewGuid();
        var old1 = Guid.NewGuid();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        using (var seed = CreateContext())
        {
            await seed.Database.MigrateAsync();
            await ResetDataAsync(seed);

            SeedUser(seed, actingUserId);
            seed.FuelVouchers.Add(Stock(old1, "okko", "okko-95", 50m, today.AddDays(1), VoucherStatus.Available));

            await seed.SaveChangesAsync();
        }

        using var ctx = CreateContext();
        var result = await BuildHandler(ctx).HandleAsync(new ConfirmVoucherExchangeCommand(
            new List<Guid> { old1 }, Guid.NewGuid(), // import that has no vouchers
            new List<ExchangeFuelCost> { new("okko-95", 30m) },
            200m, null, null, actingUserId, "Operator"));

        result.Success.Should().BeFalse();
        result.Error.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Idempotency_ReExchangingAnAlreadyExchangedVoucher_IsRejected()
    {
        var actingUserId = Guid.NewGuid();
        var importId = Guid.NewGuid();
        var secondImportId = Guid.NewGuid();
        var old1 = Guid.NewGuid();
        var new1 = Guid.NewGuid();
        var new2 = Guid.NewGuid();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        using (var seed = CreateContext())
        {
            await seed.Database.MigrateAsync();
            await ResetDataAsync(seed);

            SeedUser(seed, actingUserId);
            SeedImport(seed, importId);
            SeedImport(seed, secondImportId);
            seed.FuelVouchers.Add(Stock(old1, "okko", "okko-95", 50m, today.AddDays(1), VoucherStatus.Available));
            seed.FuelVouchers.Add(Stock(new1, "okko", "okko-95", 50m, today.AddMonths(6), VoucherStatus.Imported, importId));
            seed.FuelVouchers.Add(Stock(new2, "okko", "okko-95", 50m, today.AddMonths(6), VoucherStatus.Imported, secondImportId));

            await seed.SaveChangesAsync();
        }

        using (var ctx = CreateContext())
        {
            var first = await BuildHandler(ctx).HandleAsync(new ConfirmVoucherExchangeCommand(
                new List<Guid> { old1 }, importId,
                new List<ExchangeFuelCost> { new("okko-95", 30m) },
                200m, null, null, actingUserId, "Operator"));
            first.Success.Should().BeTrue(first.Error);
        }

        ConfirmVoucherExchangeResult second;
        using (var ctx = CreateContext())
        {
            // old1 is already recorded in voucher_exchanges → re-confirm must be refused (no double-apply).
            second = await BuildHandler(ctx).HandleAsync(new ConfirmVoucherExchangeCommand(
                new List<Guid> { old1 }, secondImportId,
                new List<ExchangeFuelCost> { new("okko-95", 32m) },
                200m, null, null, actingUserId, "Operator"));
        }

        second.Success.Should().BeFalse();

        using var verify = CreateContext();
        (await verify.VoucherExchanges.AsNoTracking().CountAsync()).Should().Be(1); // only the first stuck
        var leftoverNew = await verify.FuelVouchers.AsNoTracking().FirstAsync(v => v.Id == new2);
        leftoverNew.Status.Should().Be(VoucherStatus.Imported); // second batch never activated
    }

    // ---- Helpers -------------------------------------------------------------------------------

    private static ConfirmVoucherExchangeCommandHandler BuildHandler(ApplicationDbContext ctx)
    {
        var recalculator = new BlendedCostRecalculator(ctx);
        var eventService = new ProviderEventService(ctx);
        var setBatchCost = new SetBatchCostCommandHandler(ctx, recalculator, eventService, NotificationDispatcher.Disabled);

        var backgroundJobClient = new Mock<IBackgroundJobClient>();
        backgroundJobClient.Setup(c => c.Create(It.IsAny<Job>(), It.IsAny<IState>())).Returns("job-id");
        var bulkAction = new BulkActionVouchersCommandHandler(ctx, backgroundJobClient.Object, eventService);

        return new ConfirmVoucherExchangeCommandHandler(ctx, setBatchCost, bulkAction, eventService);
    }

    private static void SeedUser(ApplicationDbContext ctx, Guid userId)
        => ctx.Users.Add(new User
        {
            Id = userId,
            PhoneNumber = $"+38{userId:N}"[..20],
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        });

    private static void SeedImport(ApplicationDbContext ctx, Guid importId)
        => ctx.VoucherImports.Add(new VoucherImport
        {
            Id = importId,
            FileName = $"import-{importId:N}.pdf",
            Status = "Completed",
            StartedAtUtc = DateTime.UtcNow,
            CompletedAtUtc = DateTime.UtcNow
        });

    /// <summary>
    /// The purchase that delivered a customer's voucher. A held voucher (Assigned/Used/Blocked) must
    /// carry an <c>order_id</c> — ck_voucher_held_has_order rejects the row otherwise. Warehouse stock
    /// (Available/Imported/Expired) must keep it null, so <see cref="Stock"/> takes it as optional.
    /// </summary>
    private static Guid SeedPurchaseOrder(ApplicationDbContext ctx, Guid userId)
    {
        var orderId = Guid.NewGuid();
        ctx.Orders.Add(new Order
        {
            Id = orderId,
            UserId = userId,
            Price = 0,
            Status = OrderStatus.Fulfilled,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        });
        return orderId;
    }

    private static FuelVoucher Stock(Guid id, string provider, string fuelTypeId, decimal liters, DateOnly expiry, VoucherStatus status, Guid? importId = null, Guid? orderId = null)
        => new()
        {
            Id = id,
            Provider = provider,
            FuelTypeId = fuelTypeId,
            Liters = liters,
            ProviderExpirationDate = expiry,
            CustomerExpirationDate = expiry,
            VoucherNumber = $"VX-{id:N}"[..16],
            QrPayload = $"qr-{id:N}",
            Status = status,
            ImportJobId = importId,
            OrderId = orderId,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };

    private static async Task ResetDataAsync(ApplicationDbContext context)
    {
        await context.Database.ExecuteSqlRawAsync(
            """TRUNCATE TABLE "voucher_exchanges", "purchase_batches", "voucher_imports", "fulfillments", "voucher_renewal_items", "orders", "order_line_items", "outbox_events", "fuel_vouchers", "users", "provider_event_outbox" RESTART IDENTITY CASCADE""");
    }

    private ApplicationDbContext CreateContext()
        => new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(_fixture.DbContainer.GetConnectionString())
            .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking)
            .Options);
}
