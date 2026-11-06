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
using Npgsql;
using Xunit;
using static FuelFlow.SharedKernel.Domain.FuelIdMapper;

namespace FuelFlow.IntegrationTests.Orders;

/// <summary>
/// The second button (planning #889): an order written on the declared route, and what happens to
/// the basket when the two buttons are pressed against each other.
/// </summary>
/// <remarks>
/// <para>
/// The route is decided by a field in a body the customer sends, so everything the flow is supposed
/// to guarantee is only guaranteed here: that the acquirer is never asked about a declared order,
/// that an account nobody opened to the offer cannot take that route, and that one basket ends up
/// with one live order whichever button came second. The last of those is the interesting one — two
/// orders for one cart is not two attempts, it is two ways to pay for the same fuel, and one of them
/// has no bank in it.
/// </para>
/// <para>
/// These drive the real handlers against a real PostgreSQL, with the acquirer counting. A unit test
/// cannot show that the declared order wrote no invoice, because "no invoice" is the database's own
/// answer: <c>ck_order_declared_route_asks_no_acquirer</c> refuses the row.
/// </para>
/// </remarks>
[Collection("Integration Tests")]
public sealed class DeclaredRouteCheckoutTests : IClassFixture<TestDatabaseFixture>, IAsyncLifetime
{
    private const decimal Liters = 10m;
    private const int Quantity = 1;
    private const decimal CostPerLiter = 30m;
    private const decimal PricePerLiter = 90m;
    private const decimal BasketTotal = PricePerLiter * Liters * Quantity;

    private readonly TestDatabaseFixture _fixture;

    /// <summary>Names each seeded customer's settlement code, so no two of them are the same string.</summary>
    private static int _seededUsers;

