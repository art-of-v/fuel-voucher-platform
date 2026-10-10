using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using FuelFlow.Features.Auth.SendCode;
using FuelFlow.Features.Auth.Verify;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace FuelFlow.IntegrationTests;

/// <summary>
/// Pins the one control standing between a cross-site page and a minted access token.
///
/// The refresh cookie is httpOnly, Secure and <c>SameSite=None</c> - <c>None</c> because the admin
/// SPA is a different site from the API, so <c>Strict</c>/<c>Lax</c> would break sign-in outright. The
/// consequence is that the browser attaches that cookie to *any* cross-site request, and the Origin
/// allow-list in <c>AuthController.Refresh</c> is therefore the only thing rejecting a hostile page.
///
/// It works, verified manually against production on 2026-10-09. Nothing asserted it, which is the
/// same shape of risk as the three bugs this repo shipped in a row with no test able to see them
/// (#927, #929, #930): the failure mode is silent and the only symptom is an incident.
///
/// The assertion that matters is the first one - a hostile Origin *carrying a valid cookie*. Without
/// the cookie the guard would pass trivially on the empty-token path and the test would prove nothing.
/// </summary>
[Collection("Integration Tests")]
public sealed class RefreshOriginGuardIntegrationTests : IClassFixture<TestDatabaseFixture>
{
    private const string AllowedOrigin = "https://app.palne.shop";
    private const string HostileOrigin = "https://evil.example";
    private const string RefreshCookieName = "refresh_token";

    public RefreshOriginGuardIntegrationTests(TestDatabaseFixture fixture)
    {
        // Constructing the fixture starts Postgres and publishes Database__ConnectionString,
        // which the host below needs before it boots.
        _ = fixture;
    }

    /// <summary>
    /// A host with a known allow-list. DevBypass stays on so the OTP code is the well-known
    /// dev code, which is what lets <see cref="MintRefreshTokenAsync"/> get a real token cheaply;
    /// it does not weaken the guard under test, which reads only Cors:AllowedOrigins.
    /// </summary>
    private sealed class OriginFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Cors:AllowedOrigins:0"] = AllowedOrigin
                });
            });
        }
    }

    /// <summary>Signs in through the OTP flow and returns a genuinely valid refresh token.</summary>
    private async Task<string> MintRefreshTokenAsync(HttpClient client)
    {
        var phone = $"+1555{DateTime.UtcNow.Ticks % 10_000_000:0000000}";
        await client.PostAsJsonAsync("/api/auth/send-code", new SendCodeCommand(phone));

        var verified = await client.PostAsJsonAsync(
            "/api/auth/verify", new VerifyCodeCommand(phone, "000000"));
        verified.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await verified.Content.ReadFromJsonAsync<VerifyCodeResponse>();
        result!.RefreshToken.Should().NotBeNullOrEmpty();
        return result.RefreshToken!;
    }

    private static HttpRequestMessage RefreshRequest(string? origin, string? cookieToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/refresh");
        request.Headers.TryAddWithoutValidation("Origin", origin);
        if (cookieToken is not null)
            request.Headers.Add("Cookie", $"{RefreshCookieName}={cookieToken}");
        return request;
    }

    private static async Task<string> ReadMessageAsync(HttpResponseMessage response)
    {
        var raw = await response.Content.ReadAsStringAsync();
        // The responses are anonymous objects; a substring assertion is enough and keeps this
        // test from coupling to a serialization detail.
        return raw;
    }

    [Fact]
    public async Task Refresh_WithHostileOrigin_AndValidCookie_IsRejectedAndIssuesNoToken()
    {
        using var factory = new OriginFactory();
        using var client = factory.CreateClient();
        var refreshToken = await MintRefreshTokenAsync(client);

        var response = await client.SendAsync(RefreshRequest(HostileOrigin, refreshToken));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await ReadMessageAsync(response)).Should().Contain("Origin not allowed");
        // No token handed back, and none rotated: the request must not have reached the
        // refresh logic at all.
        response.Headers.Should().NotContain(h => h.Key.Equals("Set-Cookie", StringComparison.OrdinalIgnoreCase));
        (await response.Content.ReadAsStringAsync()).Should().NotContain("accessToken");
    }

    [Fact]
    public async Task Refresh_WithNullOrigin_IsRejected()
    {
        using var factory = new OriginFactory();
        using var client = factory.CreateClient();

        // "null" is what a browser sends for a sandboxed iframe, a data: page or a
        // cross-origin redirect. It is not an empty string and must not read as "no origin".
        var response = await client.SendAsync(RefreshRequest("null", cookieToken: null));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await ReadMessageAsync(response)).Should().Contain("Origin not allowed");
    }

    [Fact]
    public async Task Refresh_WithAllowedOrigin_GetsPastTheOriginGuard()
    {
        using var factory = new OriginFactory();
        using var client = factory.CreateClient();

        // No token at all, so the request cannot succeed - but which rejection it hits is the
        // assertion. "Refresh token is required" means the Origin guard passed it; "Origin not
        // allowed" would mean the allow-list itself is broken and every test above is passing
        // for the wrong reason.
        var response = await client.SendAsync(RefreshRequest(AllowedOrigin, cookieToken: null));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ReadMessageAsync(response)).Should().Contain("Refresh token is required");
        (await ReadMessageAsync(response)).Should().NotContain("Origin not allowed");
    }

    [Fact]
    public async Task Refresh_WithAllowedOrigin_AndValidCookie_IssuesANewAccessToken()
    {
        using var factory = new OriginFactory();
        using var client = factory.CreateClient();
        var refreshToken = await MintRefreshTokenAsync(client);

        var response = await client.SendAsync(RefreshRequest(AllowedOrigin, refreshToken));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Contain("accessToken");
    }

    [Fact]
    public async Task Refresh_WithNoOrigin_IsUnaffected()
    {
        using var factory = new OriginFactory();
        using var client = factory.CreateClient();

        // Deliberate, and currently unasserted anywhere: a native app or curl sends no Origin at
        // all, and blocking those would break the mobile sign-in. Pinning it means a later
        // tightening of this guard has to be a decision rather than an accident.
        var response = await client.SendAsync(RefreshRequest(origin: null, cookieToken: null));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ReadMessageAsync(response)).Should().Contain("Refresh token is required");
    }

    [Fact]
    public async Task RefreshLogout_WithHostileOrigin_IsRejected()
    {
        using var factory = new OriginFactory();
        using var client = factory.CreateClient();
        var refreshToken = await MintRefreshTokenAsync(client);

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/refresh/logout");
        request.Headers.TryAddWithoutValidation("Origin", HostileOrigin);
        request.Headers.Add("Cookie", $"{RefreshCookieName}={refreshToken}");

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await response.Content.ReadAsStringAsync()).Should().Contain("Origin not allowed");
    }
}