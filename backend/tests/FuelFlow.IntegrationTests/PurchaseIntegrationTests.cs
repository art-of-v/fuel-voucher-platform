using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FuelFlow.Features.Orders.CreateCheckout;
using FuelFlow.Features.Orders.SimulatePayment;
using FuelFlow.Features.Orders.SharedModels;
using FuelFlow.Features.Brands;
using FuelFlow.Features.Vouchers;
using FuelFlow.Features.Vouchers.SharedModels;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using FuelFlow.SharedKernel.DTOs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using FuelFlow.API.Features.Orders.CreateCheckout.Models;
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
                CreatedAtUtc = DateTime.UtcNow
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
        user.UpdatedAtUtc = DateTime.UtcNow;
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
    private void SeedSellableStock(string BrandId, string fuelTypeId, decimal liters, int count = 1)
    {
        using var scope = _fixture.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var expiry = DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(6));

        for (var i = 0; i < count; i++)
        {
            context.FuelVouchers.Add(new FuelVoucher
            {
                Id = Guid.CreateVersion7(),
                Brand = BrandId,
                FuelTypeId =   ToUuid(fuelTypeId),
                Liters = liters,
                SupplierExpirationDate = expiry,
                CustomerExpirationDate = expiry,
                VoucherNumber = $"STOCK-{BrandId}-{fuelTypeId}-{liters}-{Guid.NewGuid().ToString()[..8]}",
                QrPayload = Guid.NewGuid().ToString(),
                Status = VoucherStatus.Available,
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow
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
}

