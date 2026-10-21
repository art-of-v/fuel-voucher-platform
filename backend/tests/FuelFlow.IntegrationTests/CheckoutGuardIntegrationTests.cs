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

namespace FuelFlow.IntegrationTests;

/// <summary>
/// The checkout guard, against a real PostgreSQL: one basket, one order awaiting payment, one
/// invoice.
/// </summary>
/// <remarks>
/// <para>
/// The defect this covers was a race, and a race is not a thing a unit test can hold still: the
/// handler read "is there a pending order for this basket?", found none, and asked the acquirer
/// for an invoice - and a second request for the same basket, arriving at the same moment, read
/// the same nothing and asked for a second invoice. Two invoices for one basket is a customer
/// charged twice for one tap, and the mobile client makes the second request on its own: its
/// <c>fetchWithRetry</c> resends the identical POST when the first one times out.
/// </para>
/// <para>
/// So the test sends two requests for one basket at the same time, against the same database, and
/// counts what the acquirer was asked for. The count is the assertion that matters: one invoice,
/// not two. The database's own refusals do the rest.
/// </para>
/// </remarks>
[Collection("Integration Tests")]
public sealed class CheckoutGuardIntegrationTests : IClassFixture<TestDatabaseFixture>
{
    private const decimal Liters = 10m;
    private const int Quantity = 1;
    private const decimal CostPerLiter = 30m;
    private const decimal PricePerLiter = 90m;

    private readonly TestDatabaseFixture _fixture;

