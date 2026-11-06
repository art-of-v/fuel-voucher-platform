using FluentAssertions;
using FuelFlow.API.Features.Orders.SharedServices.Monobank;
using FuelFlow.API.Features.Orders.SharedServices.Monobank.Models;
using FuelFlow.Features.Brands;
using FuelFlow.Features.Orders.CreateCheckout;
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
/// Item 5 of the owner's ruling of 2026-11-06 (planning #889), across all three answers the balance can
/// give to a promotional tap: «Якщо на рахунку не вистачає коштів для покриття замовлення — воно не може
/// бути виконане.»
/// </summary>
/// <remarks>
/// <para>
/// The ruling says an order formed by the second button is paid «з врахуванням наявних коштів» — with the
/// money already on the account — and that what is not covered cannot be performed. Read as one boolean
/// that is the same sentence twice: money that covers pays, money that does not fails. The reason it is
/// not one boolean is that the account has a third state, and it is the reason the button exists at all:
/// an account holding NOTHING is not short of anything, it is waiting for the declaration that has not
/// arrived yet. That branch has to keep writing orders.
/// </para>
/// <para>
/// So the interesting boundary is between "holds nothing" and "holds 400 of 900", and before this change
/// both answered "the order waits" — because the balance had only ever been asked whether it covered the
/// price, and a partly-funded balance answers no in exactly the same words an empty one does. The
/// difference is what the customer is left holding: a 900 ₴ order claiming stock that the company will
/// never be paid 500 ₴ of, or no order and a number.
/// </para>
/// <para>
/// These drive the real handlers against a real PostgreSQL, so the refusals are proved by the rows that
/// are absent: no order, no draw, no invoice asked, and the held money still spendable. The retry pair is
/// here too, because the second tap is answered by the order that already holds the cart, and a balance
/// that arrived in between is the whole point of the button — that tap is where a waiting order becomes
/// a paid one.
/// </para>
/// </remarks>
[Collection("Integration Tests")]
public sealed class PromoCheckoutBalanceTests : IClassFixture<TestDatabaseFixture>, IAsyncLifetime
{
    private const decimal Liters = 10m;
    private const int Quantity = 1;
    private const decimal CostPerLiter = 30m;
    private const decimal PricePerLiter = 90m;
    private const decimal BasketTotal = PricePerLiter * Liters * Quantity;

    /// <summary>Money on the account that is real and is not enough — the state the ruling refuses.</summary>
    private const decimal HeldButShort = 400m;

    /// <summary>What the customer is missing, and the figure the phone has to show them.</summary>
    private const decimal Missing = BasketTotal - HeldButShort;

    private readonly TestDatabaseFixture _fixture;

    /// <summary>Names each seeded customer's settlement code, so no two of them are the same string.</summary>
    private static int _seededUsers;

