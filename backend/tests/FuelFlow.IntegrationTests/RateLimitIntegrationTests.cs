using System.Net;
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
/// Asserts the OTP rate limits actually return 429 - the per-phone send-code and verify limits
/// and the per-IP OTP ceiling. The existing suite only checks limiter middleware *ordering*
/// (PipelineOrderConventionTests) and never proves a limit fires. These match the behaviour
/// verified live against production; here they run against a real host so a regression that
/// removes a policy or mis-partitions it is caught in CI.
///
/// The default integration host boots Development with Auth:DevBypass=true, which sets every
/// PermitLimit to int.MaxValue - so a bespoke host with DevBypass off is required. With no SMS
/// provider configured (SmsClub/Twilio creds are empty in Development) the composition root
/// resolves FakeSmsService, so no real SMS is sent even with bypass off. Each test uses its own
/// factory instance so the process-global limiter partitions start empty.
/// </summary>
[Collection("Integration Tests")]
public sealed class RateLimitIntegrationTests : IClassFixture<TestDatabaseFixture>
{
    public RateLimitIntegrationTests(TestDatabaseFixture fixture)
    {
        // Constructing the fixture is what starts Postgres and publishes Database__ConnectionString,
        // which the host below needs before it boots.
        _ = fixture;
    }

    /// <summary>
    /// A Development host with the OTP rate limits actually enforced (DevBypass off). SmsClub token
    /// is pinned empty so a developer's user-secrets cannot wire a real SMS provider into the suite.
    /// </summary>
    private sealed class NoBypassFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Auth:DevBypass"] = "false",
                    ["SmsClub:Token"] = "",
                    ["SmsClub:SenderName"] = "",
                });
            });
        }
    }

    [Fact]
    public async Task VerifyCode_ShouldReturn429_OnSixthAttemptForSamePhone()
    {
        // Per-phone verify limit is 5 / 5 min. No code is seeded, so each of the first five
        // attempts reaches the handler and returns 401 (invalid code) - a rejection still spends
        // a permit - and the sixth is refused by the limiter with 429.
        await using var factory = new NoBypassFactory();
        var client = factory.CreateClient();
        const string phone = "+380995550001";

        for (var i = 1; i <= 5; i++)
        {
            var resp = await client.PostAsJsonAsync("/api/auth/verify", new VerifyCodeCommand(phone, "999999"));
            resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized, $"attempt {i} should reach the handler");
        }

        var sixth = await client.PostAsJsonAsync("/api/auth/verify", new VerifyCodeCommand(phone, "999999"));
        sixth.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task SendCode_ShouldReturn429_OnFourthAttemptForSamePhone()
    {
        // Per-phone send-code limit is 3 / min: three 200s then a 429.
        await using var factory = new NoBypassFactory();
        var client = factory.CreateClient();
        const string phone = "+380995550002";

        for (var i = 1; i <= 3; i++)
        {
            var resp = await client.PostAsJsonAsync("/api/auth/send-code", new SendCodeCommand(phone));
            resp.StatusCode.Should().Be(HttpStatusCode.OK, $"send {i} should succeed");
        }

        var fourth = await client.PostAsJsonAsync("/api/auth/send-code", new SendCodeCommand(phone));
        fourth.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task SendCode_ShouldReturn429_OnThirteenthDistinctPhone_ViaPerIpOtpCeiling()
    {
        // The per-IP OTP ceiling is 12 / 10 min across all four OTP paths. Twelve DISTINCT phones,
        // one send each: no per-phone limit trips (1 << 3/min), and the 13th distinct phone trips
        // the ceiling. This doubles as proof that the send-code partition key is the phone from the
        // BODY, not the IP: were it keyed per-IP, the named policy (3/min) would 429 at the 4th call.
        await using var factory = new NoBypassFactory();
        var client = factory.CreateClient();

        for (var i = 1; i <= 12; i++)
        {
            var phone = $"+38099555{i:D4}";
            var resp = await client.PostAsJsonAsync("/api/auth/send-code", new SendCodeCommand(phone));
            resp.StatusCode.Should().Be(HttpStatusCode.OK, $"distinct phone #{i} should be under both limits");
        }

        var thirteenth = await client.PostAsJsonAsync("/api/auth/send-code", new SendCodeCommand("+380995559999"));
        thirteenth.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
    }
}
