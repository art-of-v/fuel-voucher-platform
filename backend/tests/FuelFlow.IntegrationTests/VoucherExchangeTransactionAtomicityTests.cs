using FuelFlow.Features.Pricing;
using FluentAssertions;
using Hangfire;
using Moq;
using FuelFlow.Features.Orders.SharedModels;
using FuelFlow.Features.Brands;
using FuelFlow.Features.Vouchers;
using FuelFlow.Features.Vouchers.BulkActionVouchers;
using FuelFlow.Features.Vouchers.Exchange;
using FuelFlow.Features.Vouchers.PurchaseBatchCost;
using FuelFlow.Features.Vouchers.SharedModels;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using FuelFlow.SharedKernel.Observability;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using static FuelFlow.SharedKernel.Domain.FuelIdMapper;

namespace FuelFlow.IntegrationTests;

/// <summary>
/// #277: the exchange stamped each new voucher's own cost **before** it opened the transaction, so
/// anything that failed afterwards rolled the exchange back while the stamp survived. What was left
/// was a voucher priced by an exchange that never happened, carrying no exchange row to say so — and
/// the invariant the neighbouring comment is written to protect ("no transient under-price") is exactly
/// what that state is.
/// </summary>
/// <remarks>
/// The failure is provoked with a <see cref="SaveChangesInterceptor"/> rather than a mock seam,
/// because the production code has none to give: the recalculator is sealed, the bulk-action handler
/// is concrete, and every validation in the handler runs *before* the stamp. An interceptor on the
/// tracked save is the one thing that can fail deterministically at a chosen point inside the
/// transaction — no concurrency, no timing, no fault injection.
/// </remarks>
[Collection("Integration Tests")]
public sealed class VoucherExchangeTransactionAtomicityTests : IClassFixture<TestDatabaseFixture>
{
    private readonly TestDatabaseFixture _fixture;

    public VoucherExchangeTransactionAtomicityTests(TestDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task A_Failure_After_The_Cost_Stamp_Leaves_The_New_Vouchers_Priced_As_They_Were()
    {
        var actingUserId = Guid.NewGuid();
        var supplierId = Guid.NewGuid();
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
            SeedSupplier(seed, supplierId);
            SeedImport(seed, importId);
            seed.FuelVouchers.Add(Stock(old1, today, VoucherStatus.Available, supplierId: supplierId, costPerLiter: 30m));
            seed.FuelVouchers.Add(Stock(old2, today, VoucherStatus.Available, supplierId: supplierId, costPerLiter: 30m));
            // No cost and no supplier on the replacements, exactly as an operator's PDF arrives: the exchange
            // is what derives both, from the olds. That also makes the supplier link observable - a
            // seeded supplier would prove nothing about whether the statement rolled back.
            seed.FuelVouchers.Add(Stock(new1, today, VoucherStatus.Imported, importId: importId));
            seed.FuelVouchers.Add(Stock(new2, today, VoucherStatus.Imported, importId: importId));

            await seed.SaveChangesAsync();
        }

        // The second tracked save is the one that flushes the recalculated blended cost, which runs
        // after the stamp and after the olds are expired. Failing there is a failure *inside* the
        // transaction and *after* the money-relevant write.
        var failOnSave = 2;
        var interceptor = new ThrowOnNthSaveChanges(failOnSave);

        using (var ctx = CreateContext(interceptor))
        {
            var eventService = new AuditEventService(ctx);
            var handler = new ConfirmVoucherExchangeCommandHandler(
                ctx,
                new BlendedCostRecalculator(ctx, new PriceHistoryRecorder(ctx)),
                new BulkActionVouchersCommandHandler(ctx, new Mock<IBackgroundJobClient>().Object, eventService),
                eventService);

            await Assert.ThrowsAsync<InvalidOperationException>(() => handler.HandleAsync(
                new ConfirmVoucherExchangeCommand(
                    OldVoucherIds: new List<Guid> { old1, old2 },
                    NewImportId: importId,
                    Costs: new List<ExchangeFuelCost> { new(ToUuid("okko-95"), 30m) },
                    SurchargeUah: 200m,
                    ActingUserId: actingUserId,
                    ActingUserName: "Operator")));
        }

        interceptor.ThrowCount.Should().BeGreaterThanOrEqualTo(1, "the interceptor is what provoked the failure");

        using var verify = CreateContext();

        var news = await verify.FuelVouchers.IgnoreQueryFilters().AsNoTracking()
            .Where(v => v.Id == new1 || v.Id == new2)
            .ToListAsync();

        news.Should().HaveCount(2);
        news.Should().OnlyContain(v => v.CostPerLiter == null,
            "the stamp is inside the transaction now, so a failure after it takes the cost with it - a voucher " +
            "must never carry a price derived from an exchange that was rolled back");
        news.Should().OnlyContain(v => v.SupplierId == null,
            "the supplier link is part of the same statement and must roll back with the cost");

        var olds = await verify.FuelVouchers.IgnoreQueryFilters().AsNoTracking()
            .Where(v => v.Id == old1 || v.Id == old2)
            .ToListAsync();
        olds.Should().OnlyContain(v => v.Status == VoucherStatus.Available,
            "the olds were never retired: the exchange did not happen");

        (await verify.VoucherExchanges.IgnoreQueryFilters().AsNoTracking().ToListAsync())
            .Should().BeEmpty("no exchange rows survive a rolled-back exchange");
    }