    public PromoCheckoutBalanceTests(TestDatabaseFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync()
    {
        using var context = CreateContext();
        await PaymentTestReset.RunAsync(context);
        TestJournal.SeedChart(context);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    /// <summary>
    /// The branch the second button exists for. An account the channel has not been paid yet is not a
    /// short account, and refusing it would close the door the ruling opened: the order is written, it
    /// claims nothing but its basket, and a declaration that arrives later pays for it.
    /// </summary>
    [Fact]
    public async Task AnAccountHoldingNothing_WritesTheOrder_AndLeavesItWaiting()
    {
        var userId = await SeedAsync();

        var acquirer = new CountingAcquirer();
        using var context = CreateContext();

        var response = await CartHandler(context, acquirer)
            .HandleAsync(CartCommand(userId));

        response.AlreadyPaid.Should().BeFalse("the account holds nothing, so nothing could pay for it");
        acquirer.InvoicesCreated.Should().Be(0, "the customer pressed the promotional button, not the bank one");

        var order = await context.Orders.AsNoTracking().SingleAsync(o => o.Id == response.OrderIds.Single());
        order.Status.Should().Be(OrderStatus.PendingPayment);
        order.SettlementRoute.Should().Be(SettlementRoute.Declared);

        (await context.PaymentAllocations.AsNoTracking().CountAsync())
            .Should().Be(0, "there was nothing to draw, and a draw of nothing is a row that lies");
    }

    /// <summary>
    /// Item 5, the tap. Money exists and does not cover, so the order cannot be performed — which is a
    /// refusal at the tap, with the figure, not an order that waits.
    /// </summary>
    [Fact]
    public async Task AnAccountPartlyFunded_RefusesTheCart_AndNamesTheShortfall()
    {
        var userId = await SeedAsync();
        await SeedHeldMoneyAsync(userId, HeldButShort);

        var acquirer = new CountingAcquirer();
        var merchant = new CountingMerchantResolver();
        using var context = CreateContext();

        var act = () => CartHandler(context, acquirer, merchant).HandleAsync(CartCommand(userId));

        var refused = (await act.Should().ThrowAsync<PromoBalanceShortException>()).Which;

        refused.ShortfallUah.Should().Be(Missing,
            "the customer's sentence needs the amount, and the amount is the draw's own answer, not arithmetic "
             + "done twice in two places");
        refused.HeldUah.Should().Be(HeldButShort);
        refused.OwedUah.Should().Be(BasketTotal);
        PromoBalanceShortException.Code.Should().Be("promo_balance_short",
            "the app keys its translation on this string, so renaming it silently is a customer with no message");
    }

    /// <summary>
    /// The same refusal asked of the single-item checkout — the other button's other endpoint, and the one
    /// a customer whose cart is one package arrives at. Both are the ruling's, and a gate on one of them is
    /// a gate that can be walked around by buying one item at a time.
    /// </summary>
    [Fact]
    public async Task AnAccountPartlyFunded_RefusesASingleItem_WithTheSameFigures()
    {
        var userId = await SeedAsync();
        await SeedHeldMoneyAsync(userId, HeldButShort);

        using var context = CreateContext();
        var act = () => PurchaseHandler(context, new CountingAcquirer())
            .HandleAsync(PurchaseCommand(userId));

        var refused = (await act.Should().ThrowAsync<PromoBalanceShortException>()).Which;
        refused.ShortfallUah.Should().Be(Missing);
        refused.OwedUah.Should().Be(BasketTotal);
    }

    /// <summary>
    /// What a refusal is worth is that nothing happened. The order would have claimed stock, the draw
    /// would have spent the customer's 400 ₴ on a sale that still could not be fulfilled, and the
    /// acquirer would have been asked about a tap that never wanted it — three ways for half a payment to
    /// become a mess an operator has to untangle.
    /// </summary>
    [Fact]
    public async Task APromoSaleRefusedAsShort_LeavesNoOrder_NoDraw_AndNoInvoice()
    {
        var userId = await SeedAsync();
        await SeedHeldMoneyAsync(userId, HeldButShort);

        var acquirer = new CountingAcquirer();
        var merchant = new CountingMerchantResolver();

        using (var context = CreateContext())
        {
            var act = () => CartHandler(context, acquirer, merchant).HandleAsync(CartCommand(userId));
            await act.Should().ThrowAsync<PromoBalanceShortException>();
        }

        using var verify = CreateContext();
        (await verify.Orders.AsNoTracking().CountAsync(o => o.UserId == userId))
            .Should().Be(0, "the refused tap writes no row, so there is nothing to cancel, refund or explain");
        (await verify.Orders.AsNoTracking().CountAsync(o => o.UserId == userId && o.CheckoutKey != null))
            .Should().Be(0, "and it holds no basket, so the next tap is not refused for holding this one");

        (await verify.PaymentAllocations.AsNoTracking().CountAsync())
            .Should().Be(0, "a shortfall that spent the balance leaves the customer with less money and no fuel");
        (await PaymentBalance.AvailableAsync(verify, userId)).Single().RemainingUah
            .Should().Be(HeldButShort, "the 400 ₴ is still the customer's to spend on the next order");

        acquirer.InvoicesCreated.Should().Be(0, "a declared tap never asks the bank, and a refused one asks nothing");
        merchant.Calls.Should().Be(0, "and it does not even look for a terminal");

        (await verify.Fulfillments.AsNoTracking().CountAsync())
            .Should().Be(0, "no voucher was claimed for a sale that did not happen");
    }

    /// <summary>
    /// The cover branch, guarded so that the new gate cannot swallow it: money that is enough pays, and
    /// it pays as the order is written — the acquirer is never asked for an invoice on a sale the company
    /// has already been given the cash for.
    /// </summary>
    [Fact]
    public async Task AnAccountThatCoversThePrice_PaysTheOrder_AsItIsWritten()
    {
        var userId = await SeedAsync();
        await SeedHeldMoneyAsync(userId, BasketTotal);

        var acquirer = new CountingAcquirer();
        using var context = CreateContext();

        var response = await CartHandler(context, acquirer).HandleAsync(CartCommand(userId));

        response.AlreadyPaid.Should().BeTrue();
        acquirer.InvoicesCreated.Should().Be(0);

        var order = await context.Orders.AsNoTracking().SingleAsync(o => o.Id == response.OrderIds.Single());
        order.Status.Should().Be(OrderStatus.PendingFulfillment);

        (await context.PaymentAllocations.AsNoTracking().CountAsync(a => a.OrderId == order.Id))
            .Should().Be(1, "covering is a draw on the balance, not a judgement that the money looked big enough");
        (await PaymentBalance.AvailableAsync(context, userId))
            .Should().BeEmpty("the balance is committed to this order now");
    }

    /// <summary>
    /// Two transfers, one order, neither big enough: the balance is the account, and an account that
    /// covers is not a refusal however the money arrived. Oldest first, so which one emptied is a rule and
    /// not the order a query returned rows in.
    /// </summary>
    [Fact]
    public async Task MoneyAcrossTwoTransfers_CoversTheOrder_AndIsNotRefused()
    {
        var userId = await SeedAsync();
        var older = await SeedHeldMoneyAsync(userId, 500m, hoursAgo: 48);
        var newer = await SeedHeldMoneyAsync(userId, 500m, hoursAgo: 8);

        using var context = CreateContext();
        var response = await CartHandler(context, new CountingAcquirer()).HandleAsync(CartCommand(userId));

        response.AlreadyPaid.Should().BeTrue("900 ₴ is owed and 1 000 ₴ is held, and held is held across transfers");

        var order = response.OrderIds.Single();
        var draws = await context.PaymentAllocations.AsNoTracking()
            .Where(a => a.OrderId == order)
            .Select(a => new { a.PaymentId, a.AmountUah })
            .ToListAsync();

        draws.Should().HaveCount(2);
        draws.Single(d => d.PaymentId == older).AmountUah.Should().Be(500m);
        draws.Single(d => d.PaymentId == newer).AmountUah.Should().Be(400m);
    }

    /// <summary>
    /// The same short account, pressing the other button. The gate belongs to the offer's rules, and the
    /// payment route is the one every account has always had: a refusal that also stopped the bank would
    /// turn a rule about promotional money into a way to lose a sale.
    /// </summary>
    [Fact]
    public async Task APaymentTapOnAShortPromoAccount_IsNotRefused_AndAsksTheBank()
    {
        var userId = await SeedAsync();
        await SeedHeldMoneyAsync(userId, HeldButShort);

        var acquirer = new CountingAcquirer();
        using var context = CreateContext();

        var response = await CartHandler(context, acquirer).HandleAsync(CartCommand(userId, SettlementRoute.Acquirer));

        response.AlreadyPaid.Should().BeFalse();
        acquirer.InvoicesCreated.Should().Be(1, "the bank is asked whenever the money does not cover, on this route");

        (await context.PaymentAllocations.AsNoTracking().CountAsync())
            .Should().Be(0, "the short money stays where an operator can see it, as it did before this gate");
    }

    /// <summary>
    /// The tap that is the point of the flow: the order was written while the account held nothing, the
    /// declaration arrived after it, and the customer pressing the promotional button again is how the
    /// waiting order is paid. The basket guard answers with the order that holds the cart, and the money
    /// question is then asked of that order — not of a new one.
    /// </summary>
    [Fact]
    public async Task ASecondPromoTap_PaysTheWaitingOrder_WhenTheDeclarationArrived()
    {
        var userId = await SeedAsync();

        var acquirer = new CountingAcquirer();

        Guid orderId;
        using (var first = CreateContext())
        {
            var waiting = await CartHandler(first, acquirer).HandleAsync(CartCommand(userId));
            orderId = waiting.OrderIds.Single();
            waiting.AlreadyPaid.Should().BeFalse("the account held nothing at the tap");
        }

        await SeedHeldMoneyAsync(userId, BasketTotal);

        using var second = CreateContext();
        var again = await CartHandler(second, acquirer).HandleAsync(CartCommand(userId));

        again.OrderIds.Should().BeEquivalentTo([orderId], "one cart is one order, whatever the phone did in between");
        again.AlreadyPaid.Should().BeTrue("the money the second tap found covers the order the first tap wrote");
        acquirer.InvoicesCreated.Should().Be(0, "not on either tap");

        var order = await second.Orders.AsNoTracking().SingleAsync(o => o.Id == orderId);
        order.Status.Should().Be(OrderStatus.PendingFulfillment,
            "the tap that found the money settles the order it was answering with, which is the whole use of the button");

        (await second.PaymentAllocations.AsNoTracking().CountAsync(a => a.OrderId == orderId))
            .Should().Be(1);
    }

    /// <summary>
    /// The same retry with half the declaration: the waiting order keeps waiting and the tap is refused
    /// with what is missing. The half-money must not be taken — the order still cannot be performed, and
    /// spending the account on it would leave the customer poorer with the same unpaid basket.
    /// </summary>
    [Fact]
    public async Task ASecondPromoTap_IsRefused_WhenHalfTheDeclarationArrived()
    {
        var userId = await SeedAsync();

        using var first = CreateContext();
        var waiting = await CartHandler(first, new CountingAcquirer()).HandleAsync(CartCommand(userId));
        var orderId = waiting.OrderIds.Single();

        await SeedHeldMoneyAsync(userId, HeldButShort);

        using var second = CreateContext();
        var act = () => CartHandler(second, new CountingAcquirer()).HandleAsync(CartCommand(userId));

        var refused = (await act.Should().ThrowAsync<PromoBalanceShortException>()).Which;
        refused.ShortfallUah.Should().Be(Missing, "the figure is the same whichever of the two taps asked");

        using var verify = CreateContext();
        var still = await verify.Orders.AsNoTracking().SingleAsync(o => o.Id == orderId);
        still.Status.Should().Be(OrderStatus.PendingPayment,
            "the order that was waiting before the tap is waiting after it; a refusal changed nothing");
        still.CheckoutKey.Should().NotBeNull("and it still holds its basket, so the next tap answers with it again");

        (await verify.PaymentAllocations.AsNoTracking().CountAsync())
            .Should().Be(0, "the refused half never left the account");
        (await PaymentBalance.AvailableAsync(verify, userId)).Single().RemainingUah.Should().Be(HeldButShort);
    }

    /// <summary>
    /// A declaration the channel already named on this very order. The order owes nothing, so the tap that
    /// finds it has no money question left — only the settle it has been waiting for, which the balance
    /// takes no draw of. Answering "still waiting" here would be the app telling a customer to wait for
    /// money the company has already booked against their own order.
    /// </summary>
    [Fact]
    public async Task ASecondPromoTap_SettlesAnOrderTheChannelAlreadyNamed()
    {
        var userId = await SeedAsync();

        var acquirer = new CountingAcquirer();

        using var first = CreateContext();
        var waiting = await CartHandler(first, acquirer).HandleAsync(CartCommand(userId));
        var orderId = waiting.OrderIds.Single();

        await NameMoneyOnAsync(orderId, userId, BasketTotal);

        using var second = CreateContext();
        var again = await CartHandler(second, acquirer).HandleAsync(CartCommand(userId));

        again.OrderIds.Should().BeEquivalentTo([orderId]);
        again.AlreadyPaid.Should().BeTrue("the money is on the order; the only thing missing was the settle");

        var order = await second.Orders.AsNoTracking().SingleAsync(o => o.Id == orderId);
        order.Status.Should().Be(OrderStatus.PendingFulfillment);

        (await second.PaymentAllocations.AsNoTracking().CountAsync(a => a.OrderId == orderId))
            .Should().Be(0, "an attached payment is counted whole, and drawing it too would spend it twice");
    }

    /// <summary>
    /// Half the price named on the order, the account holding nothing else. What is still owed is the
    /// order's price less what is on it, and an account holding nothing of that is the wait again — not a
    /// refusal, because there is no money the ruling could call insufficient.
    /// </summary>
    [Fact]
    public async Task ASecondPromoTap_WaitsWhenOnlyPartOfThePriceWasNamedOnTheOrder()
    {
        var userId = await SeedAsync();

        using var first = CreateContext();
        var waiting = await CartHandler(first, new CountingAcquirer()).HandleAsync(CartCommand(userId));
        var orderId = waiting.OrderIds.Single();

        await NameMoneyOnAsync(orderId, userId, HeldButShort);

        using var second = CreateContext();
        var again = await CartHandler(second, new CountingAcquirer()).HandleAsync(CartCommand(userId));

        again.AlreadyPaid.Should().BeFalse("500 ₴ of the price is still owed and the account holds nothing");
        again.OrderIds.Should().BeEquivalentTo([orderId]);

        (await second.Orders.AsNoTracking().SingleAsync(o => o.Id == orderId))
            .Status.Should().Be(OrderStatus.PendingPayment,
                "the money named on it stays named, and a person decides what happens next");
    }

    private static BulkCheckoutCommand CartCommand(
        Guid userId, SettlementRoute route = SettlementRoute.Declared) => new()
    {
        UserId = userId,
        SettlementRoute = route,
        Items =
        {
            new CheckoutItem
            {
                BrandId = BrandIds.Okko,
                BrandName = "okko",
                FuelTypeId = ToUuid("okko-95"),
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
        Price = PricePerLiter * Liters,
        SettlementRoute = SettlementRoute.Declared
    };

    private BulkCheckoutCommandHandler CartHandler(
        ApplicationDbContext context, CountingAcquirer acquirer, CountingMerchantResolver? merchant = null)
        => new(
            context,
            new StubMonobankClientFactory(acquirer),
            merchant ?? new CountingMerchantResolver(),
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
            new CountingMerchantResolver(),
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
    /// A customer opened to the offer, with one sellable voucher behind the fuel — so the only thing that
    /// decides any answer in this file is how much the account holds.
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
            // Pinned, and pinned to a shape the store never issues: fuelflow_settlement_code() draws from
            // an alphabet with no 0 and no 1, so a code of this form cannot equal a generated one and two
            // tests here cannot equal each other. Left to the generator, every seed is a draw against a
            // unique index.
            SettlementCode = $"PB{Interlocked.Increment(ref _seededUsers):D6}",
            TokenVersion = 1,
            PromoAccessGrantedAtUtc = TestClock.UtcNow,
            CreatedAtUtc = TestClock.UtcNow,
            UpdatedAtUtc = TestClock.UtcNow
        });

        var fuel = await context.FuelTypes.FirstAsync(f => f.Id == ToUuid("okko-95"));
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
            VoucherNumber = $"SHORT-{Guid.NewGuid():N}"[..16],
            QrPayload = $"qr-{Guid.NewGuid():N}",
            Status = VoucherStatus.Available,
            CostPerLiter = CostPerLiter,
            CreatedAtUtc = TestClock.UtcNow,
            UpdatedAtUtc = TestClock.UtcNow
        });

        await context.SaveChangesAsync();
        return userId;
    }

    /// <summary>
    /// Money declared for this customer with no order against it — the balance, exactly as the channel
    /// holds it. The id comes back because which transfer a draw spends is one of the answers here.
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
    /// Money the finance channel placed on one order by hand — the shape that is already counted against
    /// its price, so the question the next tap asks of it has a different answer than of a balance.
    /// </summary>
    private async Task NameMoneyOnAsync(Guid orderId, Guid userId, decimal amount)
    {
        using var context = CreateContext();
        var now = TestClock.UtcNow;

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
            PaidAtUtc = now,
            CreatedByUserName = "finance",
            IsAutomatic = true,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
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

    /// <summary>
    /// A merchant, counting. Declared means no terminal was asked for, so any call here is a tap that
    /// looked for a bank it was never supposed to reach.
    /// </summary>
    private sealed class CountingMerchantResolver : IMonobankMerchantResolver
    {
        private int _calls;

        public int Calls => Volatile.Read(ref _calls);

        public MonobankMerchant Resolve(string? phoneNumber, bool isQaAccount)
        {
            Interlocked.Increment(ref _calls);
            return MonobankMerchant.Live;
        }
    }
}
