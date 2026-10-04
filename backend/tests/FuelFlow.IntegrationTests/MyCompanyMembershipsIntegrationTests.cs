using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using FuelFlow.Features.Vouchers;
using FuelFlow.Features.Vouchers.SharedModels;
using FuelFlow.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FuelFlow.IntegrationTests;

/// <summary>
/// Epic #103 S5 — the worker-context feed (<c>GET /api/company/my-memberships</c>) and the
/// cross-context isolation that makes it safe to switch into.
///
/// The defect this slice closes: <c>GET /api/vouchers/my</c> already returns the vouchers
/// issued to a worker (<c>WorkerUserId == me</c>), but <c>GET /api/legal-entity/mine</c> lists
/// owned entities only — so the client had no context for them and dropped them from the
/// wallet. These tests pin the pairing that makes a worker context possible (membership listed,
/// owned-entities empty, issued voucher visible) and prove that being a member grants no owner
/// power: every owner endpoint still rejects a non-owner with 404 (INV-2).
///
/// Pattern B (real <c>WebApplicationFactory</c> client + real Postgres) — the invite → accept
/// → membership flow spans auth, DI, EF and routing, which none of the handler-level tests cover.
/// </summary>
[Collection("Integration Tests")]
public sealed class MyCompanyMembershipsIntegrationTests : IClassFixture<TestDatabaseFixture>
{
    private readonly TestDatabaseFixture _fixture;

