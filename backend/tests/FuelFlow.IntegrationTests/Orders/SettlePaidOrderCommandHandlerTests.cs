using FluentAssertions;
using FuelFlow.Features.Ledger.SharedModels;
using FuelFlow.Features.Orders.SettlePaidOrder;
using FuelFlow.Features.Orders.SharedModels;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using FuelFlow.Testing;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static FuelFlow.SharedKernel.Domain.FuelIdMapper;

namespace FuelFlow.IntegrationTests.Orders;

/// <summary>
/// The settle core on its own, against a real Postgres.
/// </summary>
/// <remarks>
/// The webhook suite covers this code end to end and does not assert it directly, which was fine
/// while it was one method inside that handler. It is now the shared money step that the
/// settlement channel is about to be handed to as well (planning #867), so what it owes is a test
/// that names the core: the four writes it makes, and the two that must not be duplicated when it
/// runs twice.
/// <para>
/// Integration rather than unit for the same reason the webhook tests moved: the journal takes its
/// entry number with <c>INSERT ... ON CONFLICT DO UPDATE ... RETURNING</c> over
/// <c>gen_random_uuid()</c>, and the in-memory provider runs neither.
/// </para>
/// </remarks>
[Collection("Integration Tests")]
public sealed class SettlePaidOrderCommandHandlerTests : IClassFixture<TestDatabaseFixture>, IAsyncLifetime
{
    private readonly ApplicationDbContext _context;
    private readonly SettlePaidOrderCommandHandler _handler;

    public SettlePaidOrderCommandHandlerTests(TestDatabaseFixture fixture)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(fixture.ConnectionString)
            .Options;

        _context = new ApplicationDbContext(options, TestQrCipher.Instance);
        _handler = TestJournal.SettleFor(_context);