    /// <summary>
    /// Fails the Nth tracked save, to place a failure at a point that has no other seam. Raw
    /// <c>ExecuteUpdateAsync</c> statements do not pass through here - they bypass the change tracker
    /// entirely - which is the point: the stamp under test is one of them, so it is the *subsequent*
    /// tracked save that has to fail for the rollback to be observable.
    /// </summary>
    private sealed class ThrowOnNthSaveChanges : SaveChangesInterceptor
    {
        private readonly int _throwOnSave;
        private int _saves;

        public ThrowOnNthSaveChanges(int throwOnSave) => _throwOnSave = throwOnSave;

        public int ThrowCount { get; private set; }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            _saves++;
            if (_saves != _throwOnSave)
                return ValueTask.FromResult(result);

            ThrowCount++;
            throw new InvalidOperationException(
                $"Deliberate failure at tracked save #{_saves}, inside the exchange transaction.");
        }
    }

    private static void SeedUser(ApplicationDbContext ctx, Guid userId) => ctx.Users.Add(new User
    {
        Id = userId,
        PhoneNumber = $"+38{userId:N}"[..20],
        FirstName = "Operator",
        IsActive = true,
        CreatedAtUtc = DateTime.UtcNow,
        UpdatedAtUtc = DateTime.UtcNow
    });

    private static void SeedSupplier(ApplicationDbContext ctx, Guid supplierId) => ctx.Suppliers.Add(new Supplier
    {
        Id = supplierId,
        Name = "Seed Supplier",
        LegalForm = "ФОП",
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

    private static FuelVoucher Stock(
        Guid id,
        DateOnly expiry,
        VoucherStatus status,
        Guid? supplierId = null,
        decimal? costPerLiter = null,
        Guid? importId = null)
        => new()
        {
            Id = id,
            Brand = "okko",
            FuelTypeId = ToUuid("okko-95"),
            Liters = 50m,
            SupplierExpirationDate = expiry,
            CustomerExpirationDate = expiry,
            VoucherNumber = $"VX-{id:N}"[..16],
            QrPayload = $"qr-{id:N}",
            Status = status,
            SupplierId = supplierId,
            CostPerLiter = costPerLiter,
            ImportJobId = importId,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };

    private static async Task ResetDataAsync(ApplicationDbContext context)
        => await context.Database.ExecuteSqlRawAsync(
            """TRUNCATE TABLE "voucher_exchanges", "movement_batches", "payments", "fulfillments", "orders", "order_line_items", "outbox_events", "fuel_vouchers", "voucher_imports", "suppliers", "users", "provider_event_outbox", "app_settings" RESTART IDENTITY CASCADE""");

    private ApplicationDbContext CreateContext(IInterceptor? interceptor = null)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(_fixture.ConnectionString);

        if (interceptor != null)
            options.AddInterceptors(interceptor);

        return new ApplicationDbContext(options.Options, TestQrCipher.Instance);
    }
}