    public MyCompanyMembershipsIntegrationTests(TestDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Accept_ThenMyMemberships_ListsTheCompany_WithTheOwnerFlag()
    {
        await ResetAsync();
        var (owner, worker, legalEntityId) = await ArrangeMembershipAsync();

        var list = await worker.GetFromJsonAsync<JsonElement>("/api/company/my-memberships");

        list.ValueKind.Should().Be(JsonValueKind.Array);
        list.GetArrayLength().Should().Be(1);
        var membership = list[0];
        membership.GetProperty("legalEntityId").GetGuid().Should().Be(legalEntityId);
        membership.GetProperty("name").GetString().Should().Be("ACME LLC");
        membership.GetProperty("edrpou").GetString().Should().Be("10000071");
        membership.GetProperty("ownerUserId").GetGuid().Should().NotBe(Guid.Empty);
        membership.GetProperty("isOwner").GetBoolean().Should().BeFalse(
            "the caller works for the company but does not own it");

        // The owner does not own a membership here, so their own feed stays empty —
        // `my-memberships` means "where I work", not "where I have power".
        var ownerList = await owner.GetFromJsonAsync<JsonElement>("/api/company/my-memberships");
        ownerList.GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task MyMemberships_IsEmpty_WhileTheInvitationIsStillPending()
    {
        await ResetAsync();
        var (_, worker, _) = await ArrangeMembershipAsync(accept: false);

        var list = await worker.GetFromJsonAsync<JsonElement>("/api/company/my-memberships");
        list.GetArrayLength().Should().Be(0, "an invitation the worker has not accepted is not yet a context");
    }

    [Fact]
    public async Task MyMemberships_IsEmpty_ForAUserWithNoCompanyAtAll()
    {
        await ResetAsync();
        var client = await AuthenticatedClientAsync("+380000000081");

        var response = await client.GetAsync("/api/company/my-memberships");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var list = await response.Content.ReadFromJsonAsync<JsonElement>();
        list.GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task MyMemberships_Returns401_WhenAnonymous()
    {
        await ResetAsync();
        var response = await _fixture.CreateClient().GetAsync("/api/company/my-memberships");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task WorkerContext_IsPossible_IssuedVoucherIsServed_WhileOwnedCompaniesStayEmpty()
    {
        await ResetAsync();
        var (owner, worker, legalEntityId) = await ArrangeMembershipAsync();

        // The company bought fuel and handed a voucher to the worker.
        var voucherId = await SeedPoolVoucherAsync(legalEntityId, await UserIdAsync(owner));
        var gift = await owner.PostAsJsonAsync("/api/company/vouchers/gift", new
        {
            workerUserId = await UserIdAsync(worker),
            voucherIds = new[] { voucherId },
            legalEntityId
        });
        gift.StatusCode.Should().Be(HttpStatusCode.OK);

        // The pairing the client needs: nothing owned, but the issued fuel is served.
        var owned = await worker.GetFromJsonAsync<JsonElement>("/api/legal-entity/mine");
        owned.GetArrayLength().Should().Be(0, "a worker owns no legal entity — the switcher needs my-memberships");

        var vouchers = await worker.GetFromJsonAsync<JsonElement>("/api/vouchers/my");
        vouchers.GetArrayLength().Should().Be(1);
        var issued = vouchers[0];
        issued.GetProperty("id").GetGuid().Should().Be(voucherId);
        issued.GetProperty("legalEntityId").GetGuid().Should().Be(legalEntityId,
            "the voucher is stamped with the company, which is why the personal-context filter used to hide it");
        issued.GetProperty("workerUserId").GetGuid().Should().Be(await UserIdAsync(worker));
        issued.GetProperty("source").GetString().Should().Be("gifted");
    }

    [Fact]
    public async Task Worker_CannotUseOwnerEndpoints_ForACompanyTheyOnlyWorkFor()
    {
        await ResetAsync();
        var (owner, worker, legalEntityId) = await ArrangeMembershipAsync();
        var workerId = await UserIdAsync(worker);
        var voucherId = await SeedPoolVoucherAsync(legalEntityId, await UserIdAsync(owner));

        // Being a member is not ownership: INV-2 — a worker can never inspect, move or
        // freeze the employer's fuel. The owner list endpoints answer 200 with an empty
        // body for a company that is not the caller's (they resolve to "no company"),
        // while every owner *write* is rejected outright. Assert both halves: a worker
        // must not receive data, and must not be able to change it.
        var members = await worker.GetFromJsonAsync<JsonElement>($"/api/company/members?legalEntityId={legalEntityId}");
        members.GetArrayLength().Should().Be(0, "a member must not read the roster");
        var invitations = await worker.GetFromJsonAsync<JsonElement>($"/api/company/invitations?legalEntityId={legalEntityId}");
        invitations.GetArrayLength().Should().Be(0, "a member must not read the owner's invitations");

        (await worker.PostAsJsonAsync("/api/company/vouchers/gift", new
        {
            workerUserId = workerId,
            voucherIds = new[] { voucherId },
            legalEntityId
        })).StatusCode.Should().Be(HttpStatusCode.NotFound, "a worker cannot issue fuel");
        (await worker.PostAsync($"/api/company/vouchers/recall/{voucherId}?legalEntityId={legalEntityId}", null))
            .StatusCode.Should().Be(HttpStatusCode.NotFound, "a worker cannot pull fuel back");
        (await worker.PostAsync($"/api/company/vouchers/block/{voucherId}?legalEntityId={legalEntityId}", null))
            .StatusCode.Should().Be(HttpStatusCode.NotFound, "a worker cannot freeze fuel");

        // The company's fuel is untouched by any of those attempts.
        await using var verify = CreateContext();
        var stored = await verify.FuelVouchers.AsNoTracking().SingleAsync(v => v.Id == voucherId);
        stored.Status.Should().Be(VoucherStatus.Assigned);
        stored.WorkerUserId.Should().BeNull();
    }

    [Fact]
    public async Task Firing_RemovesTheContext_SoTheWorkerFallsBackToPersonal()
    {
        await ResetAsync();
        var (owner, worker, legalEntityId) = await ArrangeMembershipAsync();
        var voucherId = await SeedPoolVoucherAsync(legalEntityId, await UserIdAsync(owner));
        await owner.PostAsJsonAsync("/api/company/vouchers/gift", new
        {
            workerUserId = await UserIdAsync(worker),
            voucherIds = new[] { voucherId },
            legalEntityId
        });

        var members = await owner.GetFromJsonAsync<JsonElement>($"/api/company/members?legalEntityId={legalEntityId}");
        var memberId = members[0].GetProperty("id").GetGuid();

        var fired = await owner.DeleteAsync($"/api/company/members/{memberId}?legalEntityId={legalEntityId}");
        fired.StatusCode.Should().Be(HttpStatusCode.OK);

        var list = await worker.GetFromJsonAsync<JsonElement>("/api/company/my-memberships");
        list.GetArrayLength().Should().Be(0, "a fired worker has no company context left");

        var vouchers = await worker.GetFromJsonAsync<JsonElement>("/api/vouchers/my");
        vouchers.GetArrayLength().Should().Be(0, "the fuel is frozen, so it is no longer offered to the worker");

        await using var verify = CreateContext();
        var stored = await verify.FuelVouchers.AsNoTracking().SingleAsync(v => v.Id == voucherId);
        stored.Status.Should().Be(VoucherStatus.Blocked);
        stored.WorkerUserId.Should().BeNull();
    }

    // ── arrangement ────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Owner creates a company, invites the worker by phone and (unless <paramref name="accept"/>
    /// is false) the worker accepts — the real HTTP flow, so membership creation is covered too.
    /// </summary>
    private async Task<(HttpClient Owner, HttpClient Worker, Guid LegalEntityId)> ArrangeMembershipAsync(
        bool accept = true)
    {
        const string ownerPhone = "+380000000071";
        const string workerPhone = "+380000000072";
        var owner = await AuthenticatedClientAsync(ownerPhone);
        var worker = await AuthenticatedClientAsync(workerPhone);

        var created = await owner.PostAsJsonAsync("/api/legal-entity", new { Name = "ACME LLC", Edrpou = "10000071" });
        created.StatusCode.Should().Be(HttpStatusCode.OK);
        var legalEntityId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var invited = await owner.PostAsJsonAsync("/api/company/invitations", new
        {
            workerPhoneNumber = workerPhone,
            legalEntityId
        });
        invited.StatusCode.Should().Be(HttpStatusCode.OK);

        if (accept)
        {
            var pending = await worker.GetFromJsonAsync<JsonElement>("/api/company/my-invitations");
            var invitationId = pending[0].GetProperty("id").GetGuid();
            var accepted = await worker.PostAsync($"/api/company/invitations/{invitationId}/accept", null);
            accepted.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        return (owner, worker, legalEntityId);
    }

    /// <summary>A company-pool voucher: bought into the company, not yet handed to a worker.</summary>
    private async Task<Guid> SeedPoolVoucherAsync(Guid legalEntityId, Guid ownerUserId)
    {
        var id = Guid.NewGuid();
        await using var seed = CreateContext();
        seed.FuelVouchers.Add(new FuelVoucher
        {
            Id = id,
            Provider = "OKKO",
            FuelTypeId = "okko-95",
            Liters = 50m,
            ProviderExpirationDate = DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(1)),
            CustomerExpirationDate = DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(1)),
            VoucherNumber = $"OKKO-{id:N}"[..20],
            QrPayload = $"payload-{id:N}",
            Status = VoucherStatus.Assigned,
            LegalEntityId = legalEntityId,
            AssignedToUserId = ownerUserId,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        });
        await seed.SaveChangesAsync();
        return id;
    }

    private async Task<HttpClient> AuthenticatedClientAsync(string phoneNumber)
    {
        var client = _fixture.CreateClient();

        var sendCode = await client.PostAsJsonAsync("/api/auth/send-code", new { phoneNumber });
        sendCode.EnsureSuccessStatusCode();

        var verify = await client.PostAsJsonAsync("/api/auth/verify", new { phoneNumber, code = "000000" });
        verify.EnsureSuccessStatusCode();
        var token = (await verify.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("accessToken").GetString();

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private async Task<Guid> UserIdAsync(HttpClient client)
    {
        var me = await client.GetFromJsonAsync<JsonElement>("/api/auth/user/me");
        return me.GetProperty("id").GetGuid();
    }

    private async Task ResetAsync()
    {
        using var scope = _fixture.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await context.Database.ExecuteSqlRawAsync(
            """TRUNCATE TABLE "refunds", "fulfillments", "orders", "order_line_items", "outbox_events", "fuel_vouchers", "company_invitations", "company_members", "legal_entities", "verification_codes", "users", "provider_event_outbox", "app_settings" RESTART IDENTITY CASCADE""");
    }

    private ApplicationDbContext CreateContext()
        => new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(_fixture.DbContainer.GetConnectionString())
            .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking)
            .Options);
}
