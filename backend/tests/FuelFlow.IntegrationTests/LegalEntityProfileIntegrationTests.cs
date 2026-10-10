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
/// HTTP full-stack coverage for <c>LegalEntityController.UpsertProfile</c> — the Company/B2B vertical's
/// create/update-profile entry point, which had zero coverage before this slice. Pattern B (real
/// <c>WebApplicationFactory</c> client + real Postgres), mirroring <see cref="PurchaseIntegrationTests"/>.
///
/// Unlike the purchase tests, no Admin elevation or account activation is needed: <c>UpsertProfile</c>
/// is <c>[Authorize]</c>-only and there is no global is-active filter, so a freshly verified (inactive)
/// account can call it. Phone numbers are fully synthetic.
/// </summary>
[Collection("Integration Tests")]
public sealed class LegalEntityProfileIntegrationTests : IClassFixture<TestDatabaseFixture>
{
    private readonly TestDatabaseFixture _fixture;

    public LegalEntityProfileIntegrationTests(TestDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task UpsertProfile_CreatesEntity_WhenNoneExists()
    {
        await ResetAsync();
        const string phone = "+380000000001";
        var client = await AuthenticatedClientAsync(phone);

        var response = await client.PostAsJsonAsync("/api/legal-entity/profile", new
        {
            Name = "ACME LLC",
            Edrpou = "12345678",
            Email = "acme@example.com"
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var scope = _fixture.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var userId = await UserIdAsync(context, phone);
        var entity = await context.LegalEntities.SingleAsync(e => e.UserId == userId);
        entity.Name.Should().Be("ACME LLC");
        entity.Edrpou.Should().Be("12345678");
        entity.Email.Should().Be("acme@example.com");
    }

    [Fact]
    public async Task UpsertProfile_UpdatesExisting_OnSecondCall()
    {
        await ResetAsync();
        const string phone = "+380000000002";
        var client = await AuthenticatedClientAsync(phone);

        var createResponse = await client.PostAsJsonAsync("/api/legal-entity/profile", new
        {
            Name = "ACME LLC",
            Edrpou = "12345678"
        });
        createResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var createdId = (await createResponse.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id").GetGuid();

        var updateResponse = await client.PostAsJsonAsync("/api/legal-entity/profile", new
        {
            Name = "ACME Holdings",
            Edrpou = "12345678",
            DirectorName = "Іван Петренко"
        });
        updateResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var updatedId = (await updateResponse.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id").GetGuid();

        // Upsert keys on UserId — the same account must update its single row, not create a second.
        updatedId.Should().Be(createdId);

        using var scope = _fixture.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var userId = await UserIdAsync(context, phone);
        var entities = await context.LegalEntities.Where(e => e.UserId == userId).ToListAsync();
        entities.Should().ContainSingle();
        entities[0].Name.Should().Be("ACME Holdings");
        entities[0].DirectorName.Should().Be("Іван Петренко");
    }

    [Fact]
    public async Task UpsertProfile_Returns400_WhenNameMissing()
    {
        await ResetAsync();
        var client = await AuthenticatedClientAsync("+380000000003");

        var response = await client.PostAsJsonAsync("/api/legal-entity/profile", new
        {
            Name = "",
            Edrpou = "12345678"
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task UpsertProfile_Returns400_WhenEdrpouMissing()
    {
        await ResetAsync();
        var client = await AuthenticatedClientAsync("+380000000004");

        var response = await client.PostAsJsonAsync("/api/legal-entity/profile", new
        {
            Name = "ACME LLC",
            Edrpou = ""
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task UpsertProfile_Returns409_WhenEdrpouRegisteredToAnotherUser()
    {
        await ResetAsync();

        // First account claims the EDRPOU.
        var owner = await AuthenticatedClientAsync("+380000000005");
        var claim = await owner.PostAsJsonAsync("/api/legal-entity/profile", new
        {
            Name = "ACME LLC",
            Edrpou = "12345678"
        });
        claim.StatusCode.Should().Be(HttpStatusCode.OK);

        // A different account attempts to register the same EDRPOU — the unique index on
        // legal_entities.edrpou must surface as a 409 Conflict, not a 500.
        var other = await AuthenticatedClientAsync("+380000000006");
        var conflict = await other.PostAsJsonAsync("/api/legal-entity/profile", new
        {
            Name = "Beta LLC",
            Edrpou = "12345678"
        });

        conflict.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task UpsertProfile_Returns400_WhenEdrpouIsNotEightDigits()
    {
        // #128: a company was created with a 16-digit EDRPOU because the only gate was "not empty".
        // This is that exact value, refused over HTTP.
        await ResetAsync();
        var client = await AuthenticatedClientAsync("+380000000010");

        var response = await client.PostAsJsonAsync("/api/legal-entity/profile", new
        {
            Name = "QA company name",
            Edrpou = "1234567890456565"
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // And nothing was written - a refused format must not leave a half-created entity behind.
        using var scope = _fixture.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        (await context.LegalEntities.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Create_Returns400_WhenEdrpouIsNotEightDigits()
    {
        // The multi-company create path carries its own gate, not only the profile upsert.
        await ResetAsync();
        var client = await AuthenticatedClientAsync("+380000000011");

        var response = await client.PostAsJsonAsync("/api/legal-entity", new
        {
            Name = "QA company name",
            Edrpou = "1234567890456565"
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Update_Returns400_WhenEdrpouIsNotEightDigits()
    {
        // Editing an existing entity must not become a way around the gate.
        await ResetAsync();
        var client = await AuthenticatedClientAsync("+380000000012");

        var created = await client.PostAsJsonAsync("/api/legal-entity/profile", new
        {
            Name = "ACME LLC",
            Edrpou = "12345678"
        });
        created.StatusCode.Should().Be(HttpStatusCode.OK);
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var response = await client.PutAsJsonAsync($"/api/legal-entity/{id}", new
        {
            Name = "ACME LLC",
            Edrpou = "1234567890456565"
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // The stored code is untouched.
        using var scope = _fixture.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        (await context.LegalEntities.SingleAsync(e => e.Id == id)).Edrpou.Should().Be("12345678");
    }

    [Fact]
    public async Task UpsertProfile_Accepts_VatWhenValid_AndRejectsWhenMalformed()
    {
        await ResetAsync();
        var client = await AuthenticatedClientAsync("+380000000013");

        var ok = await client.PostAsJsonAsync("/api/legal-entity/profile", new
        {
            Name = "ACME LLC",
            Edrpou = "12345678",
            VatNumber = "123456789012"
        });
        ok.StatusCode.Should().Be(HttpStatusCode.OK);

        var rejected = await client.PostAsJsonAsync("/api/legal-entity/profile", new
        {
            Name = "ACME LLC",
            Edrpou = "12345678",
            VatNumber = "12345"
        });
        rejected.StatusCode.Should().Be(HttpStatusCode.BadRequest);
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
