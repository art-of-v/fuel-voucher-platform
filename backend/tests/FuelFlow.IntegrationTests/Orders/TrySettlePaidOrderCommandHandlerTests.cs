using FluentAssertions;
using FuelFlow.Features.Orders.SettlePaidOrder;
using FuelFlow.Features.Orders.SharedModels;
using FuelFlow.Payments.SharedModels;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using FuelFlow.Testing;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static FuelFlow.SharedKernel.Domain.FuelIdMapper;

namespace FuelFlow.IntegrationTests.Orders;

/// <summary>
/// The money rule on its own: does what is attached to this order pay for it, and does the answer move
/// the order.
/// </summary>
/// <remarks>
/// <para>
/// The core's suite proves what a settle writes. This is the half that decides whether it may run
/// without an acquirer having said anything, which is the decision planning #867 hands to the settlement
/// channel and to the panel — and until now the decision existed only inside the webhook, so money
/// declared in the Telegram channel could be attached to an order and sit there unpaid until its
/// invoice expired and cancelled the order around it.
/// </para>
/// <para>
/// Each test here is a boundary the rule has to hold on its own, because every caller reaches it from a
/// different direction and none of them may re-derive it.
/// </para>
/// </remarks>
[Collection("Integration Tests")]
public sealed class TrySettlePaidOrderCommandHandlerTests : IClassFixture<TestDatabaseFixture>, IAsyncLifetime
{
    private readonly ApplicationDbContext _context;
    private readonly TrySettlePaidOrderCommandHandler _handler;

    public TrySettlePaidOrderCommandHandlerTests(TestDatabaseFixture fixture)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(fixture.ConnectionString)
            .Options;

        _context = new ApplicationDbContext(options, TestQrCipher.Instance);
        _handler = TestJournal.TrySettleFor(_context);

