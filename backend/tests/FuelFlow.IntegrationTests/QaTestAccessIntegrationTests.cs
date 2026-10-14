using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using FuelFlow.Features.Auth.QaTestAccess;
using FuelFlow.Features.Auth.Refresh;
using FuelFlow.Features.Auth.SendCode;
using FuelFlow.Features.Auth.Verify;
using FuelFlow.Features.Vouchers.Availability;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using FuelFlow.SharedKernel.Options;
using FuelFlow.SharedKernel.Security;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace FuelFlow.IntegrationTests;

/// <summary>
/// End-to-end QA test-access flow against a real Postgres + the full HTTP stack: the migration
/// seeds the QA account and (off) switch, and these tests drive send-code/verify/refresh over the
/// wire while flipping the switch through the real command handler. The QA code is injected as a
/// hash via PostConfigure (a throwaway value, never the production code).
/// </summary>
[Collection("Integration Tests")]
public class QaTestAccessIntegrationTests : WebApplicationFactory<Program>, IClassFixture<TestDatabaseFixture>
{
    private const string TestQaCode = "525252"; // distinct from the dev-bypass "000000"
    private static readonly string TestQaCodeHash = SecretsHasher.Hash(TestQaCode);
    private const string QaPhone = AuthOptions.QaTestAccountPhoneNumber;

    /// <summary>Captured before the seeded account is soft-deleted, so the test can prove the
    /// re-registration minted a different row.</summary>
    private Guid SeededQaUserId;

