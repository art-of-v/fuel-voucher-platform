using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using FuelFlow.Features.Auth.SendCode;
using FuelFlow.Features.Auth.SharedModels;
using FuelFlow.Features.Auth.Verify;
using FuelFlow.Persistence;
using FuelFlow.Testing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FuelFlow.IntegrationTests;

/// <summary>
/// Pins the replay window on the signed-purchase endpoints.
///
/// <para>
/// <c>DeviceSignatureMiddleware</c> rejects a replay by refusing a signature whose timestamp is
/// still inside the tolerance window AND whose nonce is still in cache. That only works if the
/// nonce outlives the timestamp, and the two are anchored to different clocks: the nonce's TTL
/// starts when the request <b>arrives</b>, while the timestamp the guard compares against was
/// written by the <b>device</b>. The tolerance check is symmetric, so a device whose clock runs up
/// to a full tolerance fast is accepted, which anchors its timestamp as late as
/// (arrival + tolerance) and leaves the ticket acceptable until (2 x tolerance) after the nonce
/// was written.
/// </para>
///
/// <para>
/// The nonce TTL used to be exactly one tolerance, which left a real window of up to
/// <c>TimestampToleranceMs</c> — five minutes at the shipped configuration — during which a
/// captured signed request could be replayed against /api/purchases, /bulk and /renew. No key, no
/// credential: the request was simply sent again while the note was down and the ticket still
/// valid.
/// </para>
///
/// <para>
/// Both numbers are shrunk to seconds so the window is observable inside a test. The relationship
/// under test is the RATIO between them, not the magnitudes: tolerance 4s, configured nonce TTL 4s
/// (what the old formula produced), and a replay attempted 5s in — past the old TTL, well inside
/// the corrected one.
/// </para>
/// </summary>
[Collection("Integration Tests")]
public sealed class DeviceSignatureReplayIntegrationTests : IClassFixture<TestDatabaseFixture>
{
    private const string Phone = "+15550000417";
    private const string PurchasePath = "/api/purchases";

    /// <summary>Seconds of clock skew the guard tolerates, in each direction.</summary>
    private const int ToleranceMs = 4000;

    /// <summary>
    /// The nonce TTL as CONFIGURED, which is deliberately left at the old formula's result so that
    /// the corrected lifetime has to come from the middleware computing it, not from a tuned
    /// setting that would mask the bug in production.
    /// </summary>
    private const int ConfiguredNonceTtlSeconds = 4;

    private readonly TestDatabaseFixture _fixture;
    private RSA _deviceKey = null!;
    private string _deviceId = null!;

    public DeviceSignatureReplayIntegrationTests(TestDatabaseFixture fixture)
    {
        // Constructing the fixture starts the containers and publishes Database__ConnectionString,
        // which the host below needs before it boots.
        _fixture = fixture;
    }

    /// <summary>
    /// Device auth ON with the bypass OFF. Development ships
    /// <c>AllowDevelopmentBypass: true</c>, which makes the middleware wave every signed request
    /// through (line 61), so without this override the test would assert nothing at all.
    /// </summary>
    private sealed class ReplayWindowFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["DeviceAuth:Enabled"] = "true",
                    ["DeviceAuth:AllowDevelopmentBypass"] = "false",
                    ["DeviceAuth:TimestampToleranceMs"] = ToleranceMs.ToString(),
                    ["DeviceAuth:SignatureNonceTtlSeconds"] = ConfiguredNonceTtlSeconds.ToString(),
                });
            });
        }
    }

    [Fact]
    public async Task A_Signed_Purchase_Cannot_Be_Replayed_After_The_Nonces_Configured_Ttl_Has_Elapsed()
    {
        using var factory = new ReplayWindowFactory();
        using var client = factory.CreateClient();

        var token = await SignInAsync(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        _deviceKey = RSA.Create(2048);
        _deviceId = $"dev-{Guid.NewGuid():N}";
        await SeedDeviceAsync(factory, _deviceKey.ExportSubjectPublicKeyInfoPem());

        // Signed as if the device's clock is three quarters of the tolerance AHEAD of the server's -
        // inside the window, and far enough past zero that the old TTL dropped the nonce early.
        var timestamp = (TestClock.Now.ToUnixTimeMilliseconds() + (ToleranceMs * 3 / 4))
            .ToString();
        var body = JsonSerializer.Serialize(new { anything = "goes" });
        var signature = Sign($"{HttpMethod.Post}{PurchasePath}{body}{timestamp}");

        var first = await SendSignedAsync(client, body, timestamp, signature);
        var firstBody = await first.Content.ReadAsStringAsync();

        firstBody.Should().NotContain(
            "already processed",
            "the first presentation of a signed request is not a replay");

        // Past the configured nonce TTL (4s), still inside the corrected one (2 x tolerance = 8s),
        // and still inside the timestamp window (written 3s ahead, so accepted until 7s from now).
        await Task.Delay(TimeSpan.FromSeconds(5));

        var replay = await SendSignedAsync(client, body, timestamp, signature);
        var replayBody = await replay.Content.ReadAsStringAsync();

        replay.StatusCode.Should().Be(
            HttpStatusCode.Unauthorized,
            "the nonce must still be alive while the signed timestamp is still acceptable, "
            + $"otherwise the request is replayable. Body: {replayBody}");
        replayBody.Should().Contain("already processed");
    }

    private Task<HttpResponseMessage> SendSignedAsync(
        HttpClient client,
        string body,
        string timestamp,
        string signature)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, PurchasePath)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("X-Device-Id", _deviceId);
        request.Headers.Add("X-Timestamp", timestamp);
        request.Headers.Add("X-Signature", signature);
        return client.SendAsync(request);
    }

    /// <summary>RSA-SHA256 over the middleware's canonical payload, base64, as the app expects.</summary>
    private string Sign(string payload) =>
        Convert.ToBase64String(_deviceKey.SignData(
            Encoding.UTF8.GetBytes(payload),
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1));

    private async Task SeedDeviceAsync(ReplayWindowFactory factory, string publicKeyPem)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var userId = await context.Users
            .Where(u => u.PhoneNumber == Phone)
            .Select(u => u.Id)
            .SingleAsync();

        context.Devices.Add(new Device
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            DeviceId = _deviceId,
            PublicKey = publicKeyPem,
            Status = DeviceStatus.Active,
            CreatedAt = TestClock.UtcNow,
        });
        await context.SaveChangesAsync();
    }

    /// <summary>
    /// Signs in over the real OTP flow, which is also what creates the user and assigns its role.
    /// </summary>
    private static async Task<string> SignInAsync(HttpClient client)
    {
        await client.PostAsJsonAsync("/api/auth/send-code", new SendCodeCommand(Phone));
        var verified = await client.PostAsJsonAsync(
            "/api/auth/verify",
            new VerifyCodeCommand(Phone, "000000"));
        verified.EnsureSuccessStatusCode();

        var payload = JsonDocument.Parse(await verified.Content.ReadAsStringAsync());
        return payload.RootElement.GetProperty("accessToken").GetString()!;
    }
}