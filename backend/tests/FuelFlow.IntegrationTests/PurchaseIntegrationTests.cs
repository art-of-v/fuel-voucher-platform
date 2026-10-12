using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FuelFlow.Features.Orders.CreateCheckout;
using FuelFlow.Features.Orders.SimulatePayment;
using FuelFlow.Features.Orders.SharedModels;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using FuelFlow.SharedKernel.DTOs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using FuelFlow.API.Features.Orders.CreateCheckout.Models;

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

    [Fact]
    public async Task CreatePurchase_ShouldReturn200_WithValidRequest()
    {
        // Arrange
        await AuthenticateAsync("+380991234701");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);

        var request = new CreateCheckoutCommand
        {
            Provider = "okko",
            FuelTypeId = "okko-95",
            Liters = 50,
            Quantity = 1,
            Price = 2500,
            StationId = "okko",
            StationName = "OKKO Station #123"
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
            Provider = "okko",
            FuelTypeId = "okko-95",
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
            Provider = "", // Invalid
            FuelTypeId = "okko-95",
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
        var createRequest = new CreateCheckoutCommand
        {
            Provider = "wog",
            FuelTypeId = "wog-95",
            Liters = 20,
            Quantity = 2,
            Price = 1800,
            StationId = "wog"
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
        Assert.Contains(purchases, p => p.Provider == "wog");
    }

    [Fact]
    public async Task SimulatePayment_Success_ShouldMarkOrderAsPending()
    {
        // Arrange
        await AuthenticateAsync("+380991234704");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);

        // Create order
        var createRequest = new CreateCheckoutCommand
        {
            Provider = "okko",
            FuelTypeId = "okko-dp",
            Liters = 50,
            Quantity = 1,
            Price = 2000,
            StationId = "okko"
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
        var createRequest = new CreateCheckoutCommand
        {
            Provider = "okko",
            FuelTypeId = "okko-95",
            Liters = 20,
            Quantity = 1,
            Price = 1250,
            StationId = "okko"
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

        var request = new CreateCheckoutCommand
        {
            Provider = "okko",
            FuelTypeId = "okko-95",
            Liters = 50,
            Quantity = 1,
            Price = 3000,
            StationId = "okko"
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/purchases", request);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<CreateCheckoutResponse>();
        Assert.NotNull(result);
        Assert.NotEqual(Guid.Empty, result.OrderId);
    }
}

