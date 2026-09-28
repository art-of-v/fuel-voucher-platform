using FluentAssertions;
using FuelFlow.API.BackgroundJobs;
using FuelFlow.Features.Orders.SharedModels;
using FuelFlow.Features.Settings;
using FuelFlow.Features.Settings.SharedModels;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FuelFlow.IntegrationTests;

/// <summary>
/// Covers the abandoned-order garbage collector (OrderCleanupService) against a real
/// PostgreSQL container: it hard-deletes only soft-deleted + Cancelled orders aged past the
/// configurable retention window, cascades to their line items, honours the runtime retention
/// value, and does nothing at all while the runtime switch is off.
/// </summary>
[Collection("Integration Tests")]
public sealed class OrderCleanupIntegrationTests : IClassFixture<TestDatabaseFixture>
{
    private readonly TestDatabaseFixture _fixture;

    public OrderCleanupIntegrationTests(TestDatabaseFixture fixture)
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
            // OrderCleanup:Enabled intentionally NOT set -> fail-safe default of false.
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

    private static void EnableCleanup(ApplicationDbContext seed, int retentionDays)
    {
        seed.AppSettings.Add(new AppSetting
        {
            Key = AppSettingKeys.OrderCleanupEnabled,
            Value = "true",
            UpdatedAtUtc = DateTime.UtcNow
        });
        seed.AppSettings.Add(new AppSetting
        {
            Key = AppSettingKeys.OrderCleanupRetentionDays,
            Value = retentionDays.ToString(),
            UpdatedAtUtc = DateTime.UtcNow
        });
    }

    private async Task RunCleanupAsync()
    {
        using var context = CreateContext();
        var settings = new RuntimeSettingsService(context);
        var service = new OrderCleanupService(context, settings, NullLogger<OrderCleanupService>.Instance);
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
