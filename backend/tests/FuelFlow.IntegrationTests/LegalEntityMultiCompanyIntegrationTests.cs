using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using FuelFlow.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FuelFlow.IntegrationTests;

/// <summary>
/// HTTP full-stack coverage for the multi-company endpoints added in epic #103 S0:
/// <c>GET /api/legal-entity/mine</c> (list owned entities) and <c>POST /api/legal-entity</c>
/// (create a NEW entity). The load-bearing behaviour is that a single user may now own
/// several legal entities — enabled by dropping the unique index on
/// <c>legal_entities.user_id</c>. Pattern B (real <c>WebApplicationFactory</c> client + real
/// Postgres), mirroring <see cref="LegalEntityProfileIntegrationTests"/>.
/// </summary>
[Collection("Integration Tests")]
public sealed class LegalEntityMultiCompanyIntegrationTests : IClassFixture<TestDatabaseFixture>
{
    private readonly TestDatabaseFixture _fixture;

    public LegalEntityMultiCompanyIntegrationTests(TestDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Create_AllowsMultipleCompaniesForSameUser()
    {
        await ResetAsync();
        const string phone = "+380000000011";
        var client = await AuthenticatedClientAsync(phone);

        var first = await client.PostAsJsonAsync("/api/legal-entity", new
        {
            Name = "ACME LLC",
            Edrpou = "10000001"
        });
        first.StatusCode.Should().Be(HttpStatusCode.OK);
        var firstId = (await first.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var second = await client.PostAsJsonAsync("/api/legal-entity", new
        {
            Name = "Beta LLC",
            Edrpou = "10000002"
        });
        second.StatusCode.Should().Be(HttpStatusCode.OK);
        var secondId = (await second.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        // The whole point of the migration: two distinct entities under one user.
        secondId.Should().NotBe(firstId);

        using var scope = _fixture.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var userId = await UserIdAsync(context, phone);
        var entities = await context.LegalEntities.Where(e => e.UserId == userId).ToListAsync();
        entities.Should().HaveCount(2);
        entities.Select(e => e.Edrpou).Should().BeEquivalentTo(new[] { "10000001", "10000002" });
    }

    [Fact]
    public async Task GetMine_ReturnsAllOwnedCompanies()
    {
        await ResetAsync();
        const string phone = "+380000000012";
        var client = await AuthenticatedClientAsync(phone);

        await client.PostAsJsonAsync("/api/legal-entity", new { Name = "ACME LLC", Edrpou = "10000011" });
        await client.PostAsJsonAsync("/api/legal-entity", new { Name = "Beta LLC", Edrpou = "10000012" });

        var response = await client.GetAsync("/api/legal-entity/mine");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var list = await response.Content.ReadFromJsonAsync<JsonElement>();
        list.ValueKind.Should().Be(JsonValueKind.Array);
        list.GetArrayLength().Should().Be(2);
    }

    [Fact]
    public async Task GetMine_ReturnsEmptyArray_WhenNoCompanies()
    {
        await ResetAsync();
        var client = await AuthenticatedClientAsync("+380000000013");

        var response = await client.GetAsync("/api/legal-entity/mine");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var list = await response.Content.ReadFromJsonAsync<JsonElement>();
        list.ValueKind.Should().Be(JsonValueKind.Array);
        list.GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task Create_Returns409_WhenEdrpouRegisteredToAnotherUser()
    {
        await ResetAsync();

        var owner = await AuthenticatedClientAsync("+380000000014");
        var claim = await owner.PostAsJsonAsync("/api/legal-entity", new { Name = "ACME LLC", Edrpou = "10000021" });
        claim.StatusCode.Should().Be(HttpStatusCode.OK);

        // A different account claiming the same EDRPOU must get a clean 409, not a 500.
        var other = await AuthenticatedClientAsync("+380000000015");
        var conflict = await other.PostAsJsonAsync("/api/legal-entity", new { Name = "Beta LLC", Edrpou = "10000021" });
        conflict.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Create_Returns400_WhenNameMissing()
    {
        await ResetAsync();
        var client = await AuthenticatedClientAsync("+380000000016");

        var response = await client.PostAsJsonAsync("/api/legal-entity", new { Name = "", Edrpou = "10000031" });
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Update_UpdatesTheSpecifiedEntity_NotTheFirst()
    {
        await ResetAsync();
        const string phone = "+380000000017";
        var client = await AuthenticatedClientAsync(phone);

        var first = await client.PostAsJsonAsync("/api/legal-entity", new { Name = "ACME LLC", Edrpou = "10000041" });
        var firstId = (await first.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var second = await client.PostAsJsonAsync("/api/legal-entity", new { Name = "Beta LLC", Edrpou = "10000042" });
        var secondId = (await second.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        // Edit the SECOND company — the legacy POST /profile could only ever touch the first.
        var update = await client.PutAsJsonAsync($"/api/legal-entity/{secondId}", new
        {
            Name = "Beta Renamed LLC",
            Edrpou = "10000042",
            Address = "1 Main St"
        });
        update.StatusCode.Should().Be(HttpStatusCode.OK);

        using var scope = _fixture.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var stored = await context.LegalEntities.AsNoTracking().ToListAsync();
        stored.Single(e => e.Id == secondId).Name.Should().Be("Beta Renamed LLC");
        stored.Single(e => e.Id == secondId).Address.Should().Be("1 Main St");
        // The first entity is untouched.
        stored.Single(e => e.Id == firstId).Name.Should().Be("ACME LLC");
    }

    [Fact]
    public async Task Update_Returns404_WhenEntityNotOwned()
    {
        await ResetAsync();

        var owner = await AuthenticatedClientAsync("+380000000018");
        var created = await owner.PostAsJsonAsync("/api/legal-entity", new { Name = "ACME LLC", Edrpou = "10000051" });
        var entityId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var intruder = await AuthenticatedClientAsync("+380000000019");
        var response = await intruder.PutAsJsonAsync($"/api/legal-entity/{entityId}", new
        {
            Name = "Hijacked LLC",
            Edrpou = "10000051"
        });
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Update_Returns409_WhenEdrpouRegisteredToAnotherUser()
    {
        await ResetAsync();

        var other = await AuthenticatedClientAsync("+380000000020");
        await other.PostAsJsonAsync("/api/legal-entity", new { Name = "Taken LLC", Edrpou = "10000061" });

        var owner = await AuthenticatedClientAsync("+380000000021");
        var created = await owner.PostAsJsonAsync("/api/legal-entity", new { Name = "ACME LLC", Edrpou = "10000062" });
        var entityId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        // Renaming our own entity to a globally-taken EDRPOU must surface a clean 409.
        var response = await owner.PutAsJsonAsync($"/api/legal-entity/{entityId}", new
        {
            Name = "ACME LLC",
            Edrpou = "10000061"
        });
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
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

    private static async Task<Guid> UserIdAsync(ApplicationDbContext context, string phoneNumber)
        => await context.Users
            .Where(u => u.PhoneNumber == phoneNumber)
            .Select(u => u.Id)
            .SingleAsync();

    private async Task ResetAsync()
    {
        using var scope = _fixture.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await context.Database.ExecuteSqlRawAsync(
            """TRUNCATE TABLE "refunds", "fulfillments", "orders", "order_line_items", "outbox_events", "fuel_vouchers", "company_invitations", "company_members", "legal_entities", "verification_codes", "users", "provider_event_outbox", "app_settings" RESTART IDENTITY CASCADE""");
    }
}
