using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using FuelFlow.Features.Auth.AdminUser.SetUserActive;
using FuelFlow.Features.Orders.SharedModels;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using FuelFlow.SharedKernel.Options;
using FuelFlow.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static FuelFlow.SharedKernel.Domain.FuelIdMapper;

namespace FuelFlow.IntegrationTests;

/// <summary>
/// HTTP full-stack coverage for the per-order receipt (#119).
/// </summary>
/// <remarks>
/// The document itself is covered by unit tests; what only a real request can prove is the access
/// rule. <c>GET /api/purchases/{id}/receipt</c> is scoped to the caller inside the query, so the test
/// that matters is the negative one: Alice asking for Bob's order must not learn whether it exists.
/// A 403 would leak exactly that, so the expected answer is 404.
/// </remarks>
[Collection("Integration Tests")]
public sealed class OrderReceiptIntegrationTests : IClassFixture<TestDatabaseFixture>
{
    private const string AlicePhone = "+380000009001";
    private const string BobPhone = "+380000009002";
    private const string StaffPhone = "+380000009003";

    private readonly TestDatabaseFixture _fixture;

    public OrderReceiptIntegrationTests(TestDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Owner_Downloads_TheirOwnReceipt_AsAPdf()
    {
        await ResetAsync();
        await SeedAsync();

        var (alice, _) = await SignInAsync(AlicePhone);
        var orderId = await SeedOrderForAsync(AlicePhone);

        var response = await alice.GetAsync($"/api/purchases/{orderId}/receipt");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/pdf");

        var bytes = await response.Content.ReadAsByteArrayAsync();
        Encoding.ASCII.GetString(bytes, 0, 5).Should().Be("%PDF-");
    }

    [Fact]
    public async Task A_Customers_Receipt_Is_NotFound_For_Somebody_Eleses_Order()
    {
        await ResetAsync();
        await SeedAsync();

        var (alice, _) = await SignInAsync(AlicePhone);
        var bobsOrder = await SeedOrderForAsync(BobPhone);

        var response = await alice.GetAsync($"/api/purchases/{bobsOrder}/receipt");

        // 404, not 403: a 403 would confirm the order exists and let a caller enumerate ids.
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task An_Order_That_Does_Not_Exist_Is_Indistinguishable_From_Another_Customers()
    {
        await ResetAsync();
        await SeedAsync();

        var (alice, _) = await SignInAsync(AlicePhone);
        var missing = Guid.NewGuid();

        var response = await alice.GetAsync($"/api/purchases/{missing}/receipt");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_Customer_Cannot_Reach_The_Operator_Copy()
    {
        await ResetAsync();
        await SeedAsync();

        var (alice, _) = await SignInAsync(AlicePhone);
        var orderId = await SeedOrderForAsync(AlicePhone);

        var response = await alice.GetAsync($"/api/admin/orders/{orderId}/receipt");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task An_Operator_Downloads_Any_Order_Receipt()
    {
        await ResetAsync();
        await SeedAsync();
        // Promotion has to happen before signing in: the role is baked into the token at issue, so
        // a token minted beforehand still says Customer and the Staff policy rejects it.
        await PromoteToAdminAsync(StaffPhone);

        var (staff, _) = await SignInAsync(StaffPhone);
        var alicesOrder = await SeedOrderForAsync(AlicePhone);

        var response = await staff.GetAsync($"/api/admin/orders/{alicesOrder}/receipt");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var bytes = await response.Content.ReadAsByteArrayAsync();
        Encoding.ASCII.GetString(bytes, 0, 5).Should().Be("%PDF-");
    }

    [Fact]
    public async Task An_Anonymous_Caller_Is_Rejected()
    {
        await ResetAsync();
        await SeedAsync();

        var orderId = await SeedOrderForAsync(AlicePhone);
        var client = _fixture.CreateClient();

        var response = await client.GetAsync($"/api/purchases/{orderId}/receipt");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    private async Task PromoteToAdminAsync(string phone)
    {
        using var scope = _fixture.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var role = await context.Roles.FirstAsync(r => r.Name == AuthOptions.AdminRoleName);
        var user = await context.Users.FirstAsync(u => u.PhoneNumber == phone);
        user.RoleId = role.Id;
        await context.SaveChangesAsync();
    }

    private async Task<(HttpClient Client, Guid UserId)> SignInAsync(string phone)
    {
        var client = _fixture.CreateClient();

        var sendCode = await client.PostAsJsonAsync("/api/auth/send-code", new { phoneNumber = phone });
        sendCode.EnsureSuccessStatusCode();

        var verify = await client.PostAsJsonAsync("/api/auth/verify", new { phoneNumber = phone, code = "000000" });
        verify.EnsureSuccessStatusCode();

        var token = (await verify.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("accessToken").GetString();

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var scope = _fixture.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var userId = await context.Users
            .Where(u => u.PhoneNumber == phone)
            .Select(u => u.Id)
            .SingleAsync();

        return (client, userId);
    }

    private async Task<Guid> SeedOrderForAsync(string phone)
    {
        using var scope = _fixture.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var userId = await context.Users
            .Where(u => u.PhoneNumber == phone)
            .Select(u => u.Id)
            .SingleAsync();

        await EnsureTheFuelExistsAsync(context);

        var order = new Order
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Price = 1000.00m,
            Status = OrderStatus.Fulfilled,
            MonobankInvoiceId = "INV-" + Guid.NewGuid().ToString("N")[..8],
            MonobankStatus = MonobankStatus.Success,
            CreatedAtUtc = TestClock.UtcNow.AddDays(-1),
            UpdatedAtUtc = TestClock.UtcNow
        };

        order.LineItems.Add(new OrderLineItem
        {
            Id = Guid.NewGuid(),
            OrderId = order.Id,
            BrandId = BrandIds.ForSlug("okko"),
            Brand = "okko",
            FuelTypeId = ToUuid("diesel"),
            Liters = 50m,
            Quantity = 2,
            UnitPrice = 500.00m,
            LineTotal = 1000.00m
        });

        context.Orders.Add(order);
        await context.SaveChangesAsync();

        return order.Id;
    }

    /// <summary>
    /// The line item names a fuel, and order_line_items.fuel_type_id is a foreign key now: the fuel
    /// has to be a row, not a uuid typed from memory. The seeded database does not carry this one,
    /// so the fixture makes it.
    /// </summary>
    private static async Task EnsureTheFuelExistsAsync(ApplicationDbContext context)
    {
        var fuelTypeId = ToUuid("diesel");
        if (await context.FuelTypes.AnyAsync(f => f.Id == fuelTypeId))
        {
            return;
        }

        var brandId = await context.Brands.Select(b => (Guid?)b.Id).FirstOrDefaultAsync();
        if (brandId is null)
        {
            brandId = Guid.NewGuid();
            var now = TestClock.UtcNow;
            context.Brands.Add(new Brand
            {
                Id = brandId.Value,
                Slug = "receipt-fixture-" + Guid.NewGuid().ToString("N")[..8],
                Name = "Receipt fixture",
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            });
        }

        var stamp = TestClock.UtcNow;
        context.FuelTypes.Add(new FuelTypeEntity
        {
            Id = fuelTypeId,
            Name = "Дизель (receipt fixture)",
            BrandId = brandId.Value,
            PumpPricePerLiter = 50m,
            CreatedAtUtc = stamp,
            UpdatedAtUtc = stamp
        });
    }

    private async Task SeedAsync()
    {
        // Creates all three users through the real auth endpoint, which is what assigns the default
        // role; SignInAsync then promotes the operator.
        foreach (var phone in new[] { AlicePhone, BobPhone, StaffPhone })
        {
            await SignInAsync(phone);
        }
    }

    private async Task ResetAsync()
    {
        await using var context = _fixture.CreateOwnerContext();
        await context.Database.ExecuteSqlRawAsync(
            """TRUNCATE TABLE "payments", "fulfillments", "orders", "order_line_items", "outbox_events", "fuel_vouchers", "company_invitations", "company_members", "legal_entities", "verification_codes", "users", "provider_event_outbox", "app_settings" RESTART IDENTITY CASCADE""");
    }
}