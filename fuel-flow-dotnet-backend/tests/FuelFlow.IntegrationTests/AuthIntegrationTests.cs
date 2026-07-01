using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using FuelFlow.Features.Auth.Refresh;
using FuelFlow.Features.Auth.SendCode;
using FuelFlow.Features.Auth.SharedModels;
using FuelFlow.Features.Auth.Verify;
using FuelFlow.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FuelFlow.IntegrationTests;

public class AuthIntegrationTests : WebApplicationFactory<Program>, IClassFixture<TestDatabaseFixture>
{
    private readonly TestDatabaseFixture _fixture;

    public AuthIntegrationTests(TestDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");

        builder.ConfigureServices(services =>
        {
            var contextDescriptor = services.SingleOrDefault(
                d => d.ServiceType == typeof(DbContextOptions<ApplicationDbContext>));
            if (contextDescriptor != null)
            {
                services.Remove(contextDescriptor);
            }

            var dbContextDescriptor = services.SingleOrDefault(
                d => d.ServiceType == typeof(ApplicationDbContext));
            if (dbContextDescriptor != null)
            {
                services.Remove(dbContextDescriptor);
            }

            services.AddDbContext<ApplicationDbContext>(options =>
                options.UseNpgsql(_fixture.DbContainer.GetConnectionString()));
        });
    }

