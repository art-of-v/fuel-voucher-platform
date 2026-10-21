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
public class QaTestAccessIntegrationTests : WebApplicationFactory<Program>, IClassFixture<TestDatabaseFixture>,
    IAsyncLifetime
{
    private const string TestQaCode = "525252"; // distinct from the dev-bypass "000000"
    private static readonly string TestQaCodeHash = SecretsHasher.Hash(TestQaCode);
    private const string QaPhone = AuthOptions.QaTestAccountPhoneNumber;

    /// <summary>Fixed id the seeding migration writes, so cleanup can tell the seeded row from the
    /// ones the delete-then-re-register tests mint.</summary>
    private static readonly Guid SeededQaUserIdValue = AuthOptions.QaTestAccountUserId;

    /// <summary>Stand-in for "some ordinary customer" in the negative-twin tests. A fixed value so
    /// cleanup can remove it and the test stays idempotent across re-runs.</summary>
    private const string OrdinaryPhone = "+380501112233";

    /// <summary>Captured before the seeded account is soft-deleted, so the test can prove the
    /// re-registration minted a different row.</summary>
    private Guid SeededQaUserId;

    public QaTestAccessIntegrationTests(TestDatabaseFixture fixture) => _ = fixture;

    /// <summary>
    /// Restores the migration's starting state after every test in this class.
    /// </summary>
    /// <remarks>
    /// The container is shared, and xUnit runs the tests in an arbitrary order, so a test that
    /// soft-deletes the seeded QA row and leaves it that way poisons whichever test runs next -
    /// <c>MigrationSeedsQaAccount</c> failed exactly that way when this was first written. Restoring
    /// here rather than at the end of each test means correctness no longer depends on ordering.
    ///
    /// The delete-then-re-register tests mint a second, live row for the same phone, so cleanup
    /// removes rows by id rather than trying to spot "the" QA account. Only this class's phone is
    /// touched - other classes share the container and own their own rows.
    /// </remarks>
    /// Explicit interface implementation. WebApplicationFactory&lt;Program&gt; already has a
    /// DisposeAsync() from IAsyncDisposable, and a public one here silently hides it - so xUnit's
    /// IAsyncLifetime.DisposeAsync() ran the state restore while anything disposing the factory as an
    /// IAsyncDisposable got the base implementation. Two different teardown paths for one object, and
    /// the compiler flagged it.
    async Task IAsyncLifetime.DisposeAsync()
    {
        using var scope = Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var strays = await context.Users
            .IgnoreQueryFilters()
            .Where(u => (u.PhoneNumber == QaPhone && u.Id != SeededQaUserIdValue)
                     || u.PhoneNumber == OrdinaryPhone)
            .ToListAsync();

        // orders.user_id is Restrict, so a hard delete of a stray that somehow picked up an order
        // would throw - and a throw here fails EVERY test in the class with a teardown error,
        // which is a far worse failure mode than the leftover row it avoids. Skip those instead.
        var deletable = new List<User>();
        foreach (var stray in strays)
        {
            if (!await context.Orders.IgnoreQueryFilters().AnyAsync(o => o.UserId == stray.Id))
                deletable.Add(stray);
            else
                stray.IsDeleted = true;
        }
        context.Users.RemoveRange(deletable);

        var seeded = await context.Users
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.Id == SeededQaUserIdValue);
        if (seeded is not null)
        {
            seeded.IsDeleted = false;
            seeded.IsBanned = false;
            seeded.IsActive = true;
            seeded.IsQaAccount = true;
        }

        await context.SaveChangesAsync();
    }

    public Task InitializeAsync() => Task.CompletedTask;

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
        var response = await client.PostAsNativeAsync("/api/auth/verify", new VerifyCodeCommand(QaPhone, TestQaCode));

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

        var verify = await client.PostAsNativeAsync("/api/auth/verify", new VerifyCodeCommand(QaPhone, TestQaCode));
        verify.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await verify.Content.ReadFromJsonAsync<VerifyCodeResponse>();
        result!.AccessToken.Should().NotBeNullOrEmpty();

        // Clean up so re-runs start fresh.
        await SetSwitchAsync(false);
    }

    /// <summary>
    /// Deleting the QA account and signing up again has to land on a QA account again - flagged
    /// <em>and</em> active.
    ///
    /// The delete is a soft delete - the row survives so the history stays visible to staff -
    /// and signing up mints a <em>new</em> row for the same phone. The QA code kept working
    /// there, which is what made this easy to miss: the tester got a token, the token was
    /// valid, and every screen said the store was simply empty. The catalogue gate in
    /// <c>PackageStockAvailability</c> reads <c>User.IsQaAccount</c>, and nothing in the
    /// registration path had ever set it - only the seeding migration did, once.
    ///
    /// <c>IsActive</c> failed the same way for the same reason and is the harder half to spot,
    /// because it has no visible symptom until checkout: an inactive account may browse, renew
    /// and chat, and is refused only by the payment endpoints. The seeded QA row is
    /// <c>is_active = true</c> for exactly that reason.
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

        var verify = await client.PostAsNativeAsync("/api/auth/verify", new VerifyCodeCommand(QaPhone, TestQaCode));
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

            // ...and active, or the tester sees the catalogue and then cannot buy anything. The
            // seeded account is is_active=true for exactly that reason; the flag alone was not
            // enough, so this assertion exists separately to keep the two from being conflated -
            // an inactive QA account looks healthy right up until checkout 403s.
            active.IsActive.Should().BeTrue(
                "the QA phone re-registering must come back active, like the seeded account it replaces");
        }

        await SetSwitchAsync(false);
    }

    /// <summary>
    /// The negative twin of <see cref="QaPhone_ReRegistering_AfterDeletion_ComesBackAsAQaAccount"/>:
    /// the activation carve-out must not leak to ordinary phone numbers.
    ///
    /// Registration normally mints a dormant account that staff activate later, and that default is
    /// load-bearing business logic. A QA fix that set IsActive=true unconditionally would make every
    /// self-signup instantly able to spend, which is exactly the kind of change that reads as
    /// harmless and ships silently.
    /// </summary>
    [Fact]
    public async Task OrdinaryPhone_Registering_StaysInactive()
    {
        var client = CreateClient();

        var phone = OrdinaryPhone;
        (await client.PostAsJsonAsync("/api/auth/send-code", new SendCodeCommand(phone)))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.PostAsNativeAsync("/api/auth/verify",
                new VerifyCodeCommand(phone, "000000")))
            .StatusCode.Should().Be(HttpStatusCode.OK, "the dev bypass code applies to an ordinary phone");

        using var scope = Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var user = await context.Users.SingleAsync(u => u.PhoneNumber == phone);

        user.IsActive.Should().BeFalse("only staff activate ordinary accounts");
        user.IsQaAccount.Should().BeFalse("the QA grant is scoped to the configured QA phone");
    }

    /// <summary>
    /// Turning QA access off has to stop the QA identity being granted again, not merely stop it
    /// authenticating. Otherwise a reviewer arriving mid-review silently becomes an ordinary
    /// customer and regains the real-stock catalogue, which is the whole point of the segregation.
    ///
    /// The guarantee is stronger than "comes back ordinary": with the switch off the QA phone cannot
    /// register <em>at all</em>. The verify handler rejects the QA phone before it even looks at the
    /// code, so there is no code - QA or dev-bypass - that mints an account for it. That is worth
    /// pinning explicitly, because the weaker version of this test (expect an ordinary inactive
    /// account) passed for the wrong reason once already.
    /// </summary>
    [Fact]
    public async Task QaPhone_CannotRegisterAtAll_WhenSwitchOff()
    {
        await SetSwitchAsync(false);
        var client = CreateClient();

        using (var scope = Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var qa = await context.Users.FirstAsync(u => u.PhoneNumber == QaPhone);
            qa.IsDeleted = true;
            await context.SaveChangesAsync();
        }

        (await client.PostAsJsonAsync("/api/auth/send-code", new SendCodeCommand(QaPhone)))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        // Neither the QA code nor the dev-bypass code gets through: the QA gate runs before the
        // code is compared, so the response is indistinguishable from any wrong guess.
        (await client.PostAsNativeAsync("/api/auth/verify", new VerifyCodeCommand(QaPhone, TestQaCode)))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await client.PostAsNativeAsync("/api/auth/verify", new VerifyCodeCommand(QaPhone, "000000")))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized,
                "the dev-bypass code must not become a way around the QA gate");

        using (var scope = Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var live = await context.Users
                .Where(u => u.PhoneNumber == QaPhone && !u.IsDeleted)
                .ToListAsync();
            live.Should().BeEmpty("no account may be resurrected or minted for the QA phone");
        }
    }

    [Fact]
    public async Task DisablingQa_RevokesLiveQaSession()
    {
        await SetSwitchAsync(true);
        var client = CreateClient();

        await client.PostAsJsonAsync("/api/auth/send-code", new SendCodeCommand(QaPhone));
        var verify = await client.PostAsNativeAsync("/api/auth/verify", new VerifyCodeCommand(QaPhone, TestQaCode));
        var tokens = await verify.Content.ReadFromJsonAsync<VerifyCodeResponse>();

        // The refresh token works while enabled...
        var okRefresh = await client.PostAsNativeAsync("/api/auth/refresh", new RefreshTokenCommand(tokens!.RefreshToken));
        okRefresh.StatusCode.Should().Be(HttpStatusCode.OK);
        var refreshed = await okRefresh.Content.ReadFromJsonAsync<RefreshTokenResponse>();

        // ...then the admin disables QA, which revokes the QA session.
        await SetSwitchAsync(false);

        var deniedRefresh = await client.PostAsNativeAsync("/api/auth/refresh", new RefreshTokenCommand(refreshed!.RefreshToken));
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