    public DeclaredRouteCheckoutTests(TestDatabaseFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync()
    {
        using var context = CreateContext();
        await PaymentTestReset.RunAsync(context);
        TestJournal.SeedChart(context);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task APromoCheckout_WritesTheOrder_AndTheAcquirerIsNeverNamed()
    {
        var userId = await SeedAsync(granted: true);

        var acquirer = new CountingAcquirer();
        var merchant = new CountingMerchantResolver();
        using var context = CreateContext();

        var response = await PurchaseHandler(context, acquirer, merchant)
            .HandleAsync(PurchaseCommand(userId, SettlementRoute.Declared));

        response.SettlementRoute.Should().Be("Declared",
            "the answer has to say which button this order belongs to, or the app reads no payment "
             + "url as a payment that failed");
        response.PaymentUrl.Should().BeNull("there is no payment page on this route");
        response.AlreadyPaid.Should().BeFalse("nothing held money for this cart");

        acquirer.InvoicesCreated.Should().Be(0, "an invoice is a request to be charged, and this customer did not");
        merchant.Calls.Should().Be(0, "there is no terminal to choose when no terminal is asked");

        var order = await context.Orders.AsNoTracking().SingleAsync(o => o.Id == response.OrderId);
        order.SettlementRoute.Should().Be(SettlementRoute.Declared);
        order.Status.Should().Be(OrderStatus.PendingPayment, "the order waits for the channel, it is not paid yet");
        order.MonobankInvoiceId.Should().BeNull();
        order.MonobankPaymentUrl.Should().BeNull();
        order.MonobankMerchant.Should().BeNull();
        order.CheckoutKey.Should().NotBeNull("the order holds its basket while it waits");

        (await context.Payments.AsNoTracking().CountAsync(p => p.OrderId == order.Id))
            .Should().Be(0, "no money has moved; the order is a request, not a receipt");
    }

    [Fact]
    public async Task ACartOnThePromoRoute_WritesOneOrder_AndAnswersWithItsRoute()
    {
        var userId = await SeedAsync(granted: true);

        var acquirer = new CountingAcquirer();
        using var context = CreateContext();

        var response = await CartHandler(context, acquirer)
            .HandleAsync(CartCommand(userId, SettlementRoute.Declared));

        acquirer.InvoicesCreated.Should().Be(0);

        var order = await context.Orders.AsNoTracking().SingleAsync(o => o.Id == response.OrderIds.Single());
        order.SettlementRoute.Should().Be(SettlementRoute.Declared);
        response.SettlementRoute.Should().Be("Declared");

        (await context.Orders.AsNoTracking().CountAsync(o => o.UserId == userId))
            .Should().Be(1, "a cart is one order whatever route it is on");
    }

    /// <summary>
    /// The hole this slice exists to close: the button is a field in a request, so without the grant
    /// check any customer could order fuel with the acquirer never seeing the sale.
    /// </summary>
    [Fact]
    public async Task ACustomerNobodyGranted_IsRefused_AndLeavesNothingBehind()
    {
        var userId = await SeedAsync(granted: false);

        var acquirer = new CountingAcquirer();
        using var context = CreateContext();

        var act = () => PurchaseHandler(context, acquirer)
            .HandleAsync(PurchaseCommand(userId, SettlementRoute.Declared));

        await act.Should().ThrowAsync<PromoAccessException>();

        using var verify = CreateContext();
        (await verify.Orders.AsNoTracking().CountAsync(o => o.UserId == userId))
            .Should().Be(0, "a refused tap writes no order");
        acquirer.InvoicesCreated.Should().Be(0, "and asks nothing of the bank either");
    }

    [Fact]
    public async Task ACartFromAnAccountNobodyGranted_IsRefused_TheSameWay()
    {
        var userId = await SeedAsync(granted: false);

        using var context = CreateContext();
        var act = () => CartHandler(context, new CountingAcquirer())
            .HandleAsync(CartCommand(userId, SettlementRoute.Declared));

        await act.Should().ThrowAsync<PromoAccessException>();
    }

    /// <summary>
    /// The same tap, on the route every account has always had, is not held to the promotion's rules.
    /// </summary>
    [Fact]
    public async Task AnAccountNobodyGranted_StillBuysThroughTheBank()
    {
        var userId = await SeedAsync(granted: false);

        var acquirer = new CountingAcquirer();
        using var context = CreateContext();

        var response = await PurchaseHandler(context, acquirer)
            .HandleAsync(PurchaseCommand(userId, SettlementRoute.Acquirer));

        response.SettlementRoute.Should().Be("Acquirer");
        acquirer.InvoicesCreated.Should().Be(1,
            "a gate on the offer that also stopped the payment route would be a way to lose a sale");
    }

    /// <summary>
    /// The retry of a promo checkout — the phone's own resend of the same POST after a timeout. An
    /// acquirer retry waits for the invoice of the attempt still running; a declared order never gets
    /// one, so the same read would end in a 20-second timeout that answers nothing.
    /// </summary>
    [Fact]
    public async Task ARetryOfAPromoCheckout_GivesTheSameOrderBack_WithoutWaitingForAnInvoice()
    {
        var userId = await SeedAsync(granted: true);

        var acquirer = new CountingAcquirer();

        using var first = CreateContext();
        var written = await PurchaseHandler(first, acquirer)
            .HandleAsync(PurchaseCommand(userId, SettlementRoute.Declared));

        var clock = System.Diagnostics.Stopwatch.StartNew();
        using var second = CreateContext();
        var again = await PurchaseHandler(second, acquirer)
            .HandleAsync(PurchaseCommand(userId, SettlementRoute.Declared));
        clock.Stop();

        again.OrderId.Should().Be(written.OrderId, "one cart is one order");
        again.SettlementRoute.Should().Be("Declared");
        acquirer.InvoicesCreated.Should().Be(0, "not now, not on the retry");

        clock.Elapsed.Should().BeLessThan(CheckoutGuard.InvoiceWait,
            "the wait is for an invoice this route never mints");
    }

    /// <summary>
    /// The customer who taps pay after tapping the offer. The declared order is not mid-transaction —
    /// no invoice was ever asked, so no payment page is open anywhere — and the new tap means it.
    /// </summary>
    [Fact]
    public async Task ThePaymentButtonTakesOverABasketHeldByAPromoOrder()
    {
        var userId = await SeedAsync(granted: true);

        var acquirer = new CountingAcquirer();

        using var first = CreateContext();
        var promo = await PurchaseHandler(first, acquirer)
            .HandleAsync(PurchaseCommand(userId, SettlementRoute.Declared));

        using var second = CreateContext();
        var bank = await PurchaseHandler(second, acquirer)
            .HandleAsync(PurchaseCommand(userId, SettlementRoute.Acquirer));

        bank.OrderId.Should().NotBe(promo.OrderId, "the second tap is a different way to pay for the cart");
        bank.SettlementRoute.Should().Be("Acquirer");
        acquirer.InvoicesCreated.Should().Be(1, "the promo order never asked, and this one does");

        using var verify = CreateContext();
        var givenUp = await verify.Orders.AsNoTracking().SingleAsync(o => o.Id == promo.OrderId);
        givenUp.Status.Should().Be(OrderStatus.Cancelled,
            "the order the customer changed their mind about is kept, and given up");
        givenUp.CheckoutKey.Should().BeNull("and it holds the basket no more");

        (await verify.Orders.AsNoTracking().CountAsync(
                o => o.UserId == userId && o.Status == OrderStatus.PendingPayment))
            .Should().Be(1, "one live order for one cart, whichever button came second");
    }

    /// <summary>
    /// The same give-up asked of the helper itself, with the order handed over the way a caller that
    /// read it detached would hand it over. Assigning to such an instance is a silent no-op — the trap
    /// <c>NoTrackingMutationGuardTests</c> exists for, and it can only see one file at a time, while
    /// the file that writes is the one that would have had to escape it.
    /// </summary>
    [Fact]
    public async Task AGivenUpPromoOrder_IsCancelled_WhenTheCallerReadItWithoutTracking()
    {
        var userId = await SeedAsync(granted: true);

        using var context = CreateContext();
        var promo = await PurchaseHandler(context, new CountingAcquirer())
            .HandleAsync(PurchaseCommand(userId, SettlementRoute.Declared));

        using var detached = CreateContext();
        var pending = await detached.Orders.AsNoTracking().FirstAsync(o => o.Id == promo.OrderId);

        var (state, order) = await CheckoutRoutes.SettleBasketAsync(
            detached, pending, SettlementRoute.Acquirer, TestClock.UtcNow, CancellationToken.None);

        state.Should().Be(CheckoutRoutes.BasketState.Free);
        order.Should().BeNull();

        using var verify = CreateContext();
        var givenUp = await verify.Orders.AsNoTracking().SingleAsync(o => o.Id == promo.OrderId);
        givenUp.Status.Should().Be(OrderStatus.Cancelled,
            "the give-up reaches the row whatever the caller did to load it");
        givenUp.CheckoutKey.Should().BeNull("and the basket is free for the tap that replaced it");
    }

    /// <summary>
    /// The customer who taps the offer after tapping pay, while the bank is still being asked. Here
    /// the order in the way may already have a payment page open on another device, so the platform
    /// refuses instead of choosing.
    /// </summary>
    [Fact]
    public async Task ThePromoButtonRefusesABasketTheBankIsStillAskedAbout()
    {
        var userId = await SeedAsync(granted: true);

        var acquirer = new CountingAcquirer();

        using var first = CreateContext();
        var bank = await PurchaseHandler(first, acquirer)
            .HandleAsync(PurchaseCommand(userId, SettlementRoute.Acquirer));

        using var second = CreateContext();
        var act = () => PurchaseHandler(second, acquirer)
            .HandleAsync(PurchaseCommand(userId, SettlementRoute.Declared));

        var refused = (await act.Should().ThrowAsync<CheckoutAlreadyInPaymentException>()).Which;

        refused.OrderId.Should().Be(bank.OrderId, "the refusal names what is in the way, for the log");

        using var verify = CreateContext();
        var held = await verify.Orders.AsNoTracking().SingleAsync(o => o.Id == bank.OrderId);
        held.Status.Should().Be(OrderStatus.PendingPayment);
        held.SettlementRoute.Should().Be(SettlementRoute.Acquirer,
            "and the order the customer paid an invoice against is exactly as it was");
        acquirer.InvoicesCreated.Should().Be(1, "the refused tap asks the acquirer for nothing");
    }

    /// <summary>
    /// A declared order that the finance channel has already named money on. Cancelling it because a
    /// finger moved to the other button would dissolve a row that real money points at.
    /// </summary>
    [Fact]
    public async Task APromoOrderWithMoneyNamedOnItIsNotGivenUpForTheBank()
    {
        var userId = await SeedAsync(granted: true);

        var acquirer = new CountingAcquirer();

        using var first = CreateContext();
        var promo = await PurchaseHandler(first, acquirer)
            .HandleAsync(PurchaseCommand(userId, SettlementRoute.Declared));

        await NameMoneyOnAsync(promo.OrderId, userId, BasketTotal);

        using var second = CreateContext();
        var act = () => PurchaseHandler(second, acquirer)
            .HandleAsync(PurchaseCommand(userId, SettlementRoute.Acquirer));

        await act.Should().ThrowAsync<CheckoutAlreadyInPaymentException>();

        using var verify = CreateContext();
        var held = await verify.Orders.AsNoTracking().SingleAsync(o => o.Id == promo.OrderId);
        held.Status.Should().Be(OrderStatus.PendingPayment, "money is against it; a person decides what happens next");
        acquirer.InvoicesCreated.Should().Be(0, "and the refusal means no second charge is offered for the same fuel");
    }

    /// <summary>
    /// A declared order cannot expire through the invoice job — it has no invoice to expire — so the
    /// basket guard is the only thing that gives an abandoned one back. Without it, one forgotten tap
    /// would hold a cart for ever.
    /// </summary>
    [Fact]
    public async Task AnAbandonedPromoCheckout_GivesTheBasketBack()
    {
        var userId = await SeedAsync(granted: true);

        var acquirer = new CountingAcquirer();

        Guid abandoned;
        using (var first = CreateContext())
        {
            var response = await PurchaseHandler(first, acquirer)
                .HandleAsync(PurchaseCommand(userId, SettlementRoute.Declared));
            abandoned = response.OrderId;
        }

        using (var age = CreateContext())
        {
            var order = await age.Orders.FirstAsync(o => o.Id == abandoned);
            order.CreatedAtUtc = TestClock.UtcNow.AddHours(-2);
            await age.SaveChangesAsync();
        }

        using var second = CreateContext();
        var again = await PurchaseHandler(second, acquirer)
            .HandleAsync(PurchaseCommand(userId, SettlementRoute.Declared));

        again.OrderId.Should().NotBe(abandoned, "an abandoned order does not hold the basket for ever");

        using var verify = CreateContext();
        (await verify.Orders.AsNoTracking().SingleAsync(o => o.Id == abandoned))
            .Status.Should().Be(OrderStatus.PendingPayment,
                "giving the basket back is not cancelling; the old order is still payable by its own channel");
    }

    [Fact]
    public async Task TheRouteOfAnOrderThatExistsCannotBeRewritten()
    {
        var userId = await SeedAsync(granted: true);

        using var context = CreateContext();
        var response = await PurchaseHandler(context, new CountingAcquirer())
            .HandleAsync(PurchaseCommand(userId, SettlementRoute.Acquirer));

        using var verify = CreateContext();

        // The database, not the handler: the switch is reachable from any code path that loads an
        // order, including the ones written next month, and both directions move money that is
        // already in motion.
        var act = async () => await verify.Database.ExecuteSqlRawAsync(
            "UPDATE orders SET settlement_route = 'Declared' WHERE id = {0}", response.OrderId);

        (await act.Should().ThrowAsync<PostgresException>())
            .Which.SqlState.Should().Be("P0001",
                "the trigger's own raise, and only the route raises it: the state machine, not a typo");

        // The same row, updated in every way the ordinary paths update it. Only the route is theirs.
        var order = await verify.Orders.FirstAsync(o => o.Id == response.OrderId);
        order.UpdatedAtUtc = TestClock.UtcNow;
        order.IdempotencyKey = "an update that does not mention the route";
        await verify.SaveChangesAsync();
    }

    [Fact]
    public async Task APromoOrderCannotCarryAnInvoice()
    {
        var userId = await SeedAsync(granted: true);

        using var context = CreateContext();
        var response = await PurchaseHandler(context, new CountingAcquirer())
            .HandleAsync(PurchaseCommand(userId, SettlementRoute.Declared));

        using var verify = CreateContext();
        var act = async () => await verify.Database.ExecuteSqlRawAsync(
            "UPDATE orders SET monobank_invoice_id = {0} WHERE id = {1}", "MOCK_invoice", response.OrderId);

        (await act.Should().ThrowAsync<PostgresException>())
            .Which.SqlState.Should().Be("23514",
                "a declared order with an invoice is the double charge this constraint exists to prevent");
    }

    private static CreateCheckoutCommand PurchaseCommand(Guid userId, SettlementRoute route) => new()
    {
        UserId = userId,
        BrandId = BrandIds.Okko,
        BrandName = "okko",
        FuelTypeId = ToUuid("okko-95"),
        Liters = Liters,
        Quantity = Quantity,
        Price = PricePerLiter * Liters,
        SettlementRoute = route
    };

    private static BulkCheckoutCommand CartCommand(Guid userId, SettlementRoute route) => new()
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

    private BulkCheckoutCommandHandler CartHandler(ApplicationDbContext context, CountingAcquirer acquirer)
        => new(
            context,
            new StubMonobankClientFactory(acquirer),
            new CountingMerchantResolver(),
            Options.Create(MonobankOptions()),
            new RuntimeSettingsService(context),
            NullLogger<BulkCheckoutCommandHandler>.Instance,
            NotificationDispatcher.Disabled,
            Stock(context),
            new FuelFlowMetrics(),
            TestJournal.HeldMoneyFor(context));

    private CreateCheckoutCommandHandler PurchaseHandler(
        ApplicationDbContext context, CountingAcquirer acquirer, CountingMerchantResolver? merchant = null)
        => new(
            context,
            new StubMonobankClientFactory(acquirer),
            merchant ?? new CountingMerchantResolver(),
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
    /// A customer — opened to the offer or not — with stock behind a sellable fuel. Every test gets
    /// its own, so the basket of one is never the basket of another.
    /// </summary>
    private async Task<Guid> SeedAsync(bool granted)
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
            // Pinned, and pinned to a shape the store never issues: fuelflow_settlement_code() draws
            // from an alphabet with no 0 and no 1, so a code of this form cannot equal a generated one
            // and two tests in this class cannot equal each other. Left to the generator, every seed
            // here is a draw against a unique index.
            SettlementCode = $"PR{Interlocked.Increment(ref _seededUsers):D6}",
            TokenVersion = 1,
            PromoAccessGrantedAtUtc = granted ? TestClock.UtcNow : null,
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
            VoucherNumber = $"PROMO-{Guid.NewGuid():N}"[..16],
            QrPayload = $"qr-{Guid.NewGuid():N}",
            Status = VoucherStatus.Available,
            CostPerLiter = CostPerLiter,
            CreatedAtUtc = TestClock.UtcNow,
            UpdatedAtUtc = TestClock.UtcNow
        });

        await context.SaveChangesAsync();
        return userId;
    }

    /// <summary>Money the finance channel has already named on an order — the state no tap may undo.</summary>
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
    /// A merchant, counting. Declared means no terminal was asked for, and the resolution is the step
    /// that would have picked one — a QA account sent to the live merchant by a route that has no
    /// merchant is exactly the mistake this counts.
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
