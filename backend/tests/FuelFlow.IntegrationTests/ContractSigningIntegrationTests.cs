using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using FuelFlow.Features.Contracts.SharedModels;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using FuelFlow.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FuelFlow.IntegrationTests;

/// <summary>
/// HTTP full-stack coverage for <c>LegalEntityController.Sign</c> - the one endpoint that records
/// a legal act.
/// <para>
/// The body this endpoint binds is <c>{ contractId, signatureData }</c>. For as long as the
/// mobile client existed it posted <c>{ contractIds: [...], signatureData, stationId }</c>: the
/// array bound nothing, <c>ContractId</c> stayed <c>Guid.Empty</c>, and every call was a 400
/// before a single row was written. The client's own test suite agreed with the client - its
/// fixture was built from the client's types, so the mock server and the screen were happy
/// together and neither was checked against the controller.
/// </para>
/// <para>
/// So the fixture here is the SERVER's shape, and the first test posts the client's exact body.
/// A test whose fixture is the client is not evidence about the server.
/// </para>
/// </summary>
[Collection("Integration Tests")]
public sealed class ContractSigningIntegrationTests : IClassFixture<TestDatabaseFixture>
{
    private readonly TestDatabaseFixture _fixture;

    public ContractSigningIntegrationTests(TestDatabaseFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task The_Clients_Exact_Body_Binds_And_The_Signature_Lands()
    {
        await ResetAsync();
        var client = await AuthenticatedClientAsync("+380000000101");
        var contractId = await SeedContractAsync(client, "+380000000101", "okko");

        // The exact body the mobile client posts.
        var response = await client.PostAsJsonAsync("/api/legal-entity/sign", new
        {
            contractId,
            signatureData = "data:image/png;base64,SIG"
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var scope = _fixture.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var userId = await UserIdAsync(context, "+380000000101");

        var signature = await context.UserContracts.SingleAsync(uc => uc.UserId == userId);
        signature.ContractId.Should().Be(contractId);
        signature.SignatureData.Should().Be("data:image/png;base64,SIG");
        signature.SignedAtUtc.Should().BeCloseTo(TestClock.UtcNow, TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task The_Body_That_Never_Bound_Is_Still_Refused()
    {
        await ResetAsync();
        var client = await AuthenticatedClientAsync("+380000000102");
        var contractId = await SeedContractAsync(client, "+380000000102", "wog");

        // The shape the client used to post: an array of ids and a station. Nothing binds,
        // so the request is a 400 and no signature is written.
        var response = await client.PostAsJsonAsync("/api/legal-entity/sign", new
        {
            contractIds = new[] { contractId },
            signatureData = "data:image/png;base64,SIG",
            stationId = "wog"
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        using var scope = _fixture.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        (await context.UserContracts.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task The_Signed_List_Carries_The_Brand_The_Client_Matches_On()
    {
        await ResetAsync();
        var client = await AuthenticatedClientAsync("+380000000103");
        var contractId = await SeedContractAsync(client, "+380000000103", "klo");
        var brand = await BrandAsync("klo");

        await client.PostAsJsonAsync("/api/legal-entity/sign", new
        {
            contractId,
            signatureData = "data:image/png;base64,SIG"
        });

        var signed = await client.GetFromJsonAsync<JsonElement>("/api/legal-entity/contracts/signed");
        var row = signed.EnumerateArray().Should().ContainSingle().Subject;

        // The brand travels by id, not only by name: the screen decides "is this brand
        // already signed?" by comparing ids, because two brands may share a name.
        row.GetProperty("brandId").GetGuid().Should().Be(brand.Id);
        row.GetProperty("brandName").GetString().Should().Be(brand.Name);
        row.GetProperty("contractId").GetGuid().Should().Be(contractId);
    }

    [Fact]
    public async Task The_Contracts_List_Answers_At_Its_Own_Path()
    {
        // The route was lost on 2026-10-01 and never restored: the action bound to the
        // controller's own prefix, so the mobile app's GET /api/legal-entity/contracts was a 404 -
        // and the list of contracts to sign was empty for every customer, for a reason no screen
        // could show.
        var client = await AuthenticatedClientAsync("+15550007009");

        var response = await client.GetAsync("/api/legal-entity/contracts");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.ValueKind.Should().Be(JsonValueKind.Array);
    }

    /// <summary>
    /// A brand, a legal entity and a contract the caller owns - the three rows a signature
    /// needs, none of which the platform creates on its own (nothing writes a
    /// <c>contracts</c> row; see planning#375).
    /// </summary>
    private async Task<Guid> SeedContractAsync(HttpClient client, string phone, string brandSlug)
    {
        var profile = await client.PostAsJsonAsync("/api/legal-entity/profile", new
        {
            Name = "ACME LLC",
            // A code whose control digit passes the format gate (#266) - "38528174" does not,
            // and the gate would answer 400 before a contract is ever in reach.
            Edrpou = "12345678"
        });
        profile.EnsureSuccessStatusCode();
        var legalEntityId = (await profile.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id").GetGuid();

        using var scope = _fixture.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var userId = await UserIdAsync(context, phone);

        var brand = await context.Brands.FirstOrDefaultAsync(b => b.Slug == brandSlug);
        if (brand is null)
        {
            brand = new Brand
            {
                Id = Guid.NewGuid(),
                Slug = brandSlug,
                Name = brandSlug.ToUpperInvariant(),
                Color = "#00ff80",
                LogoText = brandSlug.ToUpperInvariant(),
                CreatedAtUtc = TestClock.UtcNow
            };
            context.Brands.Add(brand);
        }

        var contract = new Contract
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            LegalEntityId = legalEntityId,
            BrandId = brand.Id,
            CreatedAtUtc = TestClock.UtcNow,
            ExpiresAtUtc = TestClock.UtcNow.AddDays(30)
        };
        context.Contracts.Add(contract);
        await context.SaveChangesAsync();

        return contract.Id;
    }

    private async Task<Brand> BrandAsync(string slug)
    {
        using var scope = _fixture.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await context.Brands.AsNoTracking().SingleAsync(b => b.Slug == slug);
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
            """
            TRUNCATE TABLE "user_contracts", "contracts", "brands", "payments", "fulfillments",
                "orders", "order_line_items", "outbox_events", "fuel_vouchers", "company_invitations",
                "company_members", "legal_entities", "verification_codes", "users",
                "provider_event_outbox", "app_settings" RESTART IDENTITY CASCADE
            """);
    }
}
