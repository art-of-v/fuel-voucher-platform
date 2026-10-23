using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using FuelFlow.Features.Auth.Security;
using FuelFlow.Features.Auth.SharedModels;
using FuelFlow.Features.Auth.TwoFactor;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using FuelFlow.SharedKernel.Options;
using FuelFlow.SharedKernel.Security;
using FuelFlow.Testing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FuelFlow.IntegrationTests;

/// <summary>
/// A staff sign-in over HTTP: the first factor stops at a challenge, the second one completes it.
/// </summary>
/// <remarks>
/// <para>
/// The unit tests pin the property; this one pins the wire. What the browser receives matters as
/// much as what the handler decides: a response that carries a challenge and no token, and an
/// endpoint that turns the challenge plus a code into a session.
/// </para>
/// <para>
/// The host runs with <c>Auth:DevBypass</c> off, because DevBypass switches the second factor off
/// with everything else. With no SMS brand configured the composition root resolves
/// <c>FakeSmsService</c>, so no real SMS is sent; the code is seeded in the database instead.
/// </para>
/// </remarks>
[Collection("Integration Tests")]
public sealed class StaffSecondFactorIntegrationTests : IClassFixture<TestDatabaseFixture>
{
    private readonly TestDatabaseFixture _fixture;

    public StaffSecondFactorIntegrationTests(TestDatabaseFixture fixture) => _fixture = fixture;

