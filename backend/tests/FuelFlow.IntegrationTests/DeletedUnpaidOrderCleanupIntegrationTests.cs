using FluentAssertions;
using FuelFlow.API.BackgroundJobs;
using FuelFlow.Features.Orders.SharedModels;
using FuelFlow.Features.Vouchers;
using FuelFlow.Features.Vouchers.SharedModels;
using FuelFlow.Features.Settings;
using FuelFlow.Features.Settings.SharedModels;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FuelFlow.IntegrationTests;

/// <summary>
/// Covers the abandoned-order garbage collector (DeletedUnpaidOrderCleanupService) against a real
/// PostgreSQL container: it hard-deletes only soft-deleted + Cancelled orders aged past the
/// configurable retention window, cascades to their line items, honours the runtime retention
/// value, and does nothing at all while the runtime switch is off.
/// </summary>
[Collection("Integration Tests")]
public sealed class DeletedUnpaidOrderCleanupIntegrationTests : IClassFixture<TestDatabaseFixture>
{
    private readonly TestDatabaseFixture _fixture;

    public DeletedUnpaidOrderCleanupIntegrationTests(TestDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Cleanup_WhenEnabled_PurgesOnlyAgedCancelledSoftDeletedOrders()
    {
        var userId = Guid.NewGuid();
        var eligibleId = Guid.NewGuid();
        var recentId = Guid.NewGuid();
        var notDeletedCancelledId = Guid.NewGuid();
        var pendingDeletedId = Guid.NewGuid();
        var fulfilledId = Guid.NewGuid();

        await SeedAsync(seed =>
        {
            seed.Users.Add(CreateUser(userId));
            EnableCleanup(seed, retentionDays: 30);

            // The one true candidate: soft-deleted, driven to Cancelled, aged past retention.
            seed.Orders.Add(CreateOrder(eligibleId, userId, OrderStatus.Cancelled, isDeleted: true, ageDays: 40));

            // Ineligible for a distinct reason each - all must survive:
            // touched recently (still inside the retention window)
            seed.Orders.Add(CreateOrder(recentId, userId, OrderStatus.Cancelled, isDeleted: true, ageDays: 5));
            // cancelled but never soft-deleted (a visible order, not garbage)
            seed.Orders.Add(CreateOrder(notDeletedCancelledId, userId, OrderStatus.Cancelled, isDeleted: false, ageDays: 60));
            // soft-deleted but not yet finalized by reconciliation (could still settle as paid)
            seed.Orders.Add(CreateOrder(pendingDeletedId, userId, OrderStatus.PendingPayment, isDeleted: true, ageDays: 60));
            // a real, fulfilled purchase
            seed.Orders.Add(CreateOrder(fulfilledId, userId, OrderStatus.Fulfilled, isDeleted: false, ageDays: 60));
        });

        await RunCleanupAsync();

        using var verify = CreateContext();
        var survivors = await verify.Orders.IgnoreQueryFilters().Select(o => o.Id).ToListAsync();
        survivors.Should().BeEquivalentTo(new[] { recentId, notDeletedCancelledId, pendingDeletedId, fulfilledId });
        survivors.Should().NotContain(eligibleId);

        // The purge cascaded to the eligible order's line items; every survivor keeps its own.
        var orphanLineItems = await verify.Set<OrderLineItem>().CountAsync(li => li.OrderId == eligibleId);
        orphanLineItems.Should().Be(0);
        (await verify.Set<OrderLineItem>().CountAsync()).Should().Be(4);
    }

    [Fact]
    public async Task Cleanup_WhenDisabled_DeletesNothing()
    {
        var userId = Guid.NewGuid();
        var eligibleId = Guid.NewGuid();

        await SeedAsync(seed =>
        {
            seed.Users.Add(CreateUser(userId));
            // DeletedUnpaidOrderCleanup:Enabled intentionally NOT set -> fail-safe default of false.
            seed.Orders.Add(CreateOrder(eligibleId, userId, OrderStatus.Cancelled, isDeleted: true, ageDays: 90));
        });

        await RunCleanupAsync();

        using var verify = CreateContext();
        var exists = await verify.Orders.IgnoreQueryFilters().AnyAsync(o => o.Id == eligibleId);
        exists.Should().BeTrue();
    }

    [Fact]
    public async Task Cleanup_HonoursConfiguredRetentionWindow()
    {
        var userId = Guid.NewGuid();
        var pastRetentionId = Guid.NewGuid();   // aged 10d, retention 7d -> purge
        var withinRetentionId = Guid.NewGuid(); // aged 3d, retention 7d -> keep

        await SeedAsync(seed =>
        {
            seed.Users.Add(CreateUser(userId));
            EnableCleanup(seed, retentionDays: 7);

            seed.Orders.Add(CreateOrder(pastRetentionId, userId, OrderStatus.Cancelled, isDeleted: true, ageDays: 10));
            seed.Orders.Add(CreateOrder(withinRetentionId, userId, OrderStatus.Cancelled, isDeleted: true, ageDays: 3));
        });

        await RunCleanupAsync();

        using var verify = CreateContext();
        var survivors = await verify.Orders.IgnoreQueryFilters().Select(o => o.Id).ToListAsync();
        survivors.Should().ContainSingle().Which.Should().Be(withinRetentionId);
    }

    [Fact]
    public async Task Cleanup_SkipsAnAgedCancelledOrderThatStillOwnsVouchers()
    {
        var userId = Guid.NewGuid();
        var deliversFuelId = Guid.NewGuid();
        var pureGarbageId = Guid.NewGuid();
        var voucherId = Guid.NewGuid();

        await SeedAsync(seed =>
        {
            seed.Users.Add(CreateUser(userId));
            EnableCleanup(seed, retentionDays: 30);

            // Aged, cancelled, soft-deleted - every other criterion for garbage - but it delivered
            // fuel. fuel_vouchers.order_id is ON DELETE RESTRICT, so removing this order is
            // impossible; letting the job pick it would abort the whole batch on the FK and the same
            // row would be re-selected every night, so the job could never drain.
            seed.Orders.Add(CreateOrder(deliversFuelId, userId, OrderStatus.Cancelled, isDeleted: true, ageDays: 40));

            // A genuine leftover in the same batch: must still be purged, so the guard above cannot
            // turn into "never delete anything".
            seed.Orders.Add(CreateOrder(pureGarbageId, userId, OrderStatus.Cancelled, isDeleted: true, ageDays: 40));

            seed.FuelVouchers.Add(new FuelVoucher
            {
                Id = voucherId,
                Provider = "OKKO",
                FuelTypeId = "okko-dp",
                Liters = 10m,
                ProviderExpirationDate = DateOnly.FromDateTime(DateTime.UtcNow).AddMonths(1),
                CustomerExpirationDate = DateOnly.FromDateTime(DateTime.UtcNow).AddMonths(1),
                VoucherNumber = $"OC-{voucherId:N}"[..16],
                QrPayload = $"qr-{voucherId:N}",
                Status = VoucherStatus.Assigned,
                AssignedToUserId = userId,
                OrderId = deliversFuelId,
                CreatedAtUtc = DateTime.UtcNow.AddDays(-40),
                UpdatedAtUtc = DateTime.UtcNow.AddDays(-40)
            });
        });

        await RunCleanupAsync();

        using var verify = CreateContext();
        var remaining = await verify.Orders.IgnoreQueryFilters().Select(o => o.Id).ToListAsync();

        remaining.Should().Contain(deliversFuelId);
        remaining.Should().NotContain(pureGarbageId);
    }

    private static void EnableCleanup(ApplicationDbContext seed, int retentionDays)
    {
        seed.AppSettings.Add(new AppSetting
        {
            Key = AppSettingKeys.DeletedUnpaidOrderCleanupEnabled,
            Value = "true",
            UpdatedAtUtc = DateTime.UtcNow
        });
        seed.AppSettings.Add(new AppSetting
        {
            Key = AppSettingKeys.DeletedUnpaidOrderCleanupRetentionDays,
            Value = retentionDays.ToString(),
            UpdatedAtUtc = DateTime.UtcNow
        });
    }

    private async Task RunCleanupAsync()
    {
        using var context = CreateContext();
        var settings = new RuntimeSettingsService(context);
        var service = new DeletedUnpaidOrderCleanupService(context, settings, NullLogger<DeletedUnpaidOrderCleanupService>.Instance);
        await service.CleanupAbandonedOrdersAsync();
    }

    private static User CreateUser(Guid userId) => new()
    {
        Id = userId,
        PhoneNumber = $"+38{userId:N}"[..20],
        IsActive = true,
        CreatedAtUtc = DateTime.UtcNow,
        UpdatedAtUtc = DateTime.UtcNow
    };

    private static Order CreateOrder(
        Guid orderId,
        Guid userId,
        OrderStatus status,
        bool isDeleted,
        int ageDays)
    {
        var timestamp = DateTime.UtcNow.AddDays(-ageDays);
        return new Order
        {
            Id = orderId,
            UserId = userId,
            Price = 2000,
            Status = status,
            IsDeleted = isDeleted,
            MonobankInvoiceId = $"test-invoice-{orderId:N}",
            MonobankStatus = MonobankStatus.Success,
            CreatedAtUtc = timestamp,
            UpdatedAtUtc = timestamp,
            LineItems = new List<OrderLineItem>
            {
                new()
                {
                    Id = Guid.NewGuid(),
                    OrderId = orderId,
                    Provider = "OKKO",
                    FuelTypeId = "okko-dp",
                    Liters = 10m,
                    Quantity = 2,
                    UnitPrice = 1000,
                    LineTotal = 2000
                }
            }
        };
    }

    private async Task SeedAsync(Action<ApplicationDbContext> seed)
    {
        using var context = CreateContext();
        await context.Database.MigrateAsync();

        // Keep each test hermetic in the shared class-fixture database.
        await context.Database.ExecuteSqlRawAsync(
            """TRUNCATE TABLE "refunds", "fulfillments", "orders", "order_line_items", "outbox_events", "fuel_vouchers", "users", "provider_event_outbox", "app_settings" RESTART IDENTITY CASCADE""");

        seed(context);
        await context.SaveChangesAsync();
    }

    private ApplicationDbContext CreateContext()
        => new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(_fixture.DbContainer.GetConnectionString())
            .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking)
            .Options);
}
