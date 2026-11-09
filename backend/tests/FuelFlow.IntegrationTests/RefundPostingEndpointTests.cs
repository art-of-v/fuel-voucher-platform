using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using FuelFlow.Features.Brands;
using FuelFlow.Features.Ledger.SharedModels;
using FuelFlow.Features.Orders.RefundOrder;
using FuelFlow.Features.Orders.SettlePaidOrder;
using FuelFlow.Features.Orders.SharedModels;
using FuelFlow.Payments.SharedModels;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using FuelFlow.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using static FuelFlow.SharedKernel.Domain.FuelIdMapper;

namespace FuelFlow.IntegrationTests;

/// <summary>
/// <c>POST /api/admin/ledger/refund-posting</c>, end to end.
/// </summary>
/// <remarks>
/// The poster's behaviour is covered in <see cref="RefundIntegrationTests"/>; these tests are about
/// the second door: a refund the acquirer confirmed but the confirmation path never booked. What
/// matters here is that the door writes the same two legs the confirmation would have, answers a
/// replay with what is booked, refuses a payment that is not a confirmed refund, and belongs to the
/// money capability rather than the read surface.
/// </remarks>
[Collection("Integration Tests")]
public sealed class RefundPostingEndpointTests : IClassFixture<TestDatabaseFixture>
{
    private const decimal Price = 862.00m;
    private const decimal VatOfPrice = 143.67m;

    private readonly TestDatabaseFixture _fixture;

    public RefundPostingEndpointTests(TestDatabaseFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task An_Operator_ReDrives_A_Late_Refund_Posting()
    {
        var (orderId, captureId) = await SeedSettledOrderAsync();
        var refundId = await SeedRefundAsync(orderId, captureId, PaymentStatus.Succeeded);
        var client = await StaffClientAsync();

        var response = await client.PostAsJsonAsync(
            "/api/admin/ledger/refund-posting", new { refundPaymentId = refundId });

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        payload.GetProperty("refundPaymentId").GetGuid().Should().Be(refundId);
        payload.GetProperty("orderId").GetGuid().Should().Be(orderId);
        payload.GetProperty("refundUah").GetDecimal().Should().Be(Price);
        payload.GetProperty("vatReleasedUah").GetDecimal().Should().Be(VatOfPrice,
            "the receipt carved exactly this much VAT, so the return takes all of it back");

        await using var verify = CreateContext();
        var legs = await BookedRefundLegsAsync(verify, refundId);
        legs.Should().HaveCount(2, "a refund is one fact, and it has the same two legs as the receipt it mirrors");
        legs.Should().Contain((RefundJournalPoster.RefundedEvent, "681", "311", Price));
        legs.Should().Contain((RefundJournalPoster.RefundedVatEvent, "643", "704", VatOfPrice));
    }

    [Fact]
    public async Task A_Replay_Is_Answered_With_What_Is_Booked()
    {
        var (orderId, captureId) = await SeedSettledOrderAsync();
        var refundId = await SeedRefundAsync(orderId, captureId, PaymentStatus.Succeeded);
        var client = await StaffClientAsync();

        var first = await client.PostAsJsonAsync(
            "/api/admin/ledger/refund-posting", new { refundPaymentId = refundId });
        first.StatusCode.Should().Be(HttpStatusCode.OK);
        var firstPayload = await first.Content.ReadFromJsonAsync<JsonElement>();

        var replay = await client.PostAsJsonAsync(
            "/api/admin/ledger/refund-posting", new { refundPaymentId = refundId });
        replay.StatusCode.Should().Be(HttpStatusCode.OK);

        var replayPayload = await replay.Content.ReadFromJsonAsync<JsonElement>();
        replayPayload.GetProperty("refundUah").GetDecimal()
            .Should().Be(firstPayload.GetProperty("refundUah").GetDecimal());
        replayPayload.GetProperty("vatReleasedUah").GetDecimal()
            .Should().Be(firstPayload.GetProperty("vatReleasedUah").GetDecimal());

        await using var verify = CreateContext();
        (await BookedRefundLegsAsync(verify, refundId)).Should().HaveCount(2,
            "the key is the refund's id, so the second call is handed the entry that exists");
    }

    [Fact]
    public async Task A_Refund_Already_Booked_Is_Still_ReReadable()
    {
        var (orderId, captureId) = await SeedSettledOrderAsync();
        var refundId = await SeedRefundAsync(orderId, captureId, PaymentStatus.Succeeded);

        // The confirmation path booked it before anybody pressed the button.
        await using (var book = CreateContext())
        {
            var poster = new RefundJournalPoster(
                TestJournal.For(book), book, NullLogger<RefundJournalPoster>.Instance);
            var refund = await book.Payments.AsNoTracking().SingleAsync(p => p.Id == refundId);
            var order = await book.Orders.AsNoTracking().SingleAsync(o => o.Id == orderId);
            await poster.BookConfirmedRefundAsync(refund, order, CancellationToken.None);
            await book.SaveChangesAsync();
        }

        var client = await StaffClientAsync();
        var response = await client.PostAsJsonAsync(
            "/api/admin/ledger/refund-posting", new { refundPaymentId = refundId });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("vatReleasedUah").GetDecimal().Should().Be(VatOfPrice);

        await using var verify = CreateContext();
        (await BookedRefundLegsAsync(verify, refundId)).Should().HaveCount(2,
            "the booking the operator re-reads is the one the poster already made");
    }

    [Fact]
    public async Task A_Customer_Cannot_ReDrive_A_Refund_Posting()
    {
        var (orderId, captureId) = await SeedSettledOrderAsync();
        var refundId = await SeedRefundAsync(orderId, captureId, PaymentStatus.Succeeded);
        var client = await CustomerClientAsync();

        var response = await client.PostAsJsonAsync(
            "/api/admin/ledger/refund-posting", new { refundPaymentId = refundId });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "the door writes money into the books, which is the money capability");
    }