    public QaTestAccessIntegrationTests(TestDatabaseFixture fixture) => _ = fixture;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        // Inject the QA code hash deterministically, as if it had been hashed at boot from the
        // deploy secret. PostConfigure runs after the section binding, so it wins regardless of env.
        builder.ConfigureServices(services =>
            services.PostConfigure<AuthOptions>(o => o.QaTestAccessCodeHash = TestQaCodeHash));
    }

    private async Task SetSwitchAsync(bool enabled)
    {
        using var scope = Services.CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<SetQaTestAccessCommandHandler>();
        await handler.HandleAsync(
            new SetQaTestAccessCommand(enabled, Guid.NewGuid(), "Integration Test", SeedRoles.AdminName),
            CancellationToken.None);
    }

    [Fact]
    public async Task MigrationSeedsQaAccount()
    {
        using var scope = Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var qa = await context.Users.FirstOrDefaultAsync(u => u.PhoneNumber == QaPhone);

        qa.Should().NotBeNull();
        qa!.IsQaAccount.Should().BeTrue();
    }

    [Fact]
    public async Task QaCode_DoesNotWork_WhenDisabled()
    {
        await SetSwitchAsync(false);
        var client = CreateClient();

        // Ask for a code, then present the QA code. With the switch off it must be rejected — the
        // QA hash is never issued, and the verify-time gate blocks the QA phone.
        await client.PostAsJsonAsync("/api/auth/send-code", new SendCodeCommand(QaPhone));
        var response = await client.PostAsJsonAsync("/api/auth/verify", new VerifyCodeCommand(QaPhone, TestQaCode));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task QaCode_Works_WhenEnabled_AndIssuesTokenForQaAccount()
    {
        await SetSwitchAsync(true);
        var client = CreateClient();

        var send = await client.PostAsJsonAsync("/api/auth/send-code", new SendCodeCommand(QaPhone));
        send.StatusCode.Should().Be(HttpStatusCode.OK);

        // The stored code must be the QA hash (not the dev-bypass code), proving the QA branch ran.
        using (var scope = Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var code = await context.VerificationCodes
                .Where(v => v.PhoneNumber == QaPhone && !v.IsUsed)
                .OrderByDescending(v => v.CreatedAtUtc)
                .FirstAsync();
            code.Code.Should().Be(TestQaCodeHash);
        }

        var verify = await client.PostAsJsonAsync("/api/auth/verify", new VerifyCodeCommand(QaPhone, TestQaCode));
        verify.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await verify.Content.ReadFromJsonAsync<VerifyCodeResponse>();
        result!.AccessToken.Should().NotBeNullOrEmpty();

        // Clean up so re-runs start fresh.
        await SetSwitchAsync(false);
    }

    /// <summary>
    /// Deleting the QA account and signing up again has to land on a QA account again.
    ///
    /// The delete is a soft delete - the row survives so the history stays visible to staff -
    /// and signing up mints a <em>new</em> row for the same phone. The QA code kept working
    /// there, which is what made this easy to miss: the tester got a token, the token was
    /// valid, and every screen said the store was simply empty. The catalogue gate in
    /// <c>PackageStockAvailability</c> reads <c>User.IsQaAccount</c>, and nothing in the
    /// registration path had ever set it - only the seeding migration did, once.
    ///
    /// This is exactly the path App Review is instructed to perform on the demo account, so it
    /// is not an edge case: it is the one guaranteed to run.
    /// </summary>
    [Fact]
    public async Task QaPhone_ReRegistering_AfterDeletion_ComesBackAsAQaAccount()
    {
        await SetSwitchAsync(true);
        var client = CreateClient();

        // Soft-delete the seeded account, exactly as the app's "Delete Account" does.
        using (var scope = Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var qa = await context.Users.FirstAsync(u => u.PhoneNumber == QaPhone);
            SeededQaUserId = qa.Id;
            qa.IsDeleted = true;
            await context.SaveChangesAsync();
            (await context.Users.CountAsync(u => u.PhoneNumber == QaPhone && !u.IsDeleted))
                .Should().Be(0, "the seeded account is now soft-deleted");
        }

        // Sign up again with the QA code.
        (await client.PostAsJsonAsync("/api/auth/send-code", new SendCodeCommand(QaPhone)))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var verify = await client.PostAsJsonAsync("/api/auth/verify", new VerifyCodeCommand(QaPhone, TestQaCode));
        verify.StatusCode.Should().Be(HttpStatusCode.OK);

        using (var scope = Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            // A fresh row - the history lives on the deleted one, and must not be carried over.
            var active = await context.Users
                .Where(u => u.PhoneNumber == QaPhone && !u.IsDeleted)
                .SingleAsync();
            active.Id.Should().NotBeEmpty();
            active.Id.Should().NotBe(SeededQaUserId, "signing up again mints a new account");
            (await context.Orders.CountAsync(o => o.UserId == active.Id)).Should().Be(0,
                "history stays on the deleted account and is not carried over");

            // ...but QA access has to come back, or the tester sees an empty store.
            var availability = scope.ServiceProvider
                .GetRequiredService<PackageStockAvailability>();
            (await availability.IsTestAccountAsync(active.Id)).Should().BeTrue(
                "the QA phone re-registering must still see the QA catalogue");
        }

        await SetSwitchAsync(false);
    }

    [Fact]
    public async Task DisablingQa_RevokesLiveQaSession()
    {
        await SetSwitchAsync(true);
        var client = CreateClient();

        await client.PostAsJsonAsync("/api/auth/send-code", new SendCodeCommand(QaPhone));
        var verify = await client.PostAsJsonAsync("/api/auth/verify", new VerifyCodeCommand(QaPhone, TestQaCode));
        var tokens = await verify.Content.ReadFromJsonAsync<VerifyCodeResponse>();

        // The refresh token works while enabled...
        var okRefresh = await client.PostAsJsonAsync("/api/auth/refresh", new RefreshTokenCommand(tokens!.RefreshToken));
        okRefresh.StatusCode.Should().Be(HttpStatusCode.OK);
        var refreshed = await okRefresh.Content.ReadFromJsonAsync<RefreshTokenResponse>();

        // ...then the admin disables QA, which revokes the QA session.
        await SetSwitchAsync(false);

        var deniedRefresh = await client.PostAsJsonAsync("/api/auth/refresh", new RefreshTokenCommand(refreshed!.RefreshToken));
        deniedRefresh.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task QaStatusEndpoint_RequiresAuth()
    {
        var client = CreateClient();
        var response = await client.GetAsync("/api/admin/qa-test-access");
        // Anonymous callers are rejected by the Staff policy (401/403), never served status.
        response.StatusCode.Should().BeOneOf(HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden);
    }
}