    [Fact]
    public async Task SendCode_ShouldSucceed_WhenPhoneNumberIsValid()
    {
        var client = CreateClient();
        var request = new SendCodeCommand("+12345678901");

        var response = await client.PostAsJsonAsync("/api/auth/send-code", request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<SendCodeResponse>();
        result.Should().NotBeNull();
        result!.Success.Should().BeTrue();

        using var scope = Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var verificationCode = await context.VerificationCodes
            .Where(v => v.PhoneNumber == "+12345678901" && !v.IsUsed)
            .OrderByDescending(v => v.CreatedAtUtc)
            .FirstOrDefaultAsync();

        verificationCode.Should().NotBeNull();
        verificationCode!.Code.Should().Be("000000");
    }

    [Fact]
    public async Task SendCode_ShouldNormalizePhoneNumber()
    {
        var client = CreateClient();
        var request = new SendCodeCommand("(234) 567-8901");

        var response = await client.PostAsJsonAsync("/api/auth/send-code", request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var scope = Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var verificationCode = await context.VerificationCodes
            .Where(v => v.PhoneNumber == "+12345678901")
            .OrderByDescending(v => v.CreatedAtUtc)
            .FirstOrDefaultAsync();

        verificationCode.Should().NotBeNull();
    }

    [Fact]
    public async Task VerifyCode_ShouldSucceed_WhenCodeIsValid()
    {
        var client = CreateClient();
        var phoneNumber = "+15551234567";

        await client.PostAsJsonAsync("/api/auth/send-code", new SendCodeCommand(phoneNumber));

        var verifyRequest = new VerifyCodeCommand(phoneNumber, "000000");
        var response = await client.PostAsJsonAsync("/api/auth/verify", verifyRequest);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<VerifyCodeResponse>();
        result.Should().NotBeNull();
        result!.AccessToken.Should().NotBeNullOrEmpty();
        result.RefreshToken.Should().NotBeNullOrEmpty();
        result.ExpiresIn.Should().BeGreaterThan(0);

        using var scope = Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var user = await context.Users
            .FirstOrDefaultAsync(u => u.PhoneNumber == phoneNumber);

        user.Should().NotBeNull();
        user!.LastLoginAtUtc.Should().NotBeNull();
    }

    [Fact]
    public async Task VerifyCode_ShouldNormalizePhoneNumber()
    {
        var client = CreateClient();
        var normalizedPhone = "+19876543210";

        await client.PostAsJsonAsync("/api/auth/send-code", new SendCodeCommand(normalizedPhone));

        var verifyRequest = new VerifyCodeCommand("(987) 654-3210", "000000");
        var response = await client.PostAsJsonAsync("/api/auth/verify", verifyRequest);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var scope = Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var user = await context.Users
            .FirstOrDefaultAsync(u => u.PhoneNumber == normalizedPhone);

        user.Should().NotBeNull();
    }

    [Fact]
    public async Task VerifyCode_ShouldFail_WhenCodeIsInvalid()
    {
        var client = CreateClient();
        var phoneNumber = "+15559876543";

        await client.PostAsJsonAsync("/api/auth/send-code", new SendCodeCommand(phoneNumber));

        var verifyRequest = new VerifyCodeCommand(phoneNumber, "123456");
        var response = await client.PostAsJsonAsync("/api/auth/verify", verifyRequest);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task VerifyCode_ShouldFail_WhenCodeIsExpired()
    {
        var client = CreateClient();
        var phoneNumber = "+15551112222";

        using (var scope = Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var expiredCode = new VerificationCode
            {
                Id = Guid.NewGuid(),
                PhoneNumber = phoneNumber,
                Code = "000000",
                CreatedAtUtc = DateTime.UtcNow.AddMinutes(-20),
                ExpiresAtUtc = DateTime.UtcNow.AddMinutes(-10),
                IsUsed = false
            };
            context.VerificationCodes.Add(expiredCode);
            await context.SaveChangesAsync();
        }

        var verifyRequest = new VerifyCodeCommand(phoneNumber, "000000");
        var response = await client.PostAsJsonAsync("/api/auth/verify", verifyRequest);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task VerifyCode_ShouldMarkCodeAsUsed_AfterSuccessfulVerification()
    {
        var client = CreateClient();
        var phoneNumber = "+15553334444";

        await client.PostAsJsonAsync("/api/auth/send-code", new SendCodeCommand(phoneNumber));

        var verifyRequest = new VerifyCodeCommand(phoneNumber, "000000");
        await client.PostAsJsonAsync("/api/auth/verify", verifyRequest);

        using var scope = Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var verificationCode = await context.VerificationCodes
            .Where(v => v.PhoneNumber == phoneNumber && v.Code == "000000")
            .OrderByDescending(v => v.CreatedAtUtc)
            .FirstOrDefaultAsync();

        verificationCode.Should().NotBeNull();
        verificationCode!.IsUsed.Should().BeTrue();
        verificationCode.UsedAtUtc.Should().NotBeNull();
    }

    [Fact]
    public async Task RefreshToken_ShouldSucceed_WhenTokenIsValid()
    {
        var client = CreateClient();
        var phoneNumber = "+15556667777";

        await client.PostAsJsonAsync("/api/auth/send-code", new SendCodeCommand(phoneNumber));

        var verifyResponse = await client.PostAsJsonAsync("/api/auth/verify",
            new VerifyCodeCommand(phoneNumber, "000000"));
        var verifyResult = await verifyResponse.Content.ReadFromJsonAsync<VerifyCodeResponse>();

        var refreshRequest = new RefreshTokenCommand(verifyResult!.RefreshToken);
        var response = await client.PostAsJsonAsync("/api/auth/refresh", refreshRequest);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<RefreshTokenResponse>();
        result.Should().NotBeNull();
        result!.AccessToken.Should().NotBeNullOrEmpty();
        result.AccessToken.Should().NotBe(verifyResult.AccessToken);
        result.RefreshToken.Should().NotBeNullOrEmpty();
        result.RefreshToken.Should().NotBe(verifyResult.RefreshToken);
    }

    [Fact]
    public async Task RefreshToken_ShouldFail_WhenTokenIsInvalid()
    {
        var client = CreateClient();
        var refreshRequest = new RefreshTokenCommand("invalid-refresh-token");

        var response = await client.PostAsJsonAsync("/api/auth/refresh", refreshRequest);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task RefreshToken_ShouldFail_WhenTokenIsRevoked()
    {
        var client = CreateClient();
        var phoneNumber = "+15558889999";

        await client.PostAsJsonAsync("/api/auth/send-code", new SendCodeCommand(phoneNumber));

        var verifyResponse = await client.PostAsJsonAsync("/api/auth/verify",
            new VerifyCodeCommand(phoneNumber, "000000"));
        var verifyResult = await verifyResponse.Content.ReadFromJsonAsync<VerifyCodeResponse>();

        using (var scope = Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var refreshToken = await context.RefreshTokens
                .FirstOrDefaultAsync(rt => rt.Token == verifyResult!.RefreshToken);

            refreshToken.Should().NotBeNull();
            refreshToken!.IsRevoked = true;
            refreshToken.RevokedAtUtc = DateTime.UtcNow;
            await context.SaveChangesAsync();
        }

        var refreshRequest = new RefreshTokenCommand(verifyResult!.RefreshToken);
        var response = await client.PostAsJsonAsync("/api/auth/refresh", refreshRequest);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task RefreshToken_ShouldRevokeOldToken_AfterSuccessfulRefresh()
    {
        var client = CreateClient();
        var phoneNumber = "+15551231234";

        await client.PostAsJsonAsync("/api/auth/send-code", new SendCodeCommand(phoneNumber));

        var verifyResponse = await client.PostAsJsonAsync("/api/auth/verify",
            new VerifyCodeCommand(phoneNumber, "000000"));
        var verifyResult = await verifyResponse.Content.ReadFromJsonAsync<VerifyCodeResponse>();

        await client.PostAsJsonAsync("/api/auth/refresh",
            new RefreshTokenCommand(verifyResult!.RefreshToken));

        using var scope = Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var oldRefreshToken = await context.RefreshTokens
            .FirstOrDefaultAsync(rt => rt.Token == verifyResult.RefreshToken);

        oldRefreshToken.Should().NotBeNull();
        oldRefreshToken!.IsRevoked.Should().BeTrue();
        oldRefreshToken.RevokedAtUtc.Should().NotBeNull();
    }

    [Fact]
    public async Task AuthFlow_ShouldWorkEndToEnd_WithPhoneNormalization()
    {
        var client = CreateClient();

        var sendCodeResponse = await client.PostAsJsonAsync("/api/auth/send-code",
            new SendCodeCommand("(555) 999-8888"));
        sendCodeResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var verifyResponse = await client.PostAsJsonAsync("/api/auth/verify",
            new VerifyCodeCommand("555-999-8888", "000000"));
        verifyResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var verifyResult = await verifyResponse.Content.ReadFromJsonAsync<VerifyCodeResponse>();

        var refreshResponse = await client.PostAsJsonAsync("/api/auth/refresh",
            new RefreshTokenCommand(verifyResult!.RefreshToken));
        refreshResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var refreshResult = await refreshResponse.Content.ReadFromJsonAsync<RefreshTokenResponse>();

        refreshResult.Should().NotBeNull();
        refreshResult!.AccessToken.Should().NotBeNullOrEmpty();
    }
}
