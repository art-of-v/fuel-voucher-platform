using FluentAssertions;
using FuelFlow.API.Features.Orders.SharedServices.Monobank;
using FuelFlow.API.Features.Orders.SharedServices.Monobank.Models;
using FuelFlow.Features.Brands;
using FuelFlow.Features.Orders.CreateCheckout;
using FuelFlow.Features.Orders.SettlePaidOrder;
using FuelFlow.Features.Orders.SharedModels;
using FuelFlow.Features.Settings;
using FuelFlow.Features.Vouchers;
using FuelFlow.Features.Vouchers.Availability;
using FuelFlow.Features.Vouchers.SharedModels;
using FuelFlow.Payments.SharedModels;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using FuelFlow.SharedKernel.Observability;
using FuelFlow.SharedKernel.Options;
using FuelFlow.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;
using static FuelFlow.SharedKernel.Domain.FuelIdMapper;

namespace FuelFlow.IntegrationTests.Orders;

/// <summary>
/// Trigger 2 of planning #867: the order appears second, and the money already held pays for it.
/// </summary>
/// <remarks>
/// <para>
/// The case that needed a human. The finance channel declares a transfer, there is no order yet, so
/// the money is held; the customer then presses Pay, an order is written, an invoice is minted for
/// it, and nothing connects the two. The invoice expires, the order is cancelled around money the
/// company already has, and someone watching the unallocated tab has to place it by hand — which is
/// what 862,00 ₴ on 2026-11-04 actually was.
/// </para>
/// <para>
/// These tests drive the real checkout handlers, because the seam is in the checkout: the order has
/// to be paid from held money after it exists and before the acquirer is asked, and what is asserted
/// first is the count on the acquirer. An invoice asked for on a settled order is a second charge for
/// fuel that is already on its way, so "zero" is the result that matters; the rest proves the settle
/// itself is complete — money, order, outbox and books, not three of the four.
/// </para>
/// <para>
/// What is asserted about the money is the <b>draw</b>, not an attached payment. The account is a
/// balance and a balance is spent: one transfer can pay for several orders and one order can be paid
/// from several transfers, neither of which <c>payments.order_id</c> can say. The rows that say it are
/// <c>payment_allocations</c>, and the database, not this handler, keeps them from spending the same
/// money twice — <see cref="Payments.HeldMoneyDrawGuardTests"/> stands that part up.
/// </para>
/// </remarks>
[Collection("Integration Tests")]
public sealed class HeldMoneyPaysForTheCheckoutTests : IClassFixture<TestDatabaseFixture>, IAsyncLifetime
{
    private const decimal Liters = 10m;
    private const int Quantity = 1;
    private const decimal CostPerLiter = 30m;
    private const decimal PricePerLiter = 90m;
    private const decimal BasketTotal = PricePerLiter * Liters * Quantity;

    private readonly TestDatabaseFixture _fixture;

