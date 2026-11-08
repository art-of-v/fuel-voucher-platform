using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using FuelFlow.Features.Brands;
using FuelFlow.Features.Orders.SettlePaidOrder;
using FuelFlow.Features.Orders.SharedModels;
using FuelFlow.Features.Settings;
using FuelFlow.Payments.SharedModels;
using FuelFlow.Features.Vouchers;
using FuelFlow.Features.Vouchers.SharedModels;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using FuelFlow.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static FuelFlow.SharedKernel.Domain.FuelIdMapper;

namespace FuelFlow.IntegrationTests;

/// <summary>
/// <c>POST /api/admin/ledger/vat-release</c>, end to end.
/// </summary>
/// <remarks>
/// The service behaviour is covered in <see
/// cref="ASaleDeliveredOutsideThePerimeterReleasesItsVatTests"/>; these tests are about the HTTP edge
/// the correction needs: an order delivered before the release existed still carries its carve, and
/// this is the only door that asks the handler on its behalf. What matters here is that the door
/// posts the same entry the delivery would have, answers a replay with the entry already booked, and
/// belongs to the money capability rather than the read surface.
/// </remarks>
[Collection("Integration Tests")]
public sealed class VatReleaseEndpointTests : IClassFixture<TestDatabaseFixture>
{
    private const decimal UnitPrice = 862.00m;
    private const string ReleaseEvent = ReleaseVatOutsideThePerimeterCommandHandler.ReleaseEvent;

    private readonly TestDatabaseFixture _fixture;

    public VatReleaseEndpointTests(TestDatabaseFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task An_Operator_Releases_The_Vat_A_Pre_Feature_Delivery_Carries()
    {
        var (orderId, _) = await SeedDeliveredOrderOutsideThePerimeterAsync();
        var client = await StaffClientAsync();

        var first = await client.PostAsJsonAsync("/api/admin/ledger/vat-release", new { orderId });
        first.StatusCode.Should().Be(HttpStatusCode.OK);

        var released = await first.Content.ReadFromJsonAsync<JsonElement>();
        released.GetProperty("releasedUah").GetDecimal().Should().Be(287.33m,
            "the carve was 287.33 on 1 724.00 and both units went out on the outside route");

        await using (var verify = CreateContext())
        {
            (await VatStillOwedAsync(verify, orderId)).Should().Be(0m,
                "the liability the receipt asserted does not stand for a supply it was never in");
            (await ReleaseCountAsync(verify, orderId)).Should().Be(1);
        }

        // The correction is keyed the way the delivery keys it, so asking twice is not posting twice.
        var replay = await client.PostAsJsonAsync("/api/admin/ledger/vat-release", new { orderId });
        replay.StatusCode.Should().Be(HttpStatusCode.OK);
        (await replay.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("releasedUah").GetDecimal().Should().Be(287.33m);

        await using var after = CreateContext();
        (await ReleaseCountAsync(after, orderId)).Should().Be(1,
            "the key is the event and the order, so the second call is handed the entry that exists");
    }

    [Fact]
    public async Task A_Customer_Cannot_Release_Vat()
    {
        var (orderId, _) = await SeedDeliveredOrderOutsideThePerimeterAsync();
        var client = await CustomerClientAsync();

        var response = await client.PostAsJsonAsync("/api/admin/ledger/vat-release", new { orderId });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "the correction writes money into the books, which is the money capability");
    }

    [Fact]
    public async Task A_Release_Without_An_Order_Is_A_400()
    {
        var client = await StaffClientAsync();

        var response = await client.PostAsJsonAsync(
            "/api/admin/ledger/vat-release", new { orderId = Guid.Empty });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            "a release that names no order cannot be corrected afterwards");
    }

    // ---------------------------------------------------------------- fixtures

    /// <summary>
    /// The shape the correction exists for: money settled, so the receipt carved the VAT, and the
    /// stock already handed over on the outside route — which is why nothing will ever revisit it.
    /// </summary>
    private async Task<(Guid OrderId, Guid LineId)> SeedDeliveredOrderOutsideThePerimeterAsync()
    {
        var orderId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var lineId = Guid.NewGuid();

        await using (var seed = CreateContext())
        {
            seed.Users.Add(new User
            {
                Id = userId,
                PhoneNumber = $"+38{userId:N}"[..20],
                IsActive = true,
                CreatedAtUtc = TestClock.UtcNow,
                UpdatedAtUtc = TestClock.UtcNow,
            });

            seed.Orders.Add(new Order
            {
                Id = orderId,
                UserId = userId,
                Price = UnitPrice * 2,
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
                        Quantity = 2,
                        UnitPrice = UnitPrice,
                        LineTotal = UnitPrice * 2,
                    },
                ],
            });

