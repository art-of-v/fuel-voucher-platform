using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using FuelFlow.Features.Auth.SharedModels;
using FuelFlow.Features.Auth.SharedServices;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using FuelFlow.SharedKernel.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FuelFlow.IntegrationTests;

/// <summary>
/// Planning #739: a native sign-in used to mint two refresh tokens — the throwaway the first
/// factor hands back, and the device-bound one the signed challenge mints — and the app keeps
/// only the second. Nothing revoked the first, so every sign-in left a live, device-less row
/// behind for its full 14 days and the "active sessions" count grew with each login.
/// </summary>
/// <remarks>
/// The throwaway is recognized by what the client DECLARES (<c>X-Client-Platform: mobile</c>),
/// not by the User-Agent fallback: build 236 sends no header, never reaches the signed challenge,
/// and keeps the token it gets at /verify as its session — so that one must stay a Session token.
/// </remarks>
[Collection("Integration Tests")]
public sealed class TheThrowawayRefreshTokenDoesNotOutliveTheBind : IClassFixture<TestDatabaseFixture>
{
    private readonly TestDatabaseFixture _fixture;

    public TheThrowawayRefreshTokenDoesNotOutliveTheBind(TestDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task A_Device_Sign_In_Leaves_One_Live_Refresh_Token()
    {
        const string phone = "+380995500031";
        using var client = _fixture.CreateClient();

        var (accessToken, throwaway) = await VerifyAsync(client, phone, native: true);
        throwaway.Should().NotBeNullOrEmpty("the native client gets its first token in the body");

        var userId = await UserIdAsync(phone);

        var pending = await TokensAsync(userId);
        pending.Should().ContainSingle("a fresh account has exactly the token this sign-in minted");
        pending[0].Purpose.Should().Be(RefreshTokenPurpose.DeviceBinding);
        pending[0].Token.Should().Be(SecretsHasher.Hash(throwaway));
        pending[0].ExpiresAtUtc.Should().BeCloseTo(
            DateTime.UtcNow.Add(RefreshTokenPurposePolicy.DeviceBindingWindow),
            TimeSpan.FromMinutes(2),
            "an abandoned sign-in must not leave a 14-day token behind");

        // The rest of the app's sign-in: prove the device, then sign the challenge.
        var deviceId = $"dev-{Guid.NewGuid():N}";
        using var key = RSA.Create(2048);
        await SeedDeviceAsync(userId, deviceId, key.ExportSubjectPublicKeyInfoPem());
        var deviceToken = await BindDeviceAsync(client, deviceId, key);
        deviceToken.Should().NotBeNullOrEmpty();

        var after = await TokensAsync(userId);
        after.Should().HaveCount(2);

        var replaced = after.Single(t => t.DeviceId is null);
        replaced.IsRevoked.Should().BeTrue("the token the client discarded must not outlive the bind");
        replaced.RevokedAtUtc.Should().NotBeNull();

        var live = after.Where(t => !t.IsRevoked).ToList();
        live.Should().ContainSingle("one sign-in leaves one live session");
        live[0].DeviceId.Should().Be(deviceId);
        live[0].Purpose.Should().Be(RefreshTokenPurpose.Session);
        live[0].Token.Should().Be(SecretsHasher.Hash(deviceToken));
    }

    [Fact]
    public async Task A_Browser_Session_Survives_A_Device_Bind_On_The_Same_Account()
    {
        const string phone = "+380995500032";
        using var client = _fixture.CreateClient();

        // The browser signs in without the platform header: its token IS the session, in a cookie.
        var browserToken = await VerifyInBrowserAsync(client, phone);
        browserToken.Should().NotBeNullOrEmpty();

        var userId = await UserIdAsync(phone);
        var browserRow = (await TokensAsync(userId)).Single();
        browserRow.Purpose.Should().Be(RefreshTokenPurpose.Session, "a browser keeps the token it is given");
        browserRow.ExpiresAtUtc.Should().BeAfter(
            DateTime.UtcNow.AddDays(1),
            "a browser session is not the throwaway the signed challenge replaces");

        // The same account now completes a signed sign-in on a phone.
        var (accessToken, throwaway) = await VerifyAsync(client, phone, native: true);
        accessToken.Should().NotBeNullOrEmpty();
        throwaway.Should().NotBeNullOrEmpty();

        var deviceId = $"dev-{Guid.NewGuid():N}";
        using var key = RSA.Create(2048);
        await SeedDeviceAsync(userId, deviceId, key.ExportSubjectPublicKeyInfoPem());
        await BindDeviceAsync(client, deviceId, key);

        var after = await TokensAsync(userId);

        after.Single(t => t.Token == SecretsHasher.Hash(browserToken)).IsRevoked.Should().BeFalse(
            "revoking the throwaway must not sign the browser panel out");
        after.Single(t => t.Token == SecretsHasher.Hash(throwaway)).IsRevoked.Should().BeTrue();

        after.Where(t => !t.IsRevoked).Should().HaveCount(2, "the browser session and the new device session");
    }

    private async Task<(string? AccessToken, string? RefreshToken)> VerifyAsync(
        HttpClient client,
        string phoneNumber,
        bool native)
    {
        var sendCode = await client.PostAsJsonAsync("/api/auth/send-code", new { phoneNumber });
        sendCode.EnsureSuccessStatusCode();

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/verify")
        {
            Content = JsonContent.Create(new { phoneNumber, code = "000000" })
        };
        if (native)
            request.Headers.Add("X-Client-Platform", "mobile");

        var verify = await client.SendAsync(request);
        verify.EnsureSuccessStatusCode();

        using var document = JsonDocument.Parse(await verify.Content.ReadAsStringAsync());
        var root = document.RootElement;
        return (root.GetProperty("accessToken").GetString(), root.GetProperty("refreshToken").GetString());
    }

    /// <summary>The refresh token as a browser receives it: in the Set-Cookie header, not the body.</summary>
    private async Task<string> VerifyInBrowserAsync(HttpClient client, string phoneNumber)
    {
        var sendCode = await client.PostAsJsonAsync("/api/auth/send-code", new { phoneNumber });
        sendCode.EnsureSuccessStatusCode();

        var verify = await client.PostAsJsonAsync("/api/auth/verify", new { phoneNumber, code = "000000" });
        verify.EnsureSuccessStatusCode();

        var setCookie = verify.Headers.TryGetValues("Set-Cookie", out var cookies)
            ? string.Join("; ", cookies)
            : throw new Xunit.Sdk.XunitException("the browser must get the refresh token in a cookie");

        var pair = setCookie.Split(';')[0];
        pair.Should().StartWith("refresh_token=");
        return pair["refresh_token=".Length..];
    }

    /// <summary>
    /// Enrols the device row the signed challenge verifies against. The scheme is left unpinned
    /// on purpose, so the verifier tries every one - the app's real registration pins it, and this
    /// test is about the token bookkeeping, not about which algorithm the key produced.
    /// </summary>
    private async Task SeedDeviceAsync(Guid userId, string deviceId, string publicKeyPem)
    {
        using var scope = _fixture.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        context.Devices.Add(new Device
        {
            Id = Guid.CreateVersion7(),
            UserId = userId,
            DeviceId = deviceId,
            PublicKey = publicKeyPem,
            SignaturePayloadVersion = DeviceSignatureVerifier.Version2,
            Status = DeviceStatus.Active,
            CreatedAt = DateTime.UtcNow,
            LastSeenAt = DateTime.UtcNow,
        });
        await context.SaveChangesAsync();
    }

    private static async Task<string> BindDeviceAsync(HttpClient client, string deviceId, RSA key)
    {
        var challengeRequest = new HttpRequestMessage(HttpMethod.Post, "/api/auth/device/challenge")
        {
            Content = JsonContent.Create(new { deviceId })
        };
        challengeRequest.Headers.Add("X-Client-Platform", "mobile");

        var challengeResponse = await client.SendAsync(challengeRequest);
        challengeResponse.EnsureSuccessStatusCode();

        using var challengeDocument = JsonDocument.Parse(await challengeResponse.Content.ReadAsStringAsync());
        var challenge = challengeDocument.RootElement.GetProperty("challenge").GetString()!;

        var payload = DeviceSignatureVerifier.BuildChallengePayload(deviceId, challenge, DeviceSignatureVerifier.Version2);
        var signature = Convert.ToBase64String(key.SignData(
            Encoding.UTF8.GetBytes(payload),
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1));

        // The app sends its platform header on every request; without it the endpoint answers in
        // the browser shape and keeps the refresh token in the (nonexistent) cookie.
        var verifyRequest = new HttpRequestMessage(HttpMethod.Post, "/api/auth/device/verify")
        {
            Content = JsonContent.Create(new { deviceId, challenge, signature })
        };
        verifyRequest.Headers.Add("X-Client-Platform", "mobile");

        var verify = await client.SendAsync(verifyRequest);
        var verifyBody = await verify.Content.ReadAsStringAsync();
        verify.StatusCode.Should().Be(HttpStatusCode.OK, verifyBody);

        using var document = JsonDocument.Parse(verifyBody);
        return document.RootElement.GetProperty("refreshToken").GetString()!;
    }

    private async Task<Guid> UserIdAsync(string phoneNumber)
    {
        using var scope = _fixture.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await context.Users
            .Where(u => u.PhoneNumber == phoneNumber)
            .Select(u => u.Id)
            .SingleAsync();
    }

    private async Task<List<RefreshToken>> TokensAsync(Guid userId)
    {
        using var scope = _fixture.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await context.RefreshTokens
            .AsNoTracking()
            .Where(t => t.UserId == userId)
            .OrderBy(t => t.CreatedAtUtc)
            .ToListAsync();
    }
}