    /// <summary>
    /// A Development host with the second factor enforced: DevBypass off, and the switch that
    /// Production refuses to start without.
    /// </summary>
    private sealed class StaffSignInFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Auth:DevBypass"] = "false",
                    ["Auth:StaffSecondFactorRequired"] = "true",
                    // Pinned empty so a developer's user-secrets cannot wire a real SMS brand in.
                    ["SmsClub:Token"] = "",
                    ["SmsClub:SenderName"] = "",
                });
            });
        }
    }

    [Fact]
    public async Task The_Admin_Sign_In_Stops_At_The_Second_Factor()
    {
        await using var factory = new StaffSignInFactory();
        var client = factory.CreateClient();
        const string phone = "+380995500401";
        await SeedStaffAsync(phone, SeedRoles.AdminName);
        await SeedCodeAsync(phone, "123456");

        var response = await client.PostAsJsonAsync(
            "/api/auth/admin/verify", new { phoneNumber = phone, code = "123456" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.TryGetProperty("accessToken", out var accessToken).Should().BeTrue();
        accessToken.ValueKind.Should().Be(JsonValueKind.Null, "the first factor alone mints no session");

        var challenge = body.GetProperty("twoFactor");
        challenge.GetProperty("status").GetString().Should().Be("enroll");
        challenge.GetProperty("challenge").GetString().Should().NotBeNullOrWhiteSpace();
        challenge.GetProperty("enrollmentSecret").GetString().Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task The_Second_Factor_Completes_The_Sign_In()
    {
        await using var factory = new StaffSignInFactory();
        var client = factory.CreateClient();
        const string phone = "+380995500402";
        await SeedStaffAsync(phone, SeedRoles.AdminName);
        await SeedCodeAsync(phone, "123456");

        var first = await client.PostAsJsonAsync(
            "/api/auth/admin/verify", new { phoneNumber = phone, code = "123456" });
        first.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await first.Content.ReadFromJsonAsync<JsonElement>();
        var challenge = body.GetProperty("twoFactor");
        var secret = challenge.GetProperty("enrollmentSecret").GetString()!;

        var second = await client.PostAsJsonAsync(
            "/api/auth/two-factor/verify",
            new
            {
                challenge = challenge.GetProperty("challenge").GetString(),
                code = Totp.ComputeCode(secret, TestClock.UtcNow)
            });

        second.StatusCode.Should().Be(HttpStatusCode.OK);
        var session = await second.Content.ReadFromJsonAsync<JsonElement>();
        session.GetProperty("accessToken").GetString().Should().NotBeNullOrWhiteSpace();

        // The session is a staff session: the panel accepts it.
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", session.GetProperty("accessToken").GetString());
        var me = await client.GetAsync("/api/auth/user/me");
        me.StatusCode.Should().Be(HttpStatusCode.OK);
        (await me.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("role").GetString()
            .Should().Be(SeedRoles.AdminName);
    }

    [Fact]
    public async Task The_Shared_Endpoint_Stops_At_The_Second_Factor_Either()
    {
        // The mobile endpoint resolves the same account. Without the second factor here, the
        // admin panel's factor would be a decoration with a door next to it.
        await using var factory = new StaffSignInFactory();
        var client = factory.CreateClient();
        const string phone = "+380995500403";
        await SeedStaffAsync(phone, SeedRoles.ManagerName);
        await SeedCodeAsync(phone, "123456");

        var response = await client.PostAsJsonAsync(
            "/api/auth/verify", new { phoneNumber = phone, code = "123456" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("accessToken").ValueKind.Should().Be(JsonValueKind.Null);
        body.GetProperty("twoFactor").GetProperty("status").GetString().Should().Be("enroll");
    }

    [Fact]
    public async Task A_Wrong_Code_Is_Refused_And_No_Session_Is_Minted()
    {
        await using var factory = new StaffSignInFactory();
        var client = factory.CreateClient();
        const string phone = "+380995500404";
        await SeedStaffAsync(phone, SeedRoles.AdminName);
        await SeedCodeAsync(phone, "123456");

        var first = await client.PostAsJsonAsync(
            "/api/auth/admin/verify", new { phoneNumber = phone, code = "123456" });
        var challenge = (await first.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("twoFactor");

        var second = await client.PostAsJsonAsync(
            "/api/auth/two-factor/verify",
            new
            {
                challenge = challenge.GetProperty("challenge").GetString(),
                code = "000000"
            });

        second.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var userId = await context.Users
            .Where(u => u.PhoneNumber == phone)
            .Select(u => u.Id)
            .FirstAsync();
        (await context.RefreshTokens.CountAsync(rt => rt.UserId == userId)).Should().Be(0);
    }

    [Fact]
    public async Task A_Customer_Sign_In_Is_Unaffected()
    {
        await using var factory = new StaffSignInFactory();
        var client = factory.CreateClient();
        const string phone = "+380995500405";
        await SeedStaffAsync(phone, SeedRoles.UserName);
        await SeedCodeAsync(phone, "123456");

        var response = await client.PostAsJsonAsync(
            "/api/auth/verify", new { phoneNumber = phone, code = "123456" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("accessToken").GetString().Should().NotBeNullOrWhiteSpace();
        body.GetProperty("twoFactor").ValueKind.Should().Be(JsonValueKind.Null,
            "a customer has no second factor to prove");
    }

    // --- helpers ---------------------------------------------------------

    private async Task SeedStaffAsync(string phone, string roleName)
    {
        using var scope = _fixture.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var role = await context.Roles.FirstAsync(r => r.Name == roleName);
        var now = TestClock.UtcNow;

        context.Users.Add(new User
        {
            Id = Guid.NewGuid(),
            PhoneNumber = phone,
            RoleId = role.Id,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            IsActive = true,
            TokenVersion = 1,
        });
        await context.SaveChangesAsync();
    }

    private async Task SeedCodeAsync(string phone, string plainCode)
    {
        using var scope = _fixture.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var now = TestClock.UtcNow;

        context.VerificationCodes.Add(new VerificationCode
        {
            Id = Guid.NewGuid(),
            PhoneNumber = phone,
            Code = OtpCodeHasher.Hash(plainCode, AuthOptions.DevelopmentOtpPepper),
            ExpiresAtUtc = now.AddMinutes(5),
            CreatedAtUtc = now,
            IsUsed = false
        });
        await context.SaveChangesAsync();
    }
}