            seed.Payments.Add(new Payment
            {
                Id = Guid.NewGuid(),
                OrderId = orderId,
                Kind = PaymentKind.Capture,
                Provider = "monobank",
                ProviderInvoiceId = $"INV-{orderId:N}",
                AmountUah = UnitPrice * 2,
                AmountKopecks = (int)(UnitPrice * 200m),
                Currency = "UAH",
                Status = PaymentStatus.Succeeded,
                PaidAtUtc = TestClock.UtcNow,
                CreatedAtUtc = TestClock.UtcNow,
                UpdatedAtUtc = TestClock.UtcNow,
            });

            await seed.SaveChangesAsync();
        }

        // The receipt's carve, through the settle the money path uses.
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

        // The delivery: two vouchers, both on the route outside the perimeter, recorded against the
        // line. No fulfilment service runs, because the point is the order it never revisits.
        await using (var delivered = CreateContext())
        {
            foreach (var days in new[] { 30, 60 })
            {
                var voucher = Voucher(userId, AcquisitionSources.Partner, days);
                voucher.Status = VoucherStatus.Assigned;
                voucher.OrderId = orderId;
                voucher.AssignedToUserId = userId;

                delivered.FuelVouchers.Add(voucher);
                delivered.Fulfillments.Add(new Fulfillment
                {
                    OrderId = orderId,
                    VoucherId = voucher.Id,
                    LineItemId = lineId,
                    FulfilledAtUtc = voucher.UpdatedAtUtc,
                });
            }

            await delivered.SaveChangesAsync();
        }

        // The status the delivery left: a settlement job will not look at this order again.
        await using (var fulfil = CreateContext())
        {
            await fulfil.Orders
                .Where(o => o.Id == orderId)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(o => o.Status, OrderStatus.Fulfilled)
                    .SetProperty(o => o.FulfilledAtUtc, TestClock.UtcNow));
        }

        return (orderId, lineId);
    }

    private static FuelVoucher Voucher(Guid userId, string route, int dueInDays)
    {
        var nonce = $"{route}-{Guid.NewGuid():N}";
        var due = DateOnly.FromDateTime(TestClock.UtcNow).AddDays(dueInDays);

        return new FuelVoucher
        {
            Id = Guid.NewGuid(),
            Brand = "okko",
            BrandId = BrandIds.ForSlug("okko"),
            FuelTypeId = ToUuid("okko-95"),
            Liters = 10m,
            SupplierExpirationDate = due,
            CustomerExpirationDate = due,
            VoucherNumber = nonce,
            QrPayload = nonce,
            Status = VoucherStatus.Available,
            AcquisitionSource = route,
            CreatedAtUtc = TestClock.UtcNow,
            UpdatedAtUtc = TestClock.UtcNow,
        };
    }

    private ApplicationDbContext CreateContext()
        => new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(_fixture.ConnectionString)
            .Options, TestQrCipher.Instance);

    private static async Task<decimal> VatStillOwedAsync(ApplicationDbContext context, Guid orderId)
        => await (from line in context.JournalLines.AsNoTracking()
                  join entry in context.JournalEntries.AsNoTracking() on line.EntryId equals entry.Id
                  join account in context.ChartOfAccounts.AsNoTracking() on line.AccountId equals account.Id
                  where entry.SourceId == orderId && account.Code == "643"
                  select line.CreditUah - line.DebitUah)
                 .SumAsync();

    private static async Task<int> ReleaseCountAsync(ApplicationDbContext context, Guid orderId)
        => await context.JournalEntries.AsNoTracking()
            .CountAsync(e => e.SourceEvent == ReleaseEvent && e.SourceId == orderId);

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
                PhoneNumber = $"+38{userId:N}"[..20],
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

    private Task<HttpClient> CustomerClientAsync()
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
            context.SaveChanges();
        }

        return AuthenticatedClientAsync(PhoneOf(userId));
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
