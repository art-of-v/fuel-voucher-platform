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
            .Should().Be(1, "the held money is now the order's payment");

        (await context.OutboxEvents.AsNoTracking().CountAsync(e => e.EventType == OutboxEventType.OrderCreated))
            .Should().Be(1, "the settled order has to reach the job that claims stock for it");

        (await context.JournalEntries.AsNoTracking().Where(j => j.SourceId == order.Id)
                .Select(j => j.SourceEvent).ToListAsync())
            .Should().BeEquivalentTo(["payment.received", "payment.received.vat"],
                "money that bought something is revenue from this moment, not a liability");
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
    }

    [Fact]
    public async Task Two_payments_of_that_amount_are_not_guessed_between()
    {
        var userId = await SeedAsync();
        await SeedHeldMoneyAsync(userId, BasketTotal);
        await SeedHeldMoneyAsync(userId, BasketTotal);

        var acquirer = new CountingAcquirer();
        using var context = CreateContext();

        var response = await CartHandler(context, acquirer).HandleAsync(CartCommand(userId));

        response.AlreadyPaid.Should().BeFalse();
        acquirer.InvoicesCreated.Should().Be(1);

        (await context.Payments.AsNoTracking().CountAsync(p => p.OrderId == null))
            .Should().Be(2, "which of the two transfers pays for this order is a person's decision, not a default");
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

    private static BulkCheckoutCommand CartCommand(Guid userId) => new()
    {
        UserId = userId,
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
    /// A customer with stock behind a sellable fuel — the three things a checkout needs. The amount
    /// the test holds money for is always this basket's total, so a settle here is the rule matching
    /// and not a coincidence of price.
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
            VoucherNumber = $"HELD-{Guid.NewGuid():N}"[..16],
            QrPayload = $"qr-{Guid.NewGuid():N}",
            Status = VoucherStatus.Available,
            CostPerLiter = CostPerLiter,
            CreatedAtUtc = TestClock.UtcNow,
            UpdatedAtUtc = TestClock.UtcNow
        });

        await context.SaveChangesAsync();
        return userId;
    }

    /// <summary>Money declared for a customer with no order against it — exactly what the channel holds.</summary>
    private async Task SeedHeldMoneyAsync(Guid userId, decimal amount)
    {
        using var context = CreateContext();
        var now = TestClock.UtcNow;

        context.Payments.Add(new Payment
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

    private sealed class SingleMerchantResolver : IMonobankMerchantResolver
    {
        public MonobankMerchant Resolve(string? phoneNumber, bool isQaAccount) => MonobankMerchant.Live;
    }
}
