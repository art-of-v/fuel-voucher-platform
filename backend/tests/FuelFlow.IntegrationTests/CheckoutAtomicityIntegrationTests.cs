using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using FuelFlow.API.Features.Orders.CreateCheckout.Models;
using FuelFlow.API.Features.Orders.SharedServices.Monobank;
using FuelFlow.API.Features.Orders.SharedServices.Monobank.Models;
using FuelFlow.Features.Orders.CreateCheckout;
using FuelFlow.Features.Orders.SharedModels;
using FuelFlow.Features.Vouchers;
using FuelFlow.Features.Vouchers.SharedModels;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using FuelFlow.SharedKernel.Options;
using FuelFlow.Testing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;
using static FuelFlow.SharedKernel.Domain.FuelIdMapper;

namespace FuelFlow.IntegrationTests;

/// <summary>
/// The checkout money path on the real database, with the acquirer replaced by a recorder.
/// </summary>
/// <remarks>
/// <para>
/// The finding these cover: the invoice was minted BEFORE the order was written, so a failure
/// between the two left a live, payable invoice at the acquirer with no order behind it. The order
/// is now written first, and these tests are what says so.
/// </para>
/// <para>
/// The acquirer double is the point of the exercise. It records what the platform had already
/// committed at the moment it was asked for money, and it can fail AFTER it has accepted the
/// invoice - which is the window the finding is about.
/// </para>
/// </remarks>
[Collection("Integration Tests")]
public class CheckoutAtomicityIntegrationTests : WebApplicationFactory<Program>, IClassFixture<TestDatabaseFixture>
{
    private readonly TestDatabaseFixture _fixture;
    private readonly RecordingAcquirer _acquirer = new();
    private readonly HttpClient _client;
    private string? _accessToken;
    private Guid _userId;