    [Fact]
    public async Task A_Refund_The_Acquirer_Has_Not_Confirmed_Is_A_400()
    {
        var (orderId, captureId) = await SeedSettledOrderAsync();
        var refundId = await SeedRefundAsync(orderId, captureId, PaymentStatus.Pending);
        var client = await StaffClientAsync();

        var response = await client.PostAsJsonAsync(
            "/api/admin/ledger/refund-posting", new { refundPaymentId = refundId });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            "booking money that was only asked back would put the return in the books ahead of the bank");

        await using var verify = CreateContext();
        (await BookedRefundLegsAsync(verify, refundId)).Should().BeEmpty();
    }

    [Fact]
    public async Task A_Capture_Is_A_400()
    {
        var (_, captureId) = await SeedSettledOrderAsync();
        var client = await StaffClientAsync();

        var response = await client.PostAsJsonAsync(
            "/api/admin/ledger/refund-posting", new { refundPaymentId = captureId });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            "only a refund may book the refund event");
    }

    [Fact]
    public async Task A_Refund_That_Names_No_Order_Is_A_400()
    {
        var (orderId, captureId) = await SeedSettledOrderAsync();
        var refundId = await SeedRefundAsync(orderId, captureId, PaymentStatus.Succeeded);

        await using (var detach = CreateContext())
        {
            await detach.Payments
                .Where(p => p.Id == refundId)
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.OrderId, (Guid?)null));
        }

        var client = await StaffClientAsync();
        var response = await client.PostAsJsonAsync(
            "/api/admin/ledger/refund-posting", new { refundPaymentId = refundId });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            "a refund is booked against the sale it returns");
    }

    [Fact]
    public async Task A_Refund_Without_An_Id_Is_A_400()
    {
        var client = await StaffClientAsync();

        var response = await client.PostAsJsonAsync(
            "/api/admin/ledger/refund-posting", new { refundPaymentId = Guid.Empty });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ---------------------------------------------------------------- fixtures

    /// <summary>Money settled, so the receipt carved the VAT the return has to give back.</summary>
    private async Task<(Guid OrderId, Guid CaptureId)> SeedSettledOrderAsync()
    {
        var userId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var lineId = Guid.NewGuid();
        var captureId = Guid.NewGuid();

        await using (var seed = CreateContext())
        {
            seed.Users.Add(new User
            {
                Id = userId,
                PhoneNumber = PhoneOf(userId),
                IsActive = true,
                CreatedAtUtc = TestClock.UtcNow,
                UpdatedAtUtc = TestClock.UtcNow,
            });

            seed.Orders.Add(new Order
            {
                Id = orderId,
                UserId = userId,
                Price = Price,
                Status = OrderStatus.PendingPayment,
                CreatedAtUtc = TestClock.UtcNow,
                UpdatedAtUtc = TestClock.UtcNow,
                LineItems =
                [
                    new OrderLineItem
                    {
                        Id = lineId,
                        OrderId = orderId,
                        BrandId = BrandIds.ForSlug("okko"),
                        Brand = "okko",
                        FuelTypeId = ToUuid("okko-95"),
                        Liters = 10m,
                        Quantity = 1,
                        UnitPrice = Price,
                        LineTotal = Price,
                    },
                ],
            });

            seed.Payments.Add(new Payment
            {
                Id = captureId,
                OrderId = orderId,
                Kind = PaymentKind.Capture,
                Provider = "monobank",
                ProviderInvoiceId = $"INV-{orderId:N}",
                AmountUah = Price,
                AmountKopecks = (int)(Price * 100m),
                Currency = "UAH",
                Status = PaymentStatus.Succeeded,
                PaidAtUtc = TestClock.UtcNow,
                CreatedAtUtc = TestClock.UtcNow,
                UpdatedAtUtc = TestClock.UtcNow,
            });

            await seed.SaveChangesAsync();
        }

        await using (var settle = CreateContext())
        {
            var result = await TestJournal.TrySettleFor(settle).HandleAsync(
                new TrySettlePaidOrderCommand
                {
                    OrderId = orderId,
                    Because = "the test declared the money and attached it",
                });

            result.Settled.Should().BeTrue(result.Reason ?? "the money on the order pays for it");
        }

        return (orderId, captureId);
    }

    /// <summary>
    /// The money went back, and the confirmation path never booked it - the row is already
    /// Succeeded, so the sync will not look at it again.
    /// </summary>
    private async Task<Guid> SeedRefundAsync(Guid orderId, Guid captureId, PaymentStatus status)
    {
        var refundId = Guid.NewGuid();

        await using var late = CreateContext();
        late.Payments.Add(new Payment
        {
            Id = refundId,
            OrderId = orderId,
            Kind = PaymentKind.Refund,
            ParentPaymentId = captureId,
            Provider = "monobank",
            ProviderInvoiceId = $"INV-{orderId:N}",
            AmountUah = Price,
            AmountKopecks = (int)(Price * 100m),
            Currency = "UAH",
            Status = status,
            PaidAtUtc = status == PaymentStatus.Succeeded ? TestClock.UtcNow : null,
            CreatedAtUtc = TestClock.UtcNow,
            UpdatedAtUtc = TestClock.UtcNow,
        });
        await late.SaveChangesAsync();

        return refundId;
    }

    private ApplicationDbContext CreateContext()
        => new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(_fixture.ConnectionString)
            .Options, TestQrCipher.Instance);

    private static async Task<List<(string SourceEvent, string Debit, string Credit, decimal Amount)>>
        BookedRefundLegsAsync(ApplicationDbContext context, Guid refundId)
    {
        var entries = await context.JournalEntries
            .AsNoTracking()
            .Include(e => e.Lines)
            .ThenInclude(l => l.Account)
            .Where(e => e.SourceType == JournalSourceType.Refund && e.SourceId == refundId)
            .ToListAsync();

        return entries
            .Select(e => (
                e.SourceEvent,
                e.Lines.Single(l => l.DebitUah > 0m).Account!.Code,
                e.Lines.Single(l => l.CreditUah > 0m).Account!.Code,
                e.Lines.Sum(l => l.DebitUah)))
            .ToList();
    }

    private async Task<HttpClient> StaffClientAsync()
    {
        var userId = Guid.NewGuid();

        using (var scope = _fixture.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var role = await context.Roles.FirstAsync(r => r.Name == SeedRoles.AdminName);
            var now = TestClock.UtcNow;

            context.Users.Add(new User
            {
                Id = userId,
                PhoneNumber = PhoneOf(userId),
                RoleId = role.Id,
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
                IsActive = true,
                TokenVersion = 1,
            });
            await context.SaveChangesAsync();
        }

        return await AuthenticatedClientAsync(PhoneOf(userId));
    }

    private async Task<HttpClient> CustomerClientAsync()
    {
        var userId = Guid.NewGuid();

        using (var scope = _fixture.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var now = TestClock.UtcNow;

            context.Users.Add(new User
            {
                Id = userId,
                PhoneNumber = PhoneOf(userId),
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
                IsActive = true,
            });
            await context.SaveChangesAsync();
        }

        return await AuthenticatedClientAsync(PhoneOf(userId));
    }

    private static string PhoneOf(Guid userId) => $"+38{userId:N}"[..20];

    private async Task<HttpClient> AuthenticatedClientAsync(string phoneNumber)
    {
        var client = _fixture.CreateClient();

        var sendCode = await client.PostAsJsonAsync("/api/auth/send-code", new { phoneNumber });
        sendCode.EnsureSuccessStatusCode();

        var verify = await client.PostAsJsonAsync("/api/auth/verify", new { phoneNumber, code = "000000" });
        verify.EnsureSuccessStatusCode();
        var token = (await verify.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("accessToken").GetString();

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }
}