        TestJournal.SeedChart(_context);
    }

    public async Task InitializeAsync() => await PaymentTestReset.RunAsync(_context);

    public Task DisposeAsync()
    {
        _context.Dispose();
        return Task.CompletedTask;
    }

    private static int _userSeq;

    private async Task<Order> SeedOrderAsync(
        decimal price = 1200m,
        OrderKind kind = OrderKind.Purchase,
        bool deleted = false)
    {
        var user = new User
        {
            Id = Guid.NewGuid(),
            PhoneNumber = $"+38050{Interlocked.Increment(ref _userSeq):D7}",
            CreatedAtUtc = TestClock.UtcNow,
            UpdatedAtUtc = TestClock.UtcNow,
            SettlementCode = string.Empty,
        };

        var order = new Order
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            Price = price,
            Kind = kind,
            Status = OrderStatus.PendingPayment,
            IsDeleted = deleted,
            CreatedAtUtc = TestClock.UtcNow,
            UpdatedAtUtc = TestClock.UtcNow,
            LineItems =
            {
                new OrderLineItem
                {
                    Id = Guid.NewGuid(),
                    BrandId = BrandIds.ForSlug("okko"),
                    Brand = "okko",
                    FuelTypeId = ToUuid("okko-95"),
                    Liters = 50,
                    Quantity = 1,
                    UnitPrice = price,
                    LineTotal = price,
                }
            }
        };

        _context.Users.Add(user);
        _context.Orders.Add(order);
        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();

        // The caller loads the order with its lines, which is the contract the command states.
        return await _context.Orders
            .IgnoreQueryFilters()
            .Include(o => o.LineItems)
            .SingleAsync(o => o.Id == order.Id);
    }

    [Fact]
    public async Task Settling_moves_the_order_queues_fulfilment_and_books_the_money()
    {
        var order = await SeedOrderAsync();

        await _handler.HandleAsync(new SettlePaidOrderCommand { Order = order });

        var settled = await _context.Orders.AsNoTracking().SingleAsync(o => o.Id == order.Id);
        settled.Status.Should().Be(OrderStatus.PendingFulfillment);

        // The same containment probe the core uses: a substring test here asks Postgres for
        // `jsonb ~~ jsonb`, the operator that does not exist and that the code's own comments exist
        // because of.
        var orderProbe = System.Text.Json.JsonSerializer.Serialize(new { orderId = order.Id });
        (await _context.OutboxEvents.AsNoTracking()
            .CountAsync(e => e.EventType == OutboxEventType.OrderCreated
                         && EF.Functions.JsonContains(e.Payload, orderProbe)))
            .Should().Be(1, "the order is handed to fulfilment by the same save that moved it");

        (await _context.JournalEntries.AsNoTracking()
            .Where(j => j.SourceId == order.Id)
            .Select(j => j.SourceEvent)
            .ToListAsync())
            .Should().BeEquivalentTo(["payment.received", "payment.received.vat"]);

        // Gross in the bank, VAT inside it owed to the state: 1200.00 at 20% is 1000.00 and 200.00.
        (await AmountOf("payment.received", order.Id)).Should().Be(1200m);
        (await AmountOf("payment.received.vat", order.Id)).Should().Be(200m);

        // The status change went through EF, so the audit row the context writes on a status move
        // describes it. A settle that hand-wrote the column would book the money and leave the
        // order's history silent about who moved it - the seed's own 'Created' row says nothing
        // about the payment.
        var audit = await _context.OrderEvents.AsNoTracking()
            .Where(e => e.OrderId == order.Id && e.EventType == "StatusChanged")
            .SingleAsync();
        audit.FromStatus.Should().Be(nameof(OrderStatus.PendingPayment));
        audit.ToStatus.Should().Be(nameof(OrderStatus.PendingFulfillment));
    }

    [Fact]
    public async Task A_hidden_order_that_was_paid_comes_back_into_view()
    {
        // A customer can swipe-delete an unpaid checkout while its invoice stays payable. The money
        // then arrives for an order nobody can see, and the only cure is that the settle restores it.
        var order = await SeedOrderAsync(deleted: true);

        await _handler.HandleAsync(new SettlePaidOrderCommand { Order = order });

        var settled = await _context.Orders.IgnoreQueryFilters().AsNoTracking()
            .SingleAsync(o => o.Id == order.Id);
        settled.IsDeleted.Should().BeFalse();
        settled.Status.Should().Be(OrderStatus.PendingFulfillment, "visible and unpaid-for is no better than hidden");
    }

    [Fact]
    public async Task A_renewal_order_is_not_handed_to_the_buy_fuel_path()
    {
        // A renewal's lines name the vouchers they extend. An ORDER_CREATED event would drive the
        // buy-fuel handler into minting fresh stock vouchers on top of them, so the core writes no
        // event for one - and still books the money, because a renewal is a sale.
        var order = await SeedOrderAsync(kind: OrderKind.Renewal);

        await _handler.HandleAsync(new SettlePaidOrderCommand { Order = order });

        (await _context.OutboxEvents.CountAsync()).Should().Be(0,
            "a renewal is fulfilled by the job that reads the order, not by the event that mints stock");

        (await _context.JournalEntries.AsNoTracking()
            .Where(j => j.SourceId == order.Id)
            .Select(j => j.SourceEvent)
            .ToListAsync())
            .Should().BeEquivalentTo(["payment.received", "payment.received.vat"]);
    }

    [Fact]
    public async Task Settling_twice_books_the_money_once()
    {
        // The case the unique index exists for: the webhook and the reconciliation poll both report
        // the same payment, and the second one must find the work already done. Idempotency here is
        // a property of the keys - the outbox probe on payload->orderId and
        // (source_event, source_type, source_id) - not of a flag this handler could forget to set.
        var order = await SeedOrderAsync();

        await _handler.HandleAsync(new SettlePaidOrderCommand { Order = order });
        await _handler.HandleAsync(new SettlePaidOrderCommand { Order = order });

        (await _context.OutboxEvents.CountAsync(e => e.EventType == OutboxEventType.OrderCreated))
            .Should().Be(1, "the second settle finds the handover already queued");

        (await _context.JournalEntries.CountAsync(j => j.SourceId == order.Id))
            .Should().Be(2, "one receipt and one VAT leg, not two of each");
    }

    /// <summary>
    /// What the entry is worth: one side of it, because a posted entry is balanced and reading both
    /// sides would double the figure the row is meant to show.
    /// </summary>
    private async Task<decimal> AmountOf(string sourceEvent, Guid orderId)
    {
        var entry = await _context.JournalEntries.AsNoTracking()
            .Include(j => j.Lines)
            .SingleAsync(j => j.SourceEvent == sourceEvent && j.SourceId == orderId);

        return Math.Max(entry.Lines.Sum(l => l.DebitUah), entry.Lines.Sum(l => l.CreditUah));
    }
}
