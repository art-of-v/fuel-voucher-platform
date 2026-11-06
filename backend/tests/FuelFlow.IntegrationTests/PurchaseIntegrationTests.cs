using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FuelFlow.Features.Orders.CreateCheckout;
using FuelFlow.Features.Orders.SimulatePayment;
using FuelFlow.Features.Orders.SharedModels;
using FuelFlow.Features.Brands;
using FuelFlow.Features.Vouchers;
using FuelFlow.Features.Vouchers.SharedModels;
using FuelFlow.Payments.SharedModels;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using FuelFlow.SharedKernel.DTOs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using FuelFlow.API.Features.Orders.CreateCheckout.Models;
using FuelFlow.Testing;
using static FuelFlow.SharedKernel.Domain.FuelIdMapper;

namespace FuelFlow.IntegrationTests;

[Collection("Integration Tests")]
public class PurchaseIntegrationTests : IClassFixture<TestDatabaseFixture>
{
    private readonly TestDatabaseFixture _fixture;
    private readonly HttpClient _client;
    private string? _accessToken;

    public PurchaseIntegrationTests(TestDatabaseFixture fixture)
    {
        _fixture = fixture;
        _client = _fixture.CreateClient();
    }

    private async Task AuthenticateAsync(string phoneNumber)
    {
        if (_accessToken != null) return;

        // Send code

        // Send code
        var sendCodeResponse = await _client.PostAsJsonAsync("/api/auth/send-code", new { phoneNumber });
        sendCodeResponse.EnsureSuccessStatusCode();

        // Get verification code from database (test helper)
        using var scope = _fixture.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var verificationCode = context.VerificationCodes
            .Where(vc => vc.PhoneNumber == phoneNumber)
            .OrderByDescending(vc => vc.CreatedAtUtc)
            .First();

        // Verify code
        var verifyResponse = await _client.PostAsJsonAsync("/api/auth/verify", new
        {
            phoneNumber,
            code = "000000"
        });

        verifyResponse.EnsureSuccessStatusCode();
        var verifyResult = await verifyResponse.Content.ReadFromJsonAsync<JsonElement>();
        _accessToken = verifyResult.GetProperty("accessToken").GetString();

        using var roleScope = _fixture.Services.CreateScope();
        var roleContext = roleScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var adminRole = await roleContext.Roles.FirstOrDefaultAsync(r => r.Name == "Admin", CancellationToken.None);
        if (adminRole == null)
        {
            adminRole = new Role
            {
                Id = Guid.NewGuid(),
                Name = "Admin",
                CreatedAtUtc = TestClock.UtcNow
            };
            roleContext.Roles.Add(adminRole);
            await roleContext.SaveChangesAsync();
        }

        var user = await roleContext.Users.FirstAsync(u => u.PhoneNumber == phoneNumber, CancellationToken.None);
        user.RoleId = adminRole.Id;
        // OTP verify registers a user as is_active=false; activation is a staff-only operation
        // (see VerifyCodeCommand). Payment endpoints reject inactive accounts with 403, so a
        // purchase test has to perform that staff activation step explicitly, exactly as
        // SetUserActiveCommandHandler would, before the account can check out.
        user.IsActive = true;
        user.UpdatedAtUtc = TestClock.UtcNow;
        roleContext.Update(user);
        await roleContext.SaveChangesAsync();

        // Re-login after role assignment so token contains Admin claim
        _accessToken = null;
        var sendCodeResponse2 = await _client.PostAsJsonAsync("/api/auth/send-code", new { phoneNumber });
        sendCodeResponse2.EnsureSuccessStatusCode();

        var verificationCode2 = roleContext.VerificationCodes
            .Where(vc => vc.PhoneNumber == phoneNumber)
            .OrderByDescending(vc => vc.CreatedAtUtc)
            .First();

        var verifyResponse2 = await _client.PostAsJsonAsync("/api/auth/verify", new
        {
            phoneNumber,
            code = "000000"
        });

        verifyResponse2.EnsureSuccessStatusCode();
        var verifyResult2 = await verifyResponse2.Content.ReadFromJsonAsync<JsonElement>();
        _accessToken = verifyResult2.GetProperty("accessToken").GetString();
    }