    public CheckoutGuardIntegrationTests(TestDatabaseFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task TwoParallelPurchases_OfOneBasket_MakeOneOrderAndOneInvoice()
    {
        var userId = await SeedAsync();

        // The acquirer holds the invoice until the test lets it go. That is what makes this a
        // regression test and not a description of the happy path: the first request is inside the
        // acquirer, with its order already written, and the second request is arriving while the
        // first is still running - which is exactly the moment the old code asked for a second
        // invoice.
        var acquirer = new CountingAcquirer { HoldInvoice = true };
        var command = new CreateCheckoutCommand
        {
            UserId = userId,
            BrandId = BrandIds.Okko,
            BrandName = "okko",
            FuelTypeId = ToUuid("okko-95"),
            Liters = Liters,
            Quantity = Quantity,
            Price = PricePerLiter * Liters
        };

        using var first = CreateContext();
        using var second = CreateContext();

        var one = Task.Run(() => PurchaseHandler(first, acquirer).HandleAsync(command));
        var two = Task.Run(() => PurchaseHandler(second, acquirer).HandleAsync(command));

        await acquirer.Asked.WaitAsync(TimeSpan.FromSeconds(30));
        // Long enough for the second request to reach the database and be refused by the index.
        await Task.Delay(500);
        acquirer.Release();

        var results = await Task.WhenAll(one, two);

        // One order, one invoice - and both callers were handed the same one.
        results[0].OrderId.Should().Be(results[1].OrderId,
            "two requests for one basket are one order");
        results[0].MonobankInvoiceId.Should().Be(results[1].MonobankInvoiceId,
            "and they are handed the same invoice, so the customer is charged once");
        acquirer.InvoicesCreated.Should().Be(1,
            "the acquirer is asked once: the request that loses the race never reaches it");

        using var verify = CreateContext();
        var orders = await verify.Orders.Where(o => o.UserId == userId).ToListAsync();
        orders.Should().ContainSingle();
        orders[0].Status.Should().Be(OrderStatus.PendingPayment);

        // The invoice is recorded once, on the order that won.
        (await verify.Payments.CountAsync(p => p.OrderId == orders[0].Id)).Should().Be(1);
    }

    [Fact]
    public async Task TwoParallelCarts_OfOneBasket_MakeOneOrderAndOneInvoice()
    {
        var userId = await SeedAsync();

        var acquirer = new CountingAcquirer { HoldInvoice = true };
        var command = new BulkCheckoutCommand
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

        using var first = CreateContext();
        using var second = CreateContext();

        var one = Task.Run(() => CartHandler(first, acquirer).HandleAsync(command));
        var two = Task.Run(() => CartHandler(second, acquirer).HandleAsync(command));

        await acquirer.Asked.WaitAsync(TimeSpan.FromSeconds(30));
        await Task.Delay(500);
        acquirer.Release();

        var results = await Task.WhenAll(one, two);

        results[0].OrderIds.Single().Should().Be(results[1].OrderIds.Single(),
            "two carts with the same lines are one basket");
        results[0].MonobankInvoiceId.Should().Be(results[1].MonobankInvoiceId);
        acquirer.InvoicesCreated.Should().Be(1,
            "the acquirer is asked once: the request that loses the race never reaches it");

        using var verify = CreateContext();
        (await verify.Orders.CountAsync(o => o.UserId == userId)).Should().Be(1);
    }

    /// <summary>
    /// A purchase for the same basket AFTER the first one is paid is a second purchase, and it
    /// gets its own invoice. The guard holds a basket only while the order awaits payment.
    /// </summary>
    [Fact]
    public async Task ARepeatPurchase_AfterTheFirstOneIsPaid_GetsItsOwnInvoice()
    {
        var userId = await SeedAsync();

        var acquirer = new CountingAcquirer();
        var command = new CreateCheckoutCommand
        {
            UserId = userId,
            BrandId = BrandIds.Okko,
            BrandName = "okko",
            FuelTypeId = ToUuid("okko-95"),
            Liters = Liters,
            Quantity = Quantity,
            Price = PricePerLiter * Liters
        };

        using (var first = CreateContext())
        {
            var response = await PurchaseHandler(first, acquirer).HandleAsync(command);

            using var pay = CreateContext();
            var order = await pay.Orders.FirstAsync(o => o.Id == response.OrderId);
            order.Status = OrderStatus.PendingFulfillment;
            await pay.SaveChangesAsync();
        }

        using var second = CreateContext();
        var repeat = await PurchaseHandler(second, acquirer).HandleAsync(command);

        acquirer.InvoicesCreated.Should().Be(2,
            "a paid order gives its basket back: buying the same fuel again is a new purchase");
        repeat.OrderId.Should().NotBe(Guid.Empty);
    }

    /// <summary>
    /// The customer who abandons a checkout must be able to buy the same fuel again. The abandoned
    /// order keeps everything it was - its price, its status, its invoice - and only gives the
    /// basket back.
    /// </summary>
    [Fact]
    public async Task ANewAttempt_AfterAnAbandonedOrder_GetsItsOwnInvoice()
    {
        var userId = await SeedAsync();

        var acquirer = new CountingAcquirer();
        var command = new CreateCheckoutCommand
        {
            UserId = userId,
            BrandId = BrandIds.Okko,
            BrandName = "okko",
            FuelTypeId = ToUuid("okko-95"),
            Liters = Liters,
            Quantity = Quantity,
            Price = PricePerLiter * Liters
        };

        using (var first = CreateContext())
        {
            var response = await PurchaseHandler(first, acquirer).HandleAsync(command);

            // The customer walks away: the order stays pending, but it is old enough that the next
            // attempt is a new purchase rather than a retry.
            using var abandon = CreateContext();
            var order = await abandon.Orders.FirstAsync(o => o.Id == response.OrderId);
            order.CreatedAtUtc = TestClock.UtcNow.AddHours(-2);
            await abandon.SaveChangesAsync();
        }

        using var second = CreateContext();
        var again = await PurchaseHandler(second, acquirer).HandleAsync(command);

        acquirer.InvoicesCreated.Should().Be(2,
            "an abandoned order does not hold the basket for ever");
        again.OrderId.Should().NotBe(Guid.Empty);
    }

    private CreateCheckoutCommandHandler PurchaseHandler(ApplicationDbContext context, CountingAcquirer acquirer)
        => new(
            context,
            new StubMonobankClientFactory(acquirer),
            new SingleMerchantResolver(),
            Options.Create(new MonobankOptions
            {
                Token = "test_token",
                WebhookUrl = "https://test.local/webhook",
                RedirectUrl = "https://test.local/redirect",
                BaseUrl = "https://api.test.local",
                Enabled = false
            }),
            NullLogger<CreateCheckoutCommandHandler>.Instance,
            new FuelFlowMetrics(),
            NotificationDispatcher.Disabled,
            Stock(context));

    private BulkCheckoutCommandHandler CartHandler(ApplicationDbContext context, CountingAcquirer acquirer)
        => new(
            context,
            new StubMonobankClientFactory(acquirer),
            new SingleMerchantResolver(),
            Options.Create(new MonobankOptions
            {
                Token = "test_token",
                WebhookUrl = "https://test.local/webhook",
                RedirectUrl = "https://test.local/redirect",
                BaseUrl = "https://api.test.local",
                Enabled = false
            }),
            new RuntimeSettingsService(context),
            NullLogger<BulkCheckoutCommandHandler>.Instance,
            NotificationDispatcher.Disabled,
            Stock(context));

    private PackageStockAvailability Stock(ApplicationDbContext context)
        => new(context, new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["VoucherExpiration:Enabled"] = "true" })
            .Build());

    /// <summary>
    /// A customer, a fuel that may be sold, and stock behind it - the three things a checkout
    /// needs before it will create an order.
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
            IsDeleted = false,
            TokenVersion = 1,
            CreatedAtUtc = TestClock.UtcNow,
            UpdatedAtUtc = TestClock.UtcNow
        });

        // The fuel names the price, so a cost and a margin are what make it sellable: without a
        // cost the below-cost guard refuses the sale as "cannot tell".
        var fuel = await context.FuelTypes.FirstAsync(f => f.Id == ToUuid("okko-95"));
        fuel.SupplierPricePerLiter = CostPerLiter;
        fuel.MarginUahPerLiter = PricePerLiter - CostPerLiter;
        fuel.UpdatedAtUtc = TestClock.UtcNow;

        // The size has to exist in the seller's pool, or the package cannot be resolved.
        var pool = DataKinds.ForPool(isTestAccount: false);
        var nominalExists = await context.FuelNominals
            .AnyAsync(n => n.FuelTypeId == fuel.Id && n.DataKind == pool && n.Liters == Liters);
        if (!nominalExists)
        {
            context.FuelNominals.Add(new FuelNominal
            {
                FuelTypeId = fuel.Id,
                DataKind = pool,
                Liters = Liters
            });
        }

        // The stock behind the line. The summary row is written by the database's own trigger, so
        // it cannot disagree with the voucher.
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
            VoucherNumber = $"GUARD-{Guid.NewGuid():N}"[..16],
            QrPayload = $"qr-{Guid.NewGuid():N}",
            Status = VoucherStatus.Available,
            CostPerLiter = CostPerLiter,
            CreatedAtUtc = TestClock.UtcNow,
            UpdatedAtUtc = TestClock.UtcNow
        });

        await context.SaveChangesAsync();
        return userId;
    }

    private ApplicationDbContext CreateContext()
        => new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(_fixture.ConnectionString)
            .Options, TestQrCipher.Instance);

    /// <summary>
    /// The acquirer, counting the invoices it was asked for. The count is the whole point of the
    /// test: two requests for one basket must not become two invoices.
    /// </summary>
    private sealed class CountingAcquirer : IMonobankClient
    {
        private readonly TaskCompletionSource _asked = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _invoicesCreated;

        /// <summary>
        /// When set, the acquirer holds the invoice until the test releases it - so the second
        /// request arrives while the first is still inside the acquirer.
        /// </summary>
        public bool HoldInvoice { get; init; }

        public int InvoicesCreated => Volatile.Read(ref _invoicesCreated);

        /// <summary>Completes when the acquirer has been asked for an invoice.</summary>
        public Task Asked => _asked.Task;

        /// <summary>Lets the held invoice go.</summary>
        public void Release() => _release.TrySetResult();

        public async Task<MonobankInvoiceResponse> CreateInvoiceAsync(
            MonobankInvoiceRequest request,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _invoicesCreated);
            _asked.TrySetResult();

            if (HoldInvoice)
                await _release.Task.WaitAsync(cancellationToken);

            var invoiceId = $"MOCK_{Guid.NewGuid():N}";
            return new MonobankInvoiceResponse
            {
                InvoiceId = invoiceId,
                PageUrl = $"https://mock-monobank.local/invoice/{invoiceId}"
            };
        }

        public Task<MonobankInvoiceStatus> GetInvoiceStatusAsync(
            string invoiceId,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new MonobankInvoiceStatus { InvoiceId = invoiceId, Status = "created" });

        public Task<MonobankCancelResponse> CancelInvoiceAsync(
            string invoiceId,
            long amountKopecks,
            string extRef,
            IReadOnlyList<MonobankBasketItem>? items = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new MonobankCancelResponse { Status = "processing" });

        public Task<IReadOnlyList<MonobankStatementItem>> GetStatementAsync(
            DateTime fromUtc,
            DateTime toUtc,
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<MonobankStatementItem>>([]);
    }

    /// <summary>Every account buys at the live merchant in this test; the routing is covered elsewhere.</summary>
    private sealed class SingleMerchantResolver : IMonobankMerchantResolver
    {
        public MonobankMerchant Resolve(string? phoneNumber, bool isQaAccount) => MonobankMerchant.Live;
    }
}
