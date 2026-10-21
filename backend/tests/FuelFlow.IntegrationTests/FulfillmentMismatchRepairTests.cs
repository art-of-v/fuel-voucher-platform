using FluentAssertions;
using FuelFlow.API.BackgroundJobs;
using FuelFlow.API.Features.Orders.RefundOrder;
using FuelFlow.API.Features.Orders.SharedServices.Monobank;
using FuelFlow.Features.Orders.SharedModels;
using FuelFlow.Features.Brands;
using FuelFlow.Features.Settings;
using FuelFlow.Features.Settings.SharedModels;
using FuelFlow.Features.Vouchers;
using FuelFlow.Features.Vouchers.SharedModels;
using FuelFlow.Features.Vouchers.Events;
using FuelFlow.Features.Vouchers.Movements;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using FuelFlow.SharedKernel.Observability;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;
using static FuelFlow.SharedKernel.Domain.FuelIdMapper;

namespace FuelFlow.IntegrationTests;

/// <summary>
/// #246: the concurrency token landed on <c>orders</c> and <c>fuel_vouchers</c>, and this suite says
/// what the repair pass in <see cref="FulfillmentService"/> still does correctly once it is there.
/// </summary>
/// <remarks>
/// <para>
/// What this pins: the repair still releases the excess voucher, keeps the delivery record that did
/// happen, and puts the fuel back in stock — end to end, through the real service, against a real
/// Postgres.
/// </para>
/// <para>
/// What it cannot pin: that the repair's write to the <i>order row</i> happened. The backfill sweep
/// that runs immediately afterwards in the same call re-marks the order Fulfilled, so the end state is
/// identical whether the repair's write lands or is silently dropped. That half of the change is
/// guarded by <see cref="ConcurrencyTokenTrackingRuleIntegrationTests"/>, which states the rule the
/// tracked re-read exists to satisfy — deterministically, without needing a second order.
/// </para>
/// <para>
/// <b>This fixture deliberately does not set <c>NoTracking</c>.</b> Every other fulfillment
/// integration test builds its context with <c>UseQueryTrackingBehavior(NoTracking)</c> while
/// production runs the <c>TrackAll</c> that <c>DatabaseSetup</c> chose on purpose and
/// <c>DatabaseTrackingDefaultsTests</c> pins. That override is why the original exclusion looked
/// safe — and why this class of defect is invisible to the rest of the suite. A test for a
/// change-tracker problem that disables the change tracker is not a test of it.
/// </para>
/// </remarks>
[Collection("Integration Tests")]
public sealed class FulfillmentMismatchRepairTests : IClassFixture<TestDatabaseFixture>
{
    private readonly TestDatabaseFixture _fixture;