    /// <summary>
    /// Checkout refuses a package with no activated stock behind it, so every test that expects a sale
    /// needs real <c>Available</c> vouchers under the line it buys — the same thing an operator does by
    /// importing a PDF, costing the batch and activating it.
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
                FuelTypeId =   ToUuid(fuelTypeId),
                Liters = liters,
                SupplierExpirationDate = expiry,
                CustomerExpirationDate = expiry,
                VoucherNumber = $"STOCK-{brandId}-{fuelTypeId}-{liters}-{Guid.NewGuid().ToString()[..8]}",
                QrPayload = Guid.NewGuid().ToString(),
                Status = VoucherStatus.Available,
                CreatedAtUtc = TestClock.UtcNow,
                UpdatedAtUtc = TestClock.UtcNow
            });
        }

        context.SaveChanges();
    }

    [Fact]
    public async Task CreatePurchase_ShouldReturn200_WithValidRequest()
    {
        // Arrange
        await AuthenticateAsync("+380991234701");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);
        SeedSellableStock("okko", "okko-95", 50m);

        var request = new CreateCheckoutCommand
        {
            
            FuelTypeId = ToUuid("okko-95"),
            Liters = 50,
            Quantity = 1,
            Price = 2500,
            BrandId = BrandIds.Okko,
            BrandName = "OKKO Brand #123"
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/purchases", request);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<CreateCheckoutResponse>();
        Assert.NotNull(result);
        Assert.NotEqual(Guid.Empty, result.OrderId);
        Assert.Equal(OrderStatus.PendingPayment.ToString(), result.Status);
    }

    [Fact]
    public async Task CreatePurchase_ShouldReturn401_WhenNotAuthenticated()
    {
        // Arrange
        var request = new CreateCheckoutCommand
        {
            BrandId = BrandIds.Okko,
            FuelTypeId = ToUuid("okko-95"),
            Liters = 50,
            Quantity = 1,
            Price = 2500
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/purchases", request);

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task CreatePurchase_ShouldReturn400_WithInvalidRequest()
    {
        // Arrange
        await AuthenticateAsync("+380991234702");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);

        var request = new CreateCheckoutCommand
        {
            BrandId = Guid.Empty, // Invalid
            FuelTypeId = ToUuid("okko-95"),
            Liters = 50,
            Quantity = 1,
            Price = 2500
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/purchases", request);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetMyPurchases_ShouldReturnUserOrders()
    {
        // Arrange
        await AuthenticateAsync("+380991234703");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);

        // Create a purchase first
        SeedSellableStock("wog", "wog-95", 20m, count: 2);

        var createRequest = new CreateCheckoutCommand
        {
            
            FuelTypeId = ToUuid("wog-95"),
            Liters = 20,
            Quantity = 2,
            Price = 1800,
            BrandId = BrandIds.Wog
        };

        var createResponse = await _client.PostAsJsonAsync("/api/purchases", createRequest);
        createResponse.EnsureSuccessStatusCode();

        // Act
        var response = await _client.GetAsync("/api/purchases/my");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var purchases = await response.Content.ReadFromJsonAsync<List<PurchaseDto>>();
        Assert.NotNull(purchases);
        Assert.NotEmpty(purchases);
        Assert.Contains(purchases, p => p.Brand == "wog");
    }

    [Fact]
    public async Task SimulatePayment_Success_ShouldMarkOrderAsPending()
    {
        // Arrange
        await AuthenticateAsync("+380991234704");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);

        // Create order
        SeedSellableStock("okko", "okko-dp", 50m);

        var createRequest = new CreateCheckoutCommand
        {
            
            FuelTypeId = ToUuid("okko-dp"),
            Liters = 50,
            Quantity = 1,
            Price = 2000,
            BrandId = BrandIds.Okko
        };

        var createResponse = await _client.PostAsJsonAsync("/api/purchases", createRequest);
        var createResult = await createResponse.Content.ReadFromJsonAsync<CreateCheckoutResponse>();

        // Act
        var simulateRequest = new SimulatePaymentCommand
        {
            OrderId = createResult!.OrderId,
            Scenario = "success"
        };

        var response = await _client.PostAsJsonAsync("/api/purchases/simulate", simulateRequest);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<SimulatePaymentResponse>();
        Assert.NotNull(result);
        Assert.Equal("success", result.Status);
        Assert.NotNull(result.Purchase);
    }

    [Fact]
    public async Task SimulatePayment_Failure_ShouldCancelOrder()
    {
        // Arrange
        await AuthenticateAsync("+380991234705");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);

        // Create order
        SeedSellableStock("okko", "okko-95", 20m);

        var createRequest = new CreateCheckoutCommand
        {
            
            FuelTypeId = ToUuid("okko-95"),
            Liters = 20,
            Quantity = 1,
            Price = 1250,
            BrandId = BrandIds.Okko
        };

        var createResponse = await _client.PostAsJsonAsync("/api/purchases", createRequest);
        var createResult = await createResponse.Content.ReadFromJsonAsync<CreateCheckoutResponse>();

        // Act
        var simulateRequest = new SimulatePaymentCommand
        {
            OrderId = createResult!.OrderId,
            Scenario = "failure"
        };

        var response = await _client.PostAsJsonAsync("/api/purchases/simulate", simulateRequest);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<SimulatePaymentResponse>();
        Assert.NotNull(result);
        Assert.Equal("failed", result.Status);
        Assert.Null(result.Purchase);
    }

    [Fact]
    public async Task Checkout_ShouldCreateOrder()
    {
        // Arrange
        await AuthenticateAsync("+380991234706");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);

        SeedSellableStock("okko", "okko-95", 50m);

        var request = new CreateCheckoutCommand
        {
            
            FuelTypeId = ToUuid("okko-95"),
            Liters = 50,
            Quantity = 1,
            Price = 3000,
            BrandId = BrandIds.Okko
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/purchases", request);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<CreateCheckoutResponse>();
        Assert.NotNull(result);
        Assert.NotEqual(Guid.Empty, result.OrderId);
    }

    /// <summary>
    /// The refusal this epic exists for. A package can be in the catalog, fully priced, and still have
    /// nothing sellable behind it — then the sale must not happen. 409 with a stable code so the app can
    /// translate it; <c>wog-dp</c> is deliberately a combination no other test in this collection seeds.
    /// </summary>
    [Fact]
    public async Task CreatePurchase_ShouldReturn409_WhenPackageHasNoSellableStock()
    {
        // Arrange
        await AuthenticateAsync("+380991234707");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);

        var request = new CreateCheckoutCommand
        {
            
            FuelTypeId = ToUuid("wog-dp"),
            Liters = 10,
            Quantity = 1,
            Price = 500,
            BrandId = BrandIds.Wog
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/purchases", request);

        // Assert
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("package_out_of_stock", body.GetProperty("code").GetString());

        // Money never moved: no order, and therefore no invoice, exists for a refused line.
        using var scope = _fixture.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.False(context.Orders.Any(o => o.LineItems.Any(li => li.FuelTypeId == ToUuid("wog-dp") && o.Status == OrderStatus.PendingPayment)));
    }

    /// <summary>
    /// The catalog keeps every package and says which are buyable. The app relies on this to grey a card
    /// out instead of hiding it, so the flag has to be present on the anonymous route and honest about
    /// both a stocked and an empty nominal.
    /// </summary>
    [Fact]
    public async Task PublicPackages_ShouldFlagAvailability_WithoutHidingTheCatalog()
    {
        // Arrange
        SeedSellableStock("okko", "okko-95", 10m);

        // Act
        var response = await _client.GetAsync("/api/packages");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var packages = await response.Content.ReadFromJsonAsync<List<PublicPackageResponse>>();
        Assert.NotNull(packages);

        var stocked = packages!.Single(p => p.BrandId == BrandIds.Okko && p.FuelTypeId == ToUuid("okko-95") && p.Liters == 10m);
        Assert.True(stocked.IsAvailable, "an activated voucher of exactly this nominal is behind this package");

        var empty = packages.Single(p => p.BrandId == BrandIds.Wog && p.FuelTypeId == ToUuid("wog-dp") && p.Liters == 10m);
        Assert.False(empty.IsAvailable);
    }

    /// <summary>
    /// The second button (planning #889) on the wire rather than in the handler: a body built by a
    /// phone, naming the route it wants, and an answer a phone can read without a converter table.
    /// </summary>
    /// <remarks>
    /// Nothing else in the suite covers this, because the other checkout tests post a typed command
    /// and read a typed response with the same options on both ends — an enum would survive that as a
    /// number and prove nothing. The app sends strings and parses strings, so this sends a string and
    /// reads the raw JSON.
    /// </remarks>
    [Theory]
    [InlineData("Declared", HttpStatusCode.Forbidden, PromoAccessException.Code)]
    [InlineData("Bitcoin", HttpStatusCode.BadRequest, null)]
    public async Task A_Route_Named_By_The_Phone_Is_Heard_Or_Refused(string route, HttpStatusCode expected, string? code)
    {
        await AuthenticateAsync("+380991234711");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);

        var response = await PostRawAsync($$"""
            {
                "brandId": "{{BrandIds.Okko}}",
                "brandName": "OKKO",
                "fuelTypeId": "{{ToUuid("okko-95")}}",
                "liters": 50,
                "quantity": 1,
                "price": 3000,
                "settlementRoute": "{{route}}"
            }
            """);

        Assert.Equal(expected, response.StatusCode);

        if (code is not null)
        {
            // The grant is a column, not a role, so an account nobody opened to the offer is refused
            // whatever it can otherwise do — and it is refused by a code the app can translate.
            var body = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(code, body.GetProperty("code").GetString());
        }
    }

    /// <summary>
    /// Item 5 of planning #889 on the wire. The app's sentence is «Не вистачає 1 500 ₴», in four
    /// languages, so the amount has to arrive as a number and not inside ours: a client that read it out
    /// of a detail string would need a parser per locale, and the first locale to change its thousands
    /// separator would show the customer a shortfall of 1.
    /// </summary>
    [Fact]
    public async Task A_Promo_Checkout_On_A_Short_Account_Answers_409_With_The_Figures()
    {
        const decimal declared = 1000m;

        await AuthenticateAsync("+380991234713");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);

        SeedSellableStock("okko", "okko-95", 50m);

        Guid userId;
        using (var scope = _fixture.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var user = context.Users.Single(u => u.PhoneNumber == "+380991234713");
            user.PromoAccessGrantedAtUtc = TestClock.UtcNow;

            context.Payments.Add(new Payment
            {
                Id = Guid.CreateVersion7(),
                Kind = PaymentKind.Capture,
                Provider = PaymentProviders.Declared,
                UserId = user.Id,
                OrderId = null,
                AmountUah = declared,
                AmountKopecks = (long)Math.Round(declared * 100),
                Currency = "UAH",
                Status = PaymentStatus.Succeeded,
                PaidAtUtc = TestClock.UtcNow,
                CreatedByUserName = "finance",
                IsAutomatic = true,
                CreatedAtUtc = TestClock.UtcNow,
                UpdatedAtUtc = TestClock.UtcNow
            });

            await context.SaveChangesAsync();
            userId = user.Id;
        }

        var response = await PostRawAsync($$"""
            {
                "brandId": "{{BrandIds.Okko}}",
                "brandName": "OKKO",
                "fuelTypeId": "{{ToUuid("okko-95")}}",
                "liters": 50,
                "quantity": 1,
                "price": 2500,
                "settlementRoute": "Declared"
            }
            """);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(PromoBalanceShortException.Code, body.GetProperty("code").GetString());

        var data = body.GetProperty("data");
        var held = data.GetProperty("heldUah").GetDecimal();
        var owed = data.GetProperty("owedUah").GetDecimal();
        var shortfall = data.GetProperty("shortfallUah").GetDecimal();

        Assert.Equal(declared, held);
        Assert.True(owed > held, "the order has to owe more than the account holds, or there is nothing to refuse");
        Assert.True(shortfall == owed - held,
            "the number the customer is shown is the one the balance is short by, not one the message rounded");

        using var verify = _fixture.Services.CreateScope();
        Assert.False(verify.ServiceProvider.GetRequiredService<ApplicationDbContext>()
            .Orders.Any(o => o.UserId == userId), "and the refusal writes no order to explain afterwards");
    }

    /// <summary>
    /// A request that says nothing about a route gets the one every client in the field meant, and is
    /// told so by name. An old bundle ignores the field; a new one does not have to guess from a
    /// missing payment link.
    /// </summary>
    [Fact]
    public async Task A_Checkout_That_Does_Not_Name_A_Route_Answers_With_The_Payment_One()
    {
        await AuthenticateAsync("+380991234712");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);

        SeedSellableStock("okko", "okko-95", 50m);

        var response = await PostRawAsync($$"""
            {
                "brandId": "{{BrandIds.Okko}}",
                "brandName": "OKKO",
                "fuelTypeId": "{{ToUuid("okko-95")}}",
                "liters": 50,
                "quantity": 1,
                "price": 3000
            }
            """);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("Acquirer", body.RootElement.GetProperty("settlementRoute").GetString());
    }

    private Task<HttpResponseMessage> PostRawAsync(string json)
        => _client.PostAsync("/api/purchases",
            new StringContent(json, Encoding.UTF8, "application/json"));
}

