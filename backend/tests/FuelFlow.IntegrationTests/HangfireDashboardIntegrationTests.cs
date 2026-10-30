using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using FuelFlow.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FuelFlow.IntegrationTests;

/// <summary>
/// The job dashboard through the real pipeline: it renders for an Admin, and it refuses a post
/// that did not come from its own page.
/// </summary>
/// <remarks>
/// <para>
/// The unit test (<c>HangfireDashboardOptionsTests</c>) pins the option. This proves the running
/// host mounts it that way and that Hangfire can resolve the antiforgery service: with
/// <c>IAntiforgery</c> missing, Hangfire skips the check, the option still reads
/// <c>false</c>, and the forged post goes through - which is the state this suite exists to
/// catch.
/// </para>
/// <para>
/// The host runs in Development, where <c>Auth:DevBypass</c> lets the dashboard's own filter
/// admit a local browser. The client is still signed in as an Admin, because the global fallback
/// policy applies to the dashboard as middleware: without a credential the request is a 401
/// before Hangfire ever sees it, so an unsigned request would prove nothing about the token.
/// </para>
/// </remarks>
[Collection("Integration Tests")]
public sealed class HangfireDashboardIntegrationTests : IClassFixture<TestDatabaseFixture>
{
    private readonly TestDatabaseFixture _fixture;

    public HangfireDashboardIntegrationTests(TestDatabaseFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task TheDashboard_Renders_WithTheTokenItsFormsNeed()
    {
        var client = await AdminClientAsync();

        var response = await client.GetAsync("/hangfire");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.Should().Be("text/html");

        // The page hands its scripts the token Hangfire validates. Without it the dashboard's own
        // buttons would all answer 403, which is how "the token is on" fails visibly instead of
        // silently.
        (await response.Content.ReadAsStringAsync()).Should().Contain("csrf-token");
    }

    [Fact]
    public async Task AStateChangingPost_WithoutTheToken_IsRefused()
    {
        var client = await AdminClientAsync();

        // Triggering a recurring job is a matched route that changes state, posted by the
        // dashboard's own buttons. No cookie and no token: the shape of a cross-site post.
        var response = await client.PostAsync(
            "/hangfire/recurring/trigger",
            new FormUrlEncodedContent(new Dictionary<string, string> { ["jobs[]"] = "probe" }));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "an authenticated Admin is admitted by the dashboard filter, so a 403 here can only " +
            "come from the request-token check");
    }

    private async Task<HttpClient> AdminClientAsync()
    {
        var phoneNumber = "+380" + Random.Shared.Next(100000000, 999999999);

        using (var scope = _fixture.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var role = await context.Roles.FirstAsync(r => r.Name == SeedRoles.AdminName);
            var now = TestClock.UtcNow;

            context.Users.Add(new User
            {
                Id = Guid.NewGuid(),
                PhoneNumber = phoneNumber,
                RoleId = role.Id,
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
                IsActive = true,
                TokenVersion = 1,
            });
            await context.SaveChangesAsync();
        }

        var client = _fixture.CreateClient();

        var sendCode = await client.PostAsJsonAsync("/api/auth/send-code", new { phoneNumber });
        sendCode.EnsureSuccessStatusCode();

        // Auth:DevBypass is on in Development, so the fixed code signs the seeded Admin in.
        var verify = await client.PostAsJsonAsync("/api/auth/verify", new { phoneNumber, code = "000000" });
        verify.EnsureSuccessStatusCode();
        var token = (await verify.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("accessToken").GetString();

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }
}