    public FulfillmentMismatchRepairTests(TestDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Repairing_A_Mismatch_Updates_The_Order_Without_Inventing_A_Conflict()
    {
        var userId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        const string brand = "okko";
        const string fuelTypeId = "okko-95";
        const decimal liters = 50m;

        var voucherA = Guid.NewGuid();
        var voucherB = Guid.NewGuid();

        using (var seed = CreateContext())
        {
            await seed.Database.MigrateAsync();
            await ResetDataAsync(seed);

            seed.Users.Add(new User
            {
                Id = userId,
                PhoneNumber = $"+38{userId:N}"[..20],
                IsActive = true,
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow
            });

            // One line item for one voucher, two delivery records: the quantity mismatch the repair
            // exists to correct. Created NOW - the repair deliberately leaves an OLDER order's
            // mismatch alone, because stripping fuel from an order a customer already holds is worse
            // than leaving a count wrong, so a day-old seed would test nothing at all.
            var order = Order(orderId, userId, quantity: 1, unitPrice: 2500);
            order.Status = OrderStatus.Fulfilled;
            order.FulfilledAtUtc = DateTime.UtcNow;
            seed.Orders.Add(order);

            seed.FuelVouchers.Add(Assigned(voucherA, brand, fuelTypeId, liters, "MR-A", userId, orderId));
            seed.FuelVouchers.Add(Assigned(voucherB, brand, fuelTypeId, liters, "MR-B", userId, orderId));

            seed.Fulfillments.Add(new Fulfillment
            {
                OrderId = orderId,
                VoucherId = voucherA,
                FulfilledAtUtc = DateTime.UtcNow.AddMinutes(-1)
            });
            seed.Fulfillments.Add(new Fulfillment
            {
                OrderId = orderId,
                VoucherId = voucherB,
                FulfilledAtUtc = DateTime.UtcNow
            });

            await seed.SaveChangesAsync();
        }

        using var serviceContext = CreateContext();
        var service = CreateService(serviceContext);
        await service.ProcessPendingOrdersAsync();

        using var verify = CreateContext();

        // IgnoreQueryFilters on purpose: Fulfillment carries a global `ReleasedAtUtc == null` filter,
        // so a released delivery record is invisible to an ordinary query. Asking "was anything
        // released" through that filter always answers no - which is exactly the mistake this suite's
        // first draft made, and the reason the assertion below reads the way it does.
        var released = await verify.Fulfillments
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(f => f.OrderId == orderId && f.ReleasedAtUtc != null)
            .ToListAsync();

        released.Should().ContainSingle(
            "the order asked for one voucher and had two delivered - one delivery record is a fact that did not happen");
        released[0].ReleaseReason.Should().Be("quantity_mismatch");

        var stillStanding = await verify.Fulfillments
            .AsNoTracking()
            .Where(f => f.OrderId == orderId)
            .ToListAsync();
        stillStanding.Should().ContainSingle("the delivery that did happen is still on the order");

        var backInStock = await verify.FuelVouchers
            .AsNoTracking()
            .Where(v => (v.Id == voucherA || v.Id == voucherB) && v.Status == VoucherStatus.Available)
            .ToListAsync();

        backInStock.Should().ContainSingle("the released voucher's fuel goes back to stock, with no order claiming it");
        backInStock[0].OrderId.Should().BeNull();
        backInStock[0].AssignedToUserId.Should().BeNull();

        // D-13. The repair is CUSTODY as well as state - `OrderId = null` above is the tell, because
        // a voucher back in the warehouse belongs to no order. So it owes a `returned` movement as well
        // as the status change, and writing only one would lose half of what happened.
        //
        // `system`, because a reconciliation found this and nobody pressed anything. The customer is
        // the subject of the movement, not its author.
        var movement = await verify.VoucherMovements.AsNoTracking()
            .SingleOrDefaultAsync(m => m.VoucherId == backInStock[0].Id);
        movement.Should().NotBeNull("the paper physically went back to the warehouse");
        movement!.Kind.Should().Be(VoucherMovementKinds.Returned);
        movement.FromLocation.Should().Be(VoucherLocations.Customer);
        movement.ToLocation.Should().Be(VoucherLocations.Pool);
        movement.OrderId.Should().Be(orderId, "the order is the reason the fuel came back");
        movement.ActorKind.Should().Be(VoucherActorKinds.System);
        movement.ActorUserId.Should().BeNull("a scheduler has nobody to name");

        var repairEvent = await verify.VoucherEvents.AsNoTracking()
            .SingleOrDefaultAsync(e => e.VoucherId == backInStock[0].Id);
        repairEvent.Should().NotBeNull();
        repairEvent!.EventType.Should().Be(VoucherEventTypes.StatusChanged);
        repairEvent.ToStatus.Should().Be(nameof(VoucherStatus.Available));
        repairEvent.FromStatus.Should().NotBe(repairEvent.ToStatus,
            "ck_voucher_events_status_changed refuses from == to, so a genuine transition never records one");
        repairEvent.ExternalRef.Should().Be(orderId.ToString(), "the order explains why the fuel came back");
        repairEvent.ActorKind.Should().Be(VoucherActorKinds.System);

        // And the voucher that was NOT released records nothing: the repair is per-voucher, not a
        // statement about the order.
        (await verify.VoucherMovements.CountAsync(m => m.VoucherId == voucherA || m.VoucherId == voucherB))
            .Should().Be(1, "only the released voucher's paper moved");

        var repaired = await verify.Orders.AsNoTracking().FirstAsync(o => o.Id == orderId);
        // Fulfilled, not PartiallyFulfilled: the repair resets the order to "one delivered, one short",
        // and the backfill sweep that runs immediately afterwards in the same call sees the order needs
        // exactly one voucher and has exactly one standing - so it marks it Fulfilled. Asserting the
        // intermediate state would pin an implementation detail another pass immediately undoes; what
        // matters is that the order ends consistent with the deliveries it actually has.
        repaired.Status.Should().Be(OrderStatus.Fulfilled);
        repaired.UpdatedAtUtc.Should().BeAfter(repaired.CreatedAtUtc.AddSeconds(-1),
            "the order row was written by the repair, not left untouched");
    }

    /// <summary>
    /// What the above cannot tell you: which of the two changes in <see cref="FulfillmentService"/>
    /// is load-bearing. The tracked re-read of the order and the tracker Clear before it overlap -
    /// either alone leaves the other green. The token is what makes the difference visible, which is
    /// why this suite exists rather than a comment in the handler.
    /// </summary>
    [Fact]
    public async Task The_Order_Is_Not_Left_Held_By_A_Stale_Instance_When_The_Run_Over()
    {
        var userId = Guid.NewGuid();
        var orderId = Guid.NewGuid();

        using (var seed = CreateContext())
        {
            await seed.Database.MigrateAsync();
            await ResetDataAsync(seed);

            seed.Users.Add(new User
            {
                Id = userId,
                PhoneNumber = $"+38{userId:N}"[..20],
                IsActive = true,
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow
            });

            var order = Order(orderId, userId, quantity: 1, unitPrice: 2500);
            order.Status = OrderStatus.Fulfilled;
            order.FulfilledAtUtc = DateTime.UtcNow;
            seed.Orders.Add(order);
            await seed.SaveChangesAsync();
        }

        using var serviceContext = CreateContext();
        var service = CreateService(serviceContext);
        await service.ProcessPendingOrdersAsync();

        // Every order the pass touched must have been flushed, not left pending in the tracker for
        // the next pass to pick up: the repair is followed by two more sweeps in the same scope, and
        // an un-flushed Modified entity is a write that happens at an unpredictable later moment.
        serviceContext.ChangeTracker.Entries<Order>().Should().OnlyContain(e => e.State == EntityState.Unchanged,
            "nothing from the repair is left waiting in the tracker");
        serviceContext.ChangeTracker.Entries<FuelVoucher>().Should().OnlyContain(e => e.State == EntityState.Unchanged,
            "the decision queries must not leave voucher rows in the tracker for a later save to pick up");
    }

    private static Order Order(Guid id, Guid userId, int quantity, decimal unitPrice) => new()
    {
        Id = id,
        UserId = userId,
        Price = quantity * unitPrice,
        Status = OrderStatus.PendingFulfillment,
        CreatedAtUtc = DateTime.UtcNow,
        UpdatedAtUtc = DateTime.UtcNow,
        LineItems = new List<OrderLineItem>
        {
            new OrderLineItem
            {
                Id = Guid.NewGuid(),
                OrderId = id,
                BrandId = BrandIds.ForSlug("okko"),
                Brand = "okko",
                FuelTypeId = ToUuid("okko-95"),
                Liters = 50m,
                Quantity = quantity,
                UnitPrice = unitPrice,
                LineTotal = quantity * unitPrice
            }
        }
    };

    private static FuelVoucher Assigned(
        Guid id, string brand, string fuelTypeId, decimal liters, string tag, Guid userId, Guid orderId) => new()
    {
        Id = id,
        BrandId = BrandIds.ForSlug(brand),
        Brand = brand,
        FuelTypeId =   ToUuid(fuelTypeId),
        Liters = liters,
        SupplierExpirationDate = DateOnly.FromDateTime(DateTime.UtcNow).AddMonths(1),
        CustomerExpirationDate = DateOnly.FromDateTime(DateTime.UtcNow).AddMonths(1),
        VoucherNumber = $"{tag}-{id:N}",
        QrPayload = $"{tag}-{id:N}",
        Status = VoucherStatus.Assigned,
        AssignedToUserId = userId,
        OrderId = orderId,
        CreatedAtUtc = DateTime.UtcNow,
        UpdatedAtUtc = DateTime.UtcNow
    };

    private static async Task ResetDataAsync(ApplicationDbContext context)
        => await context.Database.ExecuteSqlRawAsync(
            """TRUNCATE TABLE "payments", "fulfillments", "orders", "order_line_items", "outbox_events", "fuel_vouchers", "users", "provider_event_outbox", "app_settings" RESTART IDENTITY CASCADE""");

    /// <summary>
    /// The production default: no <c>UseQueryTrackingBehavior</c> call at all. See the type-level
    /// remark for why that omission is the point of this fixture.
    /// </summary>
    private ApplicationDbContext CreateContext()
        => new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(_fixture.ConnectionString)
            .Options, TestQrCipher.Instance);

    private FulfillmentService CreateService(ApplicationDbContext context)
    {
        var monobank = new Mock<IMonobankClient>().Object;
        var refundHandler = new RefundOrderCommandHandler(
            context,
            new StubMonobankClientFactory(monobank),
            new AuditEventService(context));

        return new FulfillmentService(
            context,
            NullLogger<FulfillmentService>.Instance,
            refundHandler,
            new RuntimeSettingsService(context),
            NotificationDispatcher.Disabled,
            new ConfigurationBuilder().Build());
    }
}