    public CheckoutAtomicityIntegrationTests(TestDatabaseFixture fixture)
    {
        _fixture = fixture;
        _client = CreateClient();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");

        // The acquirer is the only thing swapped out. Everything else - the handlers, the
        // database, the transactions - is the production wiring.
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IMonobankClientFactory>();
            services.AddSingleton<IMonobankClientFactory>(_acquirer);
        });
    }

    // ---------------------------------------------------------------------------------------------
    // The acceptance criterion
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public async Task The_order_is_written_before_the_invoice_is_minted()
    {
        await AuthenticateAsync("+380991234811");
        SeedSellableStock("okko", "okko-95", 50m);

        // The probe runs at the moment the acquirer is asked for money: it counts the orders the
        // platform has already committed. If the order were still only in the change tracker, the
        // count would be zero - and the invoice would be a payable document for a sale nobody
        // has a row for.
        _acquirer.OrderCountProbe = CountPendingPaymentOrdersAsync;

        var response = await _client.PostAsJsonAsync("/api/purchases", CheckoutRequest());

        response.EnsureSuccessStatusCode();
        _acquirer.OrdersCommittedWhenAskedForMoney.Should().HaveCount(1);
        _acquirer.OrdersCommittedWhenAskedForMoney[0].Should().Be(
            1, "the order is committed before the acquirer is asked for money");
    }

    [Fact]
    public async Task A_failed_invoice_leaves_the_order_behind()
    {
        await AuthenticateAsync("+380991234812");
        SeedSellableStock("okko", "okko-95", 50m);

        // The acquirer accepts the invoice and the call then fails: the invoice exists at the
        // acquirer, and the platform never learns its id. This is the window the finding names.
        _acquirer.FailAfterAcceptingTheInvoice = true;

        var response = await _client.PostAsJsonAsync("/api/purchases", CheckoutRequest());

        response.IsSuccessStatusCode.Should().BeFalse(
            "a checkout that cannot be paid is not a successful checkout");

        // The invoice the acquirer accepted has an order behind it: the sale is on record, and
        // the order is the retryable state - written, awaiting an invoice.
        _acquirer.AcceptedInvoiceIds.Should().ContainSingle();
        var orders = await PendingPaymentOrdersAsync();
        orders.Should().ContainSingle("the sale is on record even though its invoice failed");
        orders[0].MonobankInvoiceId.Should().BeNull("the platform never learned the invoice id");
        orders[0].MonobankPaymentUrl.Should().BeNull();
    }

    [Fact]
    public async Task A_retry_after_a_failed_invoice_makes_a_fresh_attempt_that_can_be_paid()
    {
        await AuthenticateAsync("+380991234813");
        SeedSellableStock("okko", "okko-95", 50m);

        _acquirer.FailAfterAcceptingTheInvoice = true;
        await _client.PostAsJsonAsync("/api/purchases", CheckoutRequest());

        _acquirer.FailAfterAcceptingTheInvoice = false;
        var response = await _client.PostAsJsonAsync("/api/purchases", CheckoutRequest());
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<CreateCheckoutResponse>();
        result!.MonobankInvoiceId.Should().NotBeNull("the retry minted an invoice");
        result.PaymentUrl.Should().NotBeNull();

        // The failed attempt gave its basket back and stays as the record of what was attempted;
        // the retry is a new attempt, and that one has the invoice the customer can pay.
        var orders = await PendingPaymentOrdersAsync();
        orders.Should().HaveCount(2, "the failed attempt stays as the record of what was attempted");

        var payable = orders.Single(o => o.MonobankInvoiceId != null);
        payable.Id.Should().Be(result.OrderId);
        payable.MonobankInvoiceId.Should().Be(result.MonobankInvoiceId);
        payable.MonobankPaymentUrl.Should().Be(result.PaymentUrl);
    }

    // ---------------------------------------------------------------------------------------------
    // Fixtures
    // ---------------------------------------------------------------------------------------------

    private async Task<string?> AuthenticateAsync(string phoneNumber)
    {
        var sendCode = await _client.PostAsJsonAsync("/api/auth/send-code", new { phoneNumber });
        sendCode.EnsureSuccessStatusCode();

        var verify = await _client.PostAsJsonAsync("/api/auth/verify", new { phoneNumber, code = "000000" });
        verify.EnsureSuccessStatusCode();

        var result = await verify.Content.ReadFromJsonAsync<JsonElement>();
        var token = result.GetProperty("accessToken").GetString();
        _accessToken = token;
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // The OTP signup leaves the account inactive, and the payment endpoints refuse an inactive
        // account. Activation is a staff operation in production; here the test does it directly.
        using (var scope = _fixture.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var user = await context.Users.FirstAsync(u => u.PhoneNumber == phoneNumber);
            user.IsActive = true;
            user.UpdatedAtUtc = TestClock.UtcNow;
            context.Update(user);
            await context.SaveChangesAsync();
            _userId = user.Id;
        }

        return token;
    }

    /// <summary>
    /// Checkout refuses a package with no activated stock behind it, so every test that expects a
    /// sale needs real <c>Available</c> vouchers under the line it buys.
    /// </summary>
    private void SeedSellableStock(string brandId, string fuelTypeId, decimal liters, int count = 1)
    {
        using var scope = _fixture.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var expiry = DateOnly.FromDateTime(TestClock.UtcNow.AddMonths(6));

        for (var i = 0; i < count; i++)
        {
            context.FuelVouchers.Add(new FuelVoucher
            {
                Id = Guid.CreateVersion7(),
                Brand = brandId,
                BrandId = BrandIds.ForSlug(brandId),
                FuelTypeId = ToUuid(fuelTypeId),
                Liters = liters,
                SupplierExpirationDate = expiry,
                CustomerExpirationDate = expiry,
                VoucherNumber = $"ATOMIC-{brandId}-{fuelTypeId}-{Guid.NewGuid().ToString()[..8]}",
                QrPayload = Guid.NewGuid().ToString(),
                Status = VoucherStatus.Available,
                CreatedAtUtc = TestClock.UtcNow,
                UpdatedAtUtc = TestClock.UtcNow
            });
        }

        context.SaveChanges();
    }

    private static CreateCheckoutCommand CheckoutRequest() => new()
    {
        FuelTypeId = ToUuid("okko-95"),
        Liters = 50,
        Quantity = 1,
        Price = 2500,
        BrandId = BrandIds.Okko,
        BrandName = "OKKO"
    };

    /// <summary>
    /// The pending-payment orders of THIS test's customer. The class shares one cloned database,
    /// so every assertion is scoped to the customer it belongs to.
    /// </summary>
    private async Task<List<Order>> PendingPaymentOrdersAsync()
    {
        await using var context = _fixture.CreateOwnerContext();
        return await context.Orders
            .IgnoreQueryFilters()
            .Where(o => o.Status == OrderStatus.PendingPayment && o.UserId == _userId)
            .OrderBy(o => o.CreatedAtUtc)
            .ToListAsync();
    }

    /// <summary>
    /// The probe looks into the database while the request that owns it is still running. A unit of
    /// work opened in that moment resolves to the anonymous tenant - the request's own scope is
    /// not this scope's scope - and the row-level policies would hide every order from it. The
    /// harness's eye is the owner's, so it sees what is committed.
    /// </summary>
    private async Task<int> CountPendingPaymentOrdersAsync()
    {
        await using var context = _fixture.CreateOwnerContext();
        return await context.Orders
            .IgnoreQueryFilters()
            .CountAsync(o => o.Status == OrderStatus.PendingPayment && o.UserId == _userId);
    }

    /// <summary>
    /// The acquirer double: it records what it accepted, it can see what the platform had already
    /// committed when it was asked, and it can fail after it has already accepted the invoice.
    /// </summary>
    private sealed class RecordingAcquirer : IMonobankClientFactory, IMonobankClient
    {
        /// <summary>
        /// The invoice counter is static on purpose: xUnit builds a new instance of the test class
        /// for every test method, and the class shares ONE database. A per-instance counter would
        /// mint the same invoice id for two different orders - and the platform's unique index on
        /// <c>monobank_invoice_id</c> would (correctly) refuse the second one.
        /// </summary>
        private static int _invoiceCounter;

        /// <summary>Every invoice id this acquirer accepted, in call order.</summary>
        public List<string> AcceptedInvoiceIds { get; } = [];

        /// <summary>
        /// How many pending-payment orders the platform had COMMITTED at the moment the acquirer
        /// was asked for money. One per call.
        /// </summary>
        public List<int> OrdersCommittedWhenAskedForMoney { get; } = [];

        /// <summary>
        /// When set, the acquirer accepts the invoice and the call then fails - the window the
        /// finding is about, where the invoice exists and the platform never hears about it.
        /// </summary>
        public bool FailAfterAcceptingTheInvoice { get; set; }

        /// <summary>The probe the test installs to look into the database mid-call.</summary>
        public Func<Task<int>>? OrderCountProbe { get; set; }

        public IMonobankClient ForMerchant(MonobankMerchant merchant) => this;

        public async Task<MonobankInvoiceResponse> CreateInvoiceAsync(
            MonobankInvoiceRequest request,
            CancellationToken cancellationToken = default)
        {
            if (OrderCountProbe is not null)
            {
                OrdersCommittedWhenAskedForMoney.Add(await OrderCountProbe());
            }

            var invoiceId = $"INV-ATOMIC-{Interlocked.Increment(ref _invoiceCounter):D3}";
            AcceptedInvoiceIds.Add(invoiceId);

            if (FailAfterAcceptingTheInvoice)
            {
                throw new HttpRequestException("the acquirer accepted the invoice and the call failed afterwards");
            }

            return new MonobankInvoiceResponse
            {
                InvoiceId = invoiceId,
                PageUrl = $"https://pay.monobank.test/{invoiceId}",
                AppUrl = $"https://mbnk.app/t/{invoiceId}"
            };
        }

        public Task<MonobankInvoiceStatus> GetInvoiceStatusAsync(string invoiceId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("These tests do not poll invoices.");

        public Task<MonobankCancelResponse> CancelInvoiceAsync(
            string invoiceId,
            long amountKopecks,
            string extRef,
            IReadOnlyList<MonobankBasketItem>? items = null,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException("These tests do not cancel invoices.");

        public Task<IReadOnlyList<MonobankStatementItem>> GetStatementAsync(
            DateTime fromUtc,
            DateTime toUtc,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException("These tests do not read statements.");
    }
}
