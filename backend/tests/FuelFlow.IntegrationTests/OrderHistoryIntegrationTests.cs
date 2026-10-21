using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using FuelFlow.Features.Orders.SharedModels;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static FuelFlow.SharedKernel.Domain.FuelIdMapper;

namespace FuelFlow.IntegrationTests;

/// <summary>
/// <c>order_events</c> is written from <see cref="ApplicationDbContext.SaveChangesAsync"/>, not from
/// the handlers. That is the only way the record stays complete - there are eight places across six
/// classes that move an order's status, and a recorder that has to be remembered at each one is the
/// same failure this table exists to remove. So these tests drive the context directly rather than
/// through the endpoints.
/// </summary>
[Collection("Integration Tests")]
public sealed class OrderHistoryIntegrationTests : IClassFixture<TestDatabaseFixture>
{
    private readonly TestDatabaseFixture _fixture;

    public OrderHistoryIntegrationTests(TestDatabaseFixture fixture) => _fixture = fixture;

    private ApplicationDbContext CreateContext()
        => new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(_fixture.ConnectionString)
            .Options, TestQrCipher.Instance);

    [Fact]
    public async Task CreatingAnOrder_RecordsA_Created_Event()
    {
        var userId = await SeedUserAsync();

        var orderId = await WithFreshContextAsync(async context =>
        {
            var order = NewOrder(userId);
            context.Orders.Add(order);
            context.OrderLineItems.Add(new OrderLineItem
            {
                Id = Guid.CreateVersion7(),
                OrderId = order.Id,
                BrandId = BrandIds.ForSlug("okko"),
                Brand = "okko",
                FuelTypeId = ToUuid("okko-95"),
                Liters = 10m,
                Quantity = 1,
                UnitPrice = 890.60m,
                LineTotal = 890.60m
            });
            await context.SaveChangesAsync();
            return order.Id;
        });

        var events = await EventsForAsync(orderId);
        events.Should().ContainSingle("an order's first row is its own history");
        events[0].EventType.Should().Be("Created");
        events[0].FromStatus.Should().BeNull("a creation has no previous state");
        events[0].ToStatus.Should().Be(OrderStatus.PendingPayment.ToString());
    }

    /// <summary>
    /// The point of the whole table. A webhook delivered twice, or out of order, leaves the same
    /// final row either way - so before this, a duplicate delivery and a clean run were
    /// indistinguishable after the fact, and there was no per-state timestamp to assert that the
    /// invoice existed before the payment was captured.
    /// </summary>
    [Fact]
    public async Task EveryStatusChange_LeavesItsOwnEvent_InOrder()
    {
        var userId = await SeedUserAsync();

        var orderId = await WithFreshContextAsync(async context =>
        {
            var order = NewOrder(userId);
            context.Orders.Add(order);
            await context.SaveChangesAsync();
            return order.Id;
        });

        foreach (var status in new[]
        {
            OrderStatus.PendingFulfillment,
            OrderStatus.PartiallyFulfilled,
            OrderStatus.Fulfilled,
        })
        {
            var toSet = status;
            await WithFreshContextAsync(async context =>
            {
                var order = await context.Orders.FirstAsync(o => o.Id == orderId);
                order.Status = toSet;
                await context.SaveChangesAsync();
            });
        }

        var events = await EventsForAsync(orderId);

        events.Select(e => e.EventType).Should().Equal(
            new[] { "Created", "StatusChanged", "StatusChanged", "StatusChanged" },
            "a table that only remembers the current row records nothing");

        // The Created event carries the status the order was born in too, so there are four
        // ToStatus values across four events.
        events.Select(e => e.ToStatus).Should().Equal(
            new[]
            {
                OrderStatus.PendingPayment.ToString(),
                OrderStatus.PendingFulfillment.ToString(),
                OrderStatus.PartiallyFulfilled.ToString(),
                OrderStatus.Fulfilled.ToString(),
            });

        events.Select(e => e.OccurredAtUtc).Should().BeInAscendingOrder();
    }

    /// <summary>
    /// A write that does not move the order must not invent a transition. Half the handlers touch an
    /// order on every call - updating a webhook timestamp, refreshing a total - and a history that
    /// filled up with those would be as useless as no history at all.
    /// </summary>
    [Fact]
    public async Task AWriteThatDoesNotMoveTheOrder_RecordsNoEvent()
    {
        var userId = await SeedUserAsync();

        var orderId = await WithFreshContextAsync(async context =>
        {
            var order = NewOrder(userId);
            context.Orders.Add(order);
            await context.SaveChangesAsync();
            return order.Id;
        });

        await WithFreshContextAsync(async context =>
        {
            var order = await context.Orders.FirstAsync(o => o.Id == orderId);
            order.LastWebhookProcessedAtUtc = DateTime.UtcNow;
            order.UpdatedAtUtc = DateTime.UtcNow;
            await context.SaveChangesAsync();
        });

        (await EventsForAsync(orderId)).Should().ContainSingle(
            "a webhook timestamp is not a state transition");
    }

    /// <summary>
    /// Two writes in one save. Both transitions have to land - a batch that moves an order through
    /// two states is still two events.
    /// </summary>
    [Fact]
    public async Task TwoStatusChangesInOneSave_RecordTwoEvents()
    {
        var userId = await SeedUserAsync();

        var orderId = await WithFreshContextAsync(async context =>
        {
            var order = NewOrder(userId);
            context.Orders.Add(order);
            await context.SaveChangesAsync();
            return order.Id;
        });

        await WithFreshContextAsync(async context =>
        {
            var order = await context.Orders.FirstAsync(o => o.Id == orderId);
            order.Status = OrderStatus.PendingFulfillment;
            order.MonobankStatus = MonobankStatus.Success;
            await context.SaveChangesAsync();
        });

        var events = await EventsForAsync(orderId);
        events.Should().HaveCount(2, "Created plus one StatusChanged");
        events[1].EventType.Should().Be("StatusChanged");
        events[1].FromStatus.Should().Be(OrderStatus.PendingPayment.ToString());
        events[1].ToStatus.Should().Be(OrderStatus.PendingFulfillment.ToString());
    }

    /// <summary>
    /// The transition carries the invoice id, because that is what a duplicate-delivery
    /// investigation searches on - two deliveries for the same invoice id have to be findable
    /// without joining the order.
    /// </summary>
    [Fact]
    public async Task ATransition_RecordsTheExternalReferenceItCameFrom()
    {
        var userId = await SeedUserAsync();

        var orderId = await WithFreshContextAsync(async context =>
        {
            var order = NewOrder(userId);
            order.MonobankInvoiceId = "INV-ORDER-HISTORY-1";
            context.Orders.Add(order);
            await context.SaveChangesAsync();
            return order.Id;
        });

        await WithFreshContextAsync(async context =>
        {
            var order = await context.Orders.FirstAsync(o => o.Id == orderId);
            order.Status = OrderStatus.PendingFulfillment;
            await context.SaveChangesAsync();
        });

        var events = await EventsForAsync(orderId);
        events.Should().Contain(e => e.ExternalRef == "INV-ORDER-HISTORY-1");
    }

    /// <summary>
    /// The database refuses an event type nothing can act on. A typo would otherwise be stored
    /// happily and read back later as an unrecognised transition - the same reasoning as
    /// <c>ck_order_line_positive_liters</c> and the other 300 constraints.
    /// </summary>
    [Fact]
    public async Task AnUnknownEventType_IsRefusedByTheDatabase()
    {
        var userId = await SeedUserAsync();

        var orderId = await WithFreshContextAsync(async context =>
        {
            var order = NewOrder(userId);
            context.Orders.Add(order);
            await context.SaveChangesAsync();
            return order.Id;
        });

        await using var context = CreateContext();
        context.OrderEvents.Add(new OrderEvent
        {
            Id = Guid.CreateVersion7(),
            OrderId = orderId,
            EventType = "PaymntCaptured",   // the typo this constraint exists for
            ToStatus = OrderStatus.Fulfilled.ToString(),
            OccurredAtUtc = DateTime.UtcNow
        });

        var act = async () => await context.SaveChangesAsync();
        await act.Should().ThrowAsync<DbUpdateException>();
    }

    /// <summary>
    /// An event with an order id that does not exist cannot be stored, so a history row can never
    /// be orphaned from the thing it describes.
    /// </summary>
    [Fact]
    public async Task AnEvent_ForAnOrderThatDoesNotExist_IsRefusedByTheDatabase()
    {
        await using var context = CreateContext();
        context.OrderEvents.Add(new OrderEvent
        {
            Id = Guid.CreateVersion7(),
            OrderId = Guid.CreateVersion7(),
            EventType = "Created",
            OccurredAtUtc = DateTime.UtcNow
        });

        var act = async () => await context.SaveChangesAsync();
        await act.Should().ThrowAsync<DbUpdateException>();
    }

    /// <summary>
    /// <c>pricing_basis</c> records the inputs that produced the price, so a line can explain its
    /// own number later. It is written by the checkout handler; the column's contract here is that
    /// it accepts a document and leaves it alone.
    /// </summary>
    [Fact]
    public async Task PricingBasis_StoresTheInputsThatProducedThePrice()
    {
        var userId = await SeedUserAsync();

        var lineId = await WithFreshContextAsync(async context =>
        {
            var order = NewOrder(userId);
            context.Orders.Add(order);
            context.OrderLineItems.Add(new OrderLineItem
            {
                Id = Guid.CreateVersion7(),
                OrderId = order.Id,
                BrandId = BrandIds.ForSlug("okko"),
                Brand = "okko",
                FuelTypeId = ToUuid("okko-95"),
                Liters = 10m,
                Quantity = 1,
                UnitPrice = 890.60m,
                LineTotal = 890.60m,
                PricingBasis = """{"supplierPricePerLiter":60.00,"marginUahPerLiter":12.34,"pumpPricePerLiter":92.90,"minDiscountPerLiter":4.00,"termsId":null}"""
            });
            await context.SaveChangesAsync();
            return context.OrderLineItems.Local.Single().Id;
        });

        await using var verify = CreateContext();
        var line = await verify.OrderLineItems.FirstAsync(l => l.Id == lineId);
        line.PricingBasis.Should().NotBeNull();
        line.PricingBasis.Should().Contain("pumpPricePerLiter");
    }

    private async Task<List<OrderEvent>> EventsForAsync(Guid orderId)
    {
        await using var context = CreateContext();
        return await context.OrderEvents
            .Where(e => e.OrderId == orderId)
            .OrderBy(e => e.OccurredAtUtc)
            .ToListAsync();
    }

    private static Order NewOrder(Guid userId) => new()
    {
        Id = Guid.CreateVersion7(),
        UserId = userId,
        Price = 890.60m,
        Status = OrderStatus.PendingPayment,
        Kind = OrderKind.Purchase,
        CreatedAtUtc = DateTime.UtcNow,
        UpdatedAtUtc = DateTime.UtcNow
    };

    private async Task<Guid> SeedUserAsync()
    {
        var id = Guid.CreateVersion7();
        await using var context = CreateContext();
        context.Users.Add(new User
        {
            Id = id,
            PhoneNumber = $"+38050{DateTime.UtcNow.Ticks % 10_000_000:0000000}",
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        });
        await context.SaveChangesAsync();
        return id;
    }

    private async Task<T> WithFreshContextAsync<T>(Func<ApplicationDbContext, Task<T>> action)
    {
        await using var context = CreateContext();
        return await action(context);
    }

    private async Task WithFreshContextAsync(Func<ApplicationDbContext, Task> action)
    {
        await using var context = CreateContext();
        await action(context);
    }
}