    public HeldMoneyPaysForTheCheckoutTests(TestDatabaseFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync()
    {
        using var context = CreateContext();
        await PaymentTestReset.RunAsync(context);
        TestJournal.SeedChart(context);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Money_held_before_the_order_pays_for_the_cart_and_the_acquirer_is_never_asked()
    {
        var userId = await SeedAsync();
        await SeedHeldMoneyAsync(userId, BasketTotal);

        var acquirer = new CountingAcquirer();
        using var context = CreateContext();

        var response = await CartHandler(context, acquirer).HandleAsync(CartCommand(userId));

        response.AlreadyPaid.Should().BeTrue("the money was in the account before the order was");
        response.MonobankInvoiceId.Should().BeNull("there is no invoice on an order that is already paid");
        acquirer.InvoicesCreated.Should().Be(0,
            "an invoice is a request to be charged, and this customer has already been");

        var order = await context.Orders.AsNoTracking().SingleAsync(o => o.Id == response.OrderIds.Single());
        order.Status.Should().Be(OrderStatus.PendingFulfillment);
        order.MonobankStatus.Should().BeNull(
            "the acquirer said nothing about this order, and a column that claims it did is how the next "
            + "reconciliation gets misled");

        (await context.Payments.AsNoTracking().CountAsync(p => p.OrderId == order.Id))
            .Should().Be(0, "the declared transfer is money to a customer, not a charge for this sale");

        var draws = await context.PaymentAllocations.AsNoTracking()
            .Where(a => a.OrderId == order.Id)
            .Select(a => new { a.PaymentId, a.AmountUah })
            .ToListAsync();

        draws.Should().HaveCount(1, "the order is paid by a draw on the balance, which is what lets one "
            + "transfer pay for several sales instead of being claimed whole by the first");
        draws.Single().AmountUah.Should().Be(BasketTotal, "the order owes its price and the balance covered it");

        (await OrderPayment.MoneyOnItAsync(context, order.Id))
            .Should().Be(BasketTotal, "the gate that decides whether fuel is handed over has to see the draw");

        (await PaymentBalance.AvailableAsync(context, userId))
            .Should().BeEmpty("the whole transfer is committed to this order now");

        (await context.OutboxEvents.AsNoTracking().CountAsync(e => e.EventType == OutboxEventType.OrderCreated))
            .Should().Be(1, "the settled order has to reach the job that claims stock for it");

        // The books say nothing. The money that paid for this order is declared money - the settlement
        // channel's, the promo channel's - and the ruling of 2026-11-07 is that a sale it pays for
        // books nothing: no receipt, no VAT leg, no revenue. The money row, the draw and the
        // fulfilment's custody records are the facts; the books would otherwise show revenue that came
        // out of a promo budget nobody recognised as such.
        (await context.JournalEntries.AsNoTracking().CountAsync(j => j.SourceId == order.Id))
            .Should().Be(0, "promo revenue is not revenue");
    }

    [Fact]
    public async Task The_single_item_checkout_pays_the_same_way()
    {
        var userId = await SeedAsync();
        await SeedHeldMoneyAsync(userId, BasketTotal);

        var acquirer = new CountingAcquirer();
        using var context = CreateContext();

        var response = await PurchaseHandler(context, acquirer).HandleAsync(PurchaseCommand(userId));

        response.AlreadyPaid.Should().BeTrue();
        acquirer.InvoicesCreated.Should().Be(0);

        (await context.Orders.AsNoTracking().FirstAsync(o => o.Id == response.OrderId))
            .Status.Should().Be(OrderStatus.PendingFulfillment);
    }

    [Fact]
    public async Task Money_for_a_different_amount_is_left_where_it_is_and_the_invoice_is_asked_for()
    {
        var userId = await SeedAsync();
        await SeedHeldMoneyAsync(userId, BasketTotal - 100m);

        var acquirer = new CountingAcquirer();
        using var context = CreateContext();

        var response = await CartHandler(context, acquirer).HandleAsync(CartCommand(userId));

        response.AlreadyPaid.Should().BeFalse();
        acquirer.InvoicesCreated.Should().Be(1, "nothing here pays for the order in full, so the sale is still the acquirer's");

        (await context.Orders.AsNoTracking().FirstAsync(o => o.Id == response.OrderIds.Single()))
            .Status.Should().Be(OrderStatus.PendingPayment);
        (await context.Payments.AsNoTracking().CountAsync(p => p.OrderId == null))
            .Should().Be(1, "the short money stays held, where an operator can see it");

        (await context.PaymentAllocations.AsNoTracking().CountAsync())
            .Should().Be(0, "a shortfall that spent the balance would leave the customer with less money "
                + "and the order still unpaid");
        (await PaymentBalance.AvailableAsync(context, userId)).Single().RemainingUah
            .Should().Be(BasketTotal - 100m, "the short money is untouched and still the customer's to spend");
    }

    /// <summary>
    /// Money an operator dismissed is closed, not just hidden from the queue: it must not pay for the
    /// next cart.
    /// </summary>
    /// <remarks>
    /// Dismissal is what keeps the unallocated worklist readable — a test capture, a transfer from
    /// before the payer registered — and its reason says the money is real and will never be placed.
    /// The balance is the other half of that promise: a dismissed capture that still counted here
    /// would let the very next checkout settle from the money the operator had just closed, which is
    /// exactly the test money a QA account holds.
    /// </remarks>
    [Fact]
    public async Task Money_an_operator_dismissed_does_not_pay_for_a_cart()
    {
        var userId = await SeedAsync();
        var dismissed = await SeedHeldMoneyAsync(userId, BasketTotal);
        await DismissAsync(dismissed, userId);

        var acquirer = new CountingAcquirer();
        using var context = CreateContext();

        var response = await CartHandler(context, acquirer).HandleAsync(CartCommand(userId));

        response.AlreadyPaid.Should().BeFalse("the operator said this money would never be placed");
        acquirer.InvoicesCreated.Should().Be(1, "with the closed transfer out of the balance, the sale is the acquirer's again");

        (await context.Orders.AsNoTracking().FirstAsync(o => o.Id == response.OrderIds.Single()))
            .Status.Should().Be(OrderStatus.PendingPayment);
        (await context.PaymentAllocations.AsNoTracking().CountAsync())
            .Should().Be(0, "nothing may be drawn on money a person closed on purpose");
        (await PaymentBalance.AvailableAsync(context, userId))
            .Should().BeEmpty("a dismissed transfer is not the customer's balance");
    }

    /// <summary>
    /// One declared transfer, two sales. The case the ruling was about: finance says 1 000,00 ₴ arrived
    /// for this customer, the customer buys twice, and the account cannot answer the second order.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The second cart is a different fuel on purpose. The same cart tapped twice is a duplicate, and
    /// the checkout's answer to a duplicate is the order it already paid for — see
    /// <see cref="A_second_tap_for_a_cart_the_held_money_paid_does_not_become_a_second_invoice"/>. A
    /// second, different sale is the case where the balance has to be asked again and can say no.
    /// </para>
    /// <para>
    /// The first cart settles and the second is refused, so the money left (100,00 ₴) stays visible in
    /// the panel and the second order goes to the acquirer instead of dying on an unpaid invoice nobody
    /// meant to send. The 800,00 ₴ that was never declared is the company's news, and the refusal is
    /// what says it.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task One_transfer_pays_the_first_cart_and_the_second_order_is_short()
    {
        var userId = await SeedAsync();
        await SeedHeldMoneyAsync(userId, 1_000m);

        var acquirer = new CountingAcquirer();

        using var first = CreateContext();
        var settled = await CartHandler(first, acquirer).HandleAsync(CartCommand(userId));

        settled.AlreadyPaid.Should().BeTrue("900,00 ₴ of a balance pays for a 900,00 ₴ order");
        acquirer.InvoicesCreated.Should().Be(0);

        using var second = CreateContext();
        var shortCart = await CartHandler(second, acquirer).HandleAsync(CartCommand(userId, "okko-dp"));

        shortCart.AlreadyPaid.Should().BeFalse("the order cannot be fulfilled on money the account does not hold");
        acquirer.InvoicesCreated.Should().Be(1, "the second sale is the acquirer's until somebody declares more");

        using var verify = CreateContext();
        (await verify.Orders.AsNoTracking().FirstAsync(o => o.Id == shortCart.OrderIds.Single()))
            .Status.Should().Be(OrderStatus.PendingPayment, "a short balance leaves the order waiting, not settled");

        (await verify.PaymentAllocations.AsNoTracking().CountAsync())
            .Should().Be(1, "the shortfall draws nothing — the last 100,00 ₴ stays free for the next order "
                + "instead of being spent on one that still could not settle");

        var held = await PaymentBalance.AvailableAsync(verify, userId);
        held.Should().HaveCount(1);
        held.Single().RemainingUah.Should().Be(100m, "what the first cart took is gone and the rest is still the customer's");
    }

    /// <summary>
    /// A balance is spent across the transfers in it, oldest first, and no single one of them has to be
    /// big enough.
    /// </summary>
    [Fact]
    public async Task A_cart_bigger_than_any_one_transfer_is_paid_from_two_of_them()
    {
        var userId = await SeedAsync();
        var older = await SeedHeldMoneyAsync(userId, 500m, hoursAgo: 48);
        var newer = await SeedHeldMoneyAsync(userId, 500m, hoursAgo: 8);

        var acquirer = new CountingAcquirer();
        using var context = CreateContext();

        var response = await CartHandler(context, acquirer).HandleAsync(CartCommand(userId));

        response.AlreadyPaid.Should().BeTrue("the account holds 1 000,00 ₴ and the order owes 900,00 ₴");
        acquirer.InvoicesCreated.Should().Be(0, "asking the bank for money the customer has already sent is a second charge");

        var order = response.OrderIds.Single();
        var draws = await context.PaymentAllocations.AsNoTracking()
            .Where(a => a.OrderId == order)
            .Select(a => new { a.PaymentId, a.AmountUah })
            .ToListAsync();

        draws.Should().HaveCount(2, "one transfer does not cover it, so the draw cuts across both");
        draws.Single(d => d.PaymentId == older).AmountUah.Should().Be(500m, "the money waiting longest is emptied first");
        draws.Single(d => d.PaymentId == newer).AmountUah.Should().Be(400m);

        (await OrderPayment.MoneyOnItAsync(context, order)).Should().Be(BasketTotal);
        (await PaymentBalance.AvailableAsync(context, userId)).Single().RemainingUah
            .Should().Be(100m, "the newest transfer keeps the change, and it is still spendable");
    }

    /// <summary>
    /// Two transfers for the same price as the cart used to be a coin-flip the code refused to take, and
    /// it left the order unpaid. A balance does not need the choice: it spends the oldest money and the
    /// customer's order settles.
    /// </summary>
    [Fact]
    public async Task Two_transfers_of_that_amount_are_answered_from_the_older_one()
    {
        var userId = await SeedAsync();
        var older = await SeedHeldMoneyAsync(userId, BasketTotal, hoursAgo: 48);
        await SeedHeldMoneyAsync(userId, BasketTotal, hoursAgo: 8);

        var acquirer = new CountingAcquirer();
        using var context = CreateContext();

        var response = await CartHandler(context, acquirer).HandleAsync(CartCommand(userId));

        response.AlreadyPaid.Should().BeTrue("two payments are not two guesses; they are a balance");
        acquirer.InvoicesCreated.Should().Be(0);

        (await context.PaymentAllocations.AsNoTracking()
                .Where(a => a.OrderId == response.OrderIds.Single())
                .Select(a => a.PaymentId).ToListAsync())
            .Should().BeEquivalentTo([older], "the draw takes the money that has been waiting longest, so the "
                + "rule is not a choice between the customer's own transfers");
    }

    /// <summary>
    /// The branch the price gate sits above, so that the gate is known not to have swallowed it: money
    /// an operator placed on the order by hand (planning #862) needs no draw, only the settle the
    /// invoice was standing in the way of.
    /// </summary>
    [Fact]
    public async Task Money_already_on_the_order_is_settled_without_a_draw()
    {
        var userId = await SeedAsync();

        var acquirer = new CountingAcquirer();
        using var context = CreateContext();
        var invoiceAsked = await CartHandler(context, acquirer).HandleAsync(CartCommand(userId));
        var orderId = invoiceAsked.OrderIds.Single();
        acquirer.InvoicesCreated.Should().Be(1, "with an empty account the checkout is the ordinary one");

        await SeedAttachedMoneyAsync(orderId, userId, BasketTotal);

        var result = await TestJournal.HeldMoneyFor(context)
            .HandleAsync(new SettleFromHeldMoneyCommand { OrderId = orderId, Because = "the test" });

        result.Settled.Should().BeTrue(
            "the money is on the order and covers it; the only thing missing was the settle");
        result.PaymentIds.Should().BeEmpty("nothing was drawn, so there is no transfer this answer names");

        (await context.PaymentAllocations.AsNoTracking().CountAsync(a => a.OrderId == orderId))
            .Should().Be(0, "an attached payment is counted whole, and drawing it as well would spend it twice");
        (await context.Orders.AsNoTracking().FirstAsync(o => o.Id == orderId)).Status
            .Should().Be(OrderStatus.PendingFulfillment);
    }

    /// <summary>
    /// The retry that follows a settled checkout. An order leaves the basket's partial index when it
    /// settles, so without the guard's second read a customer whose phone shows an error — a bundle
    /// older than this change has nothing to open and says so — would tap again and be handed an
    /// invoice for a sale already paid for.
    /// </summary>
    [Fact]
    public async Task A_second_tap_for_a_cart_the_held_money_paid_does_not_become_a_second_invoice()
    {
        var userId = await SeedAsync();
        await SeedHeldMoneyAsync(userId, BasketTotal);

        var acquirer = new CountingAcquirer();

        using var first = CreateContext();
        var settled = await CartHandler(first, acquirer).HandleAsync(CartCommand(userId));

        using var second = CreateContext();
        var again = await CartHandler(second, acquirer).HandleAsync(CartCommand(userId));

        again.OrderIds.Should().BeEquivalentTo(settled.OrderIds, "the second request is the same purchase");
        again.AlreadyPaid.Should().BeTrue();
        acquirer.InvoicesCreated.Should().Be(0, "not now, not on the retry");

        using var verify = CreateContext();
        (await verify.Orders.AsNoTracking().CountAsync(o => o.UserId == userId))
            .Should().Be(1, "one cart, one order, whatever the phone did in between");
    }

    private static BulkCheckoutCommand CartCommand(Guid userId, string fuelKey = "okko-95") => new()
    {
        UserId = userId,
        Items =
        {
            new CheckoutItem
            {
                BrandId = BrandIds.Okko,
                BrandName = "okko",
                FuelTypeId = ToUuid(fuelKey),
                Liters = Liters,
                Quantity = Quantity,
                Price = PricePerLiter * Liters
            }
        }
    };

    private static CreateCheckoutCommand PurchaseCommand(Guid userId) => new()
    {
        UserId = userId,
        BrandId = BrandIds.Okko,
        BrandName = "okko",
        FuelTypeId = ToUuid("okko-95"),
        Liters = Liters,
        Quantity = Quantity,
        Price = PricePerLiter * Liters
    };

    private BulkCheckoutCommandHandler CartHandler(ApplicationDbContext context, CountingAcquirer acquirer)
        => new(
            context,
            new StubMonobankClientFactory(acquirer),
            new SingleMerchantResolver(),
            Options.Create(MonobankOptions()),
            new RuntimeSettingsService(context),
            NullLogger<BulkCheckoutCommandHandler>.Instance,
            NotificationDispatcher.Disabled,
            Stock(context),
            new FuelFlowMetrics(),
            TestJournal.HeldMoneyFor(context));

    private CreateCheckoutCommandHandler PurchaseHandler(ApplicationDbContext context, CountingAcquirer acquirer)
        => new(
            context,
            new StubMonobankClientFactory(acquirer),
            new SingleMerchantResolver(),
            Options.Create(MonobankOptions()),
            NullLogger<CreateCheckoutCommandHandler>.Instance,
            new FuelFlowMetrics(),
            NotificationDispatcher.Disabled,
            Stock(context),
            TestJournal.HeldMoneyFor(context));

    private static MonobankOptions MonobankOptions() => new()
    {
        Token = "test_token",
        WebhookUrl = "https://test.local/webhook",
        RedirectUrl = "https://test.local/redirect",
        BaseUrl = "https://api.test.local",
        Enabled = false
    };

    private PackageStockAvailability Stock(ApplicationDbContext context)
        => new(context, new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["VoucherExpiration:Enabled"] = "true" })
            .Build());

    private ApplicationDbContext CreateContext()
        => new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(_fixture.ConnectionString)
            .Options, TestQrCipher.Instance);

    /// <summary>
    /// A customer with stock behind two sellable fuels — the three things a checkout needs. Every cart
    /// in this file is the same size and the same price, so what separates a settle from a shortfall is
    /// only how much the account holds, never what the order happens to cost.
    /// </summary>
    private async Task<Guid> SeedAsync()
    {
        var userId = Guid.NewGuid();

        using var context = CreateContext();

        var role = await context.Roles.FirstOrDefaultAsync(r => r.Name == SeedRoles.UserName);
        role ??= new Role { Id = SeedRoles.UserRoleId, Name = SeedRoles.UserName, CreatedAtUtc = TestClock.UtcNow };
        if (context.Entry(role).State == EntityState.Detached)
            context.Roles.Add(role);

        context.Users.Add(new User
        {
            Id = userId,
            PhoneNumber = $"+38099{userId.ToString("N")[..7]}",
            RoleId = role.Id,
            Role = role,
            IsActive = true,
            SettlementCode = string.Empty,
            TokenVersion = 1,
            CreatedAtUtc = TestClock.UtcNow,
            UpdatedAtUtc = TestClock.UtcNow
        });

        // Two fuels, priced and stocked the same way, because a second DIFFERENT sale is what makes a
        // balance say no: the same cart tapped twice is a duplicate, and the checkout answers that with
        // the order it already paid for.
        foreach (var fuelKey in new[] { "okko-95", "okko-dp" })
        {
            var fuel = await context.FuelTypes.FirstAsync(f => f.Id == ToUuid(fuelKey));
            fuel.SupplierPricePerLiter = CostPerLiter;
            fuel.MarginUahPerLiter = PricePerLiter - CostPerLiter;
            fuel.UpdatedAtUtc = TestClock.UtcNow;

            var pool = DataKinds.Real;
            if (!await context.FuelNominals.AnyAsync(n => n.FuelTypeId == fuel.Id && n.DataKind == pool && n.Liters == Liters))
            {
                context.FuelNominals.Add(new FuelNominal { FuelTypeId = fuel.Id, DataKind = pool, Liters = Liters });
            }

            context.FuelVouchers.Add(new FuelVoucher
            {
                Id = Guid.CreateVersion7(),
                BrandId = BrandIds.ForSlug("okko"),
                Brand = "okko",
                FuelTypeId = fuel.Id,
                Liters = Liters,
                DataKind = DataKinds.Real,
                SupplierExpirationDate = DateOnly.FromDateTime(TestClock.UtcNow.AddMonths(1)),
                CustomerExpirationDate = DateOnly.FromDateTime(TestClock.UtcNow.AddMonths(1)),
                VoucherNumber = $"HELD-{Guid.NewGuid():N}"[..16],
                QrPayload = $"qr-{Guid.NewGuid():N}",
                Status = VoucherStatus.Available,
                CostPerLiter = CostPerLiter,
                CreatedAtUtc = TestClock.UtcNow,
                UpdatedAtUtc = TestClock.UtcNow
            });
        }

        await context.SaveChangesAsync();
        return userId;
    }

    /// <summary>
    /// Money declared for a customer with no order against it — exactly what the channel holds. The id
    /// comes back because which transfer a draw spends is one of the things these tests are about, and
    /// it has to be named rather than found by which row a query happens to return first.
    /// </summary>
    private async Task<Guid> SeedHeldMoneyAsync(Guid userId, decimal amount, int hoursAgo = 0)
    {
        using var context = CreateContext();
        var paidAt = TestClock.UtcNow.AddHours(-hoursAgo);

        var payment = new Payment
        {
            Id = Guid.CreateVersion7(),
            Kind = PaymentKind.Capture,
            Provider = PaymentProviders.Declared,
            UserId = userId,
            OrderId = null,
            AmountUah = amount,
            AmountKopecks = (long)Math.Round(amount * 100),
            Currency = "UAH",
            Status = PaymentStatus.Succeeded,
            PaidAtUtc = paidAt,
            CreatedByUserName = "finance",
            IsAutomatic = true,
            CreatedAtUtc = paidAt,
            UpdatedAtUtc = paidAt
        };

        context.Payments.Add(payment);
        await context.SaveChangesAsync();
        return payment.Id;
    }

    /// <summary>
    /// The panel's dismissal, written the way its handler writes it: the three columns move together
    /// and the money itself — amount, status, journal, statement row — is untouched.
    /// </summary>
    private async Task DismissAsync(Guid paymentId, Guid byUserId)
    {
        using var context = CreateContext();
        var payment = await context.Payments.AsTracking().SingleAsync(p => p.Id == paymentId);

        payment.DismissedAtUtc = TestClock.UtcNow;
        payment.DismissedReason = "a test capture";
        payment.DismissedByUserId = byUserId;
        payment.UpdatedAtUtc = TestClock.UtcNow;

        await context.SaveChangesAsync();
    }

    /// <summary>
    /// Money an operator has placed on one order: <c>payments.order_id</c> set, no draw behind it. The
    /// shape the panel's place action writes, and the one the whole-amount read is there for.
    /// </summary>
    private async Task SeedAttachedMoneyAsync(Guid orderId, Guid userId, decimal amount)
    {
        using var context = CreateContext();

        context.Payments.Add(new Payment
        {
            Id = Guid.CreateVersion7(),
            Kind = PaymentKind.Capture,
            Provider = PaymentProviders.Declared,
            UserId = userId,
            OrderId = orderId,
            AmountUah = amount,
            AmountKopecks = (long)Math.Round(amount * 100),
            Currency = "UAH",
            Status = PaymentStatus.Succeeded,
            PaidAtUtc = TestClock.UtcNow,
            CreatedByUserName = "finance",
            IsAutomatic = false,
            CreatedAtUtc = TestClock.UtcNow,
            UpdatedAtUtc = TestClock.UtcNow
        });

        await context.SaveChangesAsync();
    }

    /// <summary>The acquirer, counting only. Nothing here races, so no invoice is ever held.</summary>
    private sealed class CountingAcquirer : IMonobankClient
    {
        private int _invoicesCreated;

        public int InvoicesCreated => Volatile.Read(ref _invoicesCreated);

        public Task<MonobankInvoiceResponse> CreateInvoiceAsync(
            MonobankInvoiceRequest request,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _invoicesCreated);
            var invoiceId = $"MOCK_{Guid.NewGuid():N}";
            return Task.FromResult(new MonobankInvoiceResponse
            {
                InvoiceId = invoiceId,
                PageUrl = $"https://mock-monobank.local/invoice/{invoiceId}"
            });
        }

        public Task<MonobankInvoiceStatus> GetInvoiceStatusAsync(
            string invoiceId, CancellationToken cancellationToken = default)
            => Task.FromResult(new MonobankInvoiceStatus { InvoiceId = invoiceId, Status = "created" });

        public Task<MonobankCancelResponse> CancelInvoiceAsync(
            string invoiceId, long amountKopecks, string extRef,
            IReadOnlyList<MonobankBasketItem>? items = null, CancellationToken cancellationToken = default)
            => Task.FromResult(new MonobankCancelResponse { Status = "processing" });

        public Task<IReadOnlyList<MonobankStatementItem>> GetStatementAsync(
            DateTime fromUtc, DateTime toUtc, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<MonobankStatementItem>>([]);
    }

    private sealed class SingleMerchantResolver : IMonobankMerchantResolver
    {
        public MonobankMerchant Resolve(string? phoneNumber, bool isQaAccount) => MonobankMerchant.Live;
    }
}