        TestJournal.SeedChart(_context);
    }

    public async Task InitializeAsync() => await PaymentTestReset.RunAsync(_context);

    public Task DisposeAsync()
    {
        _context.Dispose();
        return Task.CompletedTask;
    }

    private static int _userSeq;

    private async Task<(Guid OrderId, Guid UserId)> SeedOrderAsync(
        decimal price = 1200m,
        OrderStatus status = OrderStatus.PendingPayment,
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
            Status = status,
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

        return (order.Id, user.Id);
    }

    private async Task<Guid> SeedMoneyAsync(
        Guid orderId,
        Guid userId,
        decimal amount,
        PaymentStatus status = PaymentStatus.Succeeded,
        PaymentKind kind = PaymentKind.Capture,
        Guid? parentPaymentId = null)
    {
        var now = TestClock.UtcNow;

        var payment = new Payment
        {
            Id = Guid.CreateVersion7(),
            Kind = kind,
            // Declared: the channel's money, which is the case no authority but this rule can settle.
            Provider = PaymentProviders.Declared,
            UserId = userId,
            OrderId = orderId,
            AmountUah = amount,
            AmountKopecks = (long)Math.Round(amount * 100),
            Currency = "UAH",
            Status = status,
            // A refund is a refund of something, and the database refuses one without a parent.
            ParentPaymentId = parentPaymentId,
            PaidAtUtc = status == PaymentStatus.Succeeded ? now : null,
            CreatedByUserName = "finance",
            IsAutomatic = true,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };

        _context.Payments.Add(payment);

        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();

        return payment.Id;
    }

    private Task<TrySettlePaidOrderResult> TryAsync(Guid orderId) => _handler.HandleAsync(
        new TrySettlePaidOrderCommand { OrderId = orderId, Because = "a test" });

    [Fact]
    public async Task Money_that_covers_the_price_settles_the_order_completely()
    {
        var (orderId, userId) = await SeedOrderAsync();
        await SeedMoneyAsync(orderId, userId, 1200m);

        (await TryAsync(orderId)).Settled.Should().BeTrue();

        var settled = await _context.Orders.AsNoTracking().SingleAsync(o => o.Id == orderId);
        settled.Status.Should().Be(OrderStatus.PendingFulfillment);

        (await _context.OutboxEvents.AsNoTracking().CountAsync(e => e.EventType == OutboxEventType.OrderCreated))
            .Should().Be(1, "the order has to reach the job that claims stock for it");

        (await _context.JournalEntries.AsNoTracking()
                .Where(j => j.SourceId == orderId)
                .Select(j => j.SourceEvent)
                .ToListAsync())
            .Should().BeEquivalentTo(["payment.received", "payment.received.vat"]);
    }

    [Fact]
    public async Task Two_transfers_that_add_up_to_the_price_settle_it()
    {
        // A sum, not an equality: a customer can pay one order in two real transfers, and the second
        // one is the moment the sale exists. Matching the price on a single row would leave the order
        // unpaid forever, having been paid in full.
        var (orderId, userId) = await SeedOrderAsync(price: 1200m);
        await SeedMoneyAsync(orderId, userId, 600m);
        await SeedMoneyAsync(orderId, userId, 600m);

        (await TryAsync(orderId)).Settled.Should().BeTrue();

        (await _context.JournalEntries.AsNoTracking().CountAsync(j => j.SourceId == orderId))
            .Should().Be(2, "one receipt for the order, not one per transfer");
    }

    [Fact]
    public async Task More_money_than_the_price_still_settles_it()
    {
        // An over-payment is the customer's mistake and does not undo that they paid. The order owes
        // fuel either way; what the surplus is - a credit, a return - is an operator's decision about
        // the money, not a reason to withhold the goods.
        var (orderId, userId) = await SeedOrderAsync(price: 1200m);
        await SeedMoneyAsync(orderId, userId, 1500m);

        (await TryAsync(orderId)).Settled.Should().BeTrue();
    }

    [Fact]
    public async Task Money_short_of_the_price_declines_and_says_so_in_words()
    {
        var (orderId, userId) = await SeedOrderAsync(price: 1200m);
        await SeedMoneyAsync(orderId, userId, 1000m);

        var result = await TryAsync(orderId);

        result.Settled.Should().BeFalse();
        result.Refusal.Should().Be(SettleRefusal.MoneyIsShort);
        result.Reason.Should().Contain("1200.00");

        (await _context.Orders.AsNoTracking().SingleAsync(o => o.Id == orderId))
            .Status.Should().Be(OrderStatus.PendingPayment, "half a sale is not a paid order");
        (await _context.JournalEntries.AsNoTracking().CountAsync(j => j.SourceId == orderId))
            .Should().Be(0, "revenue the customer has not paid for is not revenue");
        (await _context.OutboxEvents.AsNoTracking().CountAsync())
            .Should().Be(0, "no stock may be claimed for it yet");
    }

    [Fact]
    public async Task An_invoice_that_was_never_paid_is_not_money()
    {
        // The row an acquirer checkout leaves behind. Its status is what says whether money moved -
        // reading the row's existence would settle every abandoned checkout in the country.
        var (orderId, userId) = await SeedOrderAsync();
        await SeedMoneyAsync(orderId, userId, 1200m, status: PaymentStatus.Pending);

        var answer = await TryAsync(orderId);
        answer.Settled.Should().BeFalse();
        answer.Refusal.Should().Be(SettleRefusal.MoneyIsShort,
            "an invoice that nobody paid is not money, so the order is short of it");
    }

    [Fact]
    public async Task A_return_is_not_the_capture_this_order_is_paid_with()
    {
        // A refund row naming the order says the platform gave money BACK. Counting it toward the price
        // would let a fully refunded order settle itself - so the two rows here add up to exactly the
        // price, and only the capture is money we hold.
        var (orderId, userId) = await SeedOrderAsync();
        var held = await SeedMoneyAsync(orderId, userId, 500m);
        await SeedMoneyAsync(orderId, userId, 700m, kind: PaymentKind.Refund, parentPaymentId: held);

        var result = await TryAsync(orderId);

        result.Settled.Should().BeFalse();
        result.Refusal.Should().Be(SettleRefusal.MoneyIsShort);
        result.Reason.Should().Contain("does not yet cover its 1200.00",
            "the refusal names what the order owes, not the rows that failed to cover it");
        (await _context.Orders.AsNoTracking().SingleAsync(o => o.Id == orderId))
            .Status.Should().Be(OrderStatus.PendingPayment);
    }

    [Fact]
    public async Task A_terminal_order_is_not_revived_even_when_its_money_arrived()
    {
        // Tonight's production shape: money on an order its expired invoice already cancelled. This rule
        // says no - Cancelled is terminal, and handing out fuel for an order the platform declared dead
        // is not the cure. The cure is planning #867's fourth trigger: the expiry must not cancel an
        // order the money already pays for. Refusing here is what keeps that fix honest instead of
        // quietly resurrecting orders.
        var (orderId, userId) = await SeedOrderAsync(status: OrderStatus.Cancelled);
        await SeedMoneyAsync(orderId, userId, 1200m);

        var result = await TryAsync(orderId);

        result.Settled.Should().BeFalse();
        result.Refusal.Should().Be(SettleRefusal.OrderRefusesSettlement,
            "the caller has to be able to tell a dead order from a short one without reading a sentence");
        result.Reason.Should().Contain("Cancelled");

        (await _context.Orders.AsNoTracking().SingleAsync(o => o.Id == orderId))
            .Status.Should().Be(OrderStatus.Cancelled);
        (await _context.JournalEntries.AsNoTracking().CountAsync(j => j.SourceId == orderId))
            .Should().Be(0);
    }

    [Fact]
    public async Task An_order_already_handed_to_fulfilment_is_answered_rather_than_re_settled()
    {
        // The settlement channel arriving after the acquirer's delivery, or an operator pressing place a
        // second time. Declining is the right answer because the work is done, and the books stay at one
        // receipt either way - which is a property of the unique keys, not of this check.
        var (orderId, userId) = await SeedOrderAsync();
        await SeedMoneyAsync(orderId, userId, 1200m);

        (await TryAsync(orderId)).Settled.Should().BeTrue();

        var again = await TryAsync(orderId);
        again.Settled.Should().BeFalse();
        again.Refusal.Should().Be(SettleRefusal.AlreadyHandedOver,
            "a second arrival is not a failure, and the caller must be able to say so without reading prose");
        again.Reason.Should().Contain("already handed to fulfilment");

        (await _context.JournalEntries.AsNoTracking().CountAsync(j => j.SourceId == orderId))
            .Should().Be(2, "one receipt and one VAT leg, however many arrivals name the same order");
        (await _context.OutboxEvents.AsNoTracking().CountAsync())
            .Should().Be(1);
    }

    [Fact]
    public async Task A_hidden_order_whose_money_arrived_is_still_found_and_settled()
    {
        // The delete filter is a listing concern. An order the customer swiped away while its invoice
        // stayed payable is still an order somebody paid for, and a rule that could not see it would
        // drop the money on the floor - which is why both the load and the price read here ignore
        // filters.
        var (orderId, userId) = await SeedOrderAsync(deleted: true);
        await SeedMoneyAsync(orderId, userId, 1200m);

        (await TryAsync(orderId)).Settled.Should().BeTrue();

        var settled = await _context.Orders.IgnoreQueryFilters().AsNoTracking()
            .SingleAsync(o => o.Id == orderId);
        settled.IsDeleted.Should().BeFalse();
        settled.Status.Should().Be(OrderStatus.PendingFulfillment);
    }
}
