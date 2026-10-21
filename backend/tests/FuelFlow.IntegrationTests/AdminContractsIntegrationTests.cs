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
/// The admin's contracts tab, read end to end.
/// </summary>
/// <remarks>
/// <para>
/// The tab used to read five fields the server had never returned - <c>title</c>,
/// <c>version</c>, <c>status</c>, <c>userName</c>, <c>companyName</c>, <c>contractTitle</c> -
/// and it called <c>contract.status.toLowerCase()</c>, so the first real contract threw the
/// whole tab over. The endpoints answered with EF entity graphs, which is the item left open
/// in the security audit ("Project DTOs instead of EF entities across the rest of the API").
/// </para>
/// <para>
/// These tests are about the screen having something true to read: the provider, the company,
/// the customer, the term, the version of the terms, and whether it was signed - and about
/// the fields that never existed being gone for good.
/// </para>
/// </remarks>
[Collection("Integration Tests")]
public sealed class AdminContractsIntegrationTests : IClassFixture<TestDatabaseFixture>
{
    private readonly TestDatabaseFixture _fixture;

    public AdminContractsIntegrationTests(TestDatabaseFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task The_Admin_List_Says_What_The_Screen_Reads()
    {
        await ResetAsync();
        await SeedProviderTextAsync("okko", "Terms of OKKO, version 1.", 30);

        var customer = await AuthenticatedClientAsync("+380000000301");
        await customer.PostAsJsonAsync("/api/legal-entity/profile", new
        {
            Name = "ACME LLC",
            Edrpou = "12345678"
        });

        var staff = await StaffClientAsync("+380000000302");
        var contracts = await staff.GetFromJsonAsync<JsonElement>("/api/admin/legal-entity/contracts");
        var row = contracts.EnumerateArray().Should().ContainSingle().Subject;

        row.GetProperty("brandName").GetString().Should().Be("OKKO");
        row.GetProperty("companyName").GetString().Should().Be("ACME LLC");
        // The customer is named by their phone number: nobody gave a name.
        row.GetProperty("customerName").GetString().Should().Be("+380000000301");
        row.GetProperty("termsVersion").GetInt32().Should().Be(1);
        row.GetProperty("expired").GetBoolean().Should().BeFalse();
        row.GetProperty("signed").GetBoolean().Should().BeFalse();
        row.GetProperty("signedAtUtc").ValueKind.Should().Be(JsonValueKind.Null);
        row.GetProperty("expiresAtUtc").GetDateTime()
            .Should().BeCloseTo(TestClock.UtcNow.AddDays(30), TimeSpan.FromMinutes(2));

        // The fields the tab used to read and the server never had. They are gone, so the
        // next person who writes a tab cannot read them again.
        row.TryGetProperty("title", out _).Should().BeFalse();
        row.TryGetProperty("status", out _).Should().BeFalse();
        row.TryGetProperty("version", out _).Should().BeFalse();
    }

    [Fact]
    public async Task The_Admin_List_Says_That_A_Signed_Contract_Is_Signed()
    {
        await ResetAsync();
        await SeedProviderTextAsync("okko", "Terms of OKKO, version 1.", 30);

        var customer = await AuthenticatedClientAsync("+380000000303");
        await customer.PostAsJsonAsync("/api/legal-entity/profile", new
        {
            Name = "ACME LLC",
            Edrpou = "12345678"
        });

        var contracts = await customer.GetFromJsonAsync<JsonElement>("/api/legal-entity/contracts");
        var contractId = contracts.EnumerateArray().Should().ContainSingle().Subject.GetProperty("id").GetGuid();
        var signed = await customer.PostAsJsonAsync("/api/legal-entity/sign", new
        {
            contractId,
            signatureData = "[\"M0,0 L10,10\"]"
        });
        signed.StatusCode.Should().Be(HttpStatusCode.OK);

        var staff = await StaffClientAsync("+380000000304");
        var adminContracts = await staff.GetFromJsonAsync<JsonElement>("/api/admin/legal-entity/contracts");
        var row = adminContracts.EnumerateArray().Should().ContainSingle().Subject;

        row.GetProperty("signed").GetBoolean().Should().BeTrue();
        row.GetProperty("signedAtUtc").ValueKind.Should().Be(JsonValueKind.String);
    }

    [Fact]
    public async Task The_Signed_List_Says_Who_Signed_What()
    {
        await ResetAsync();
        await SeedProviderTextAsync("okko", "Terms of OKKO, version 1.", 30);

        var customer = await AuthenticatedClientAsync("+380000000305");
        await customer.PostAsJsonAsync("/api/legal-entity/profile", new
        {
            Name = "ACME LLC",
            Edrpou = "12345678"
        });

        var contracts = await customer.GetFromJsonAsync<JsonElement>("/api/legal-entity/contracts");
        var contractId = contracts.EnumerateArray().Should().ContainSingle().Subject.GetProperty("id").GetGuid();
        await customer.PostAsJsonAsync("/api/legal-entity/sign", new
        {
            contractId,
            signatureData = "[\"M0,0 L10,10\"]"
        });

        var staff = await StaffClientAsync("+380000000306");
        var signedContracts = await staff.GetFromJsonAsync<JsonElement>(
            "/api/admin/legal-entity/contracts/signed-contracts");
        var row = signedContracts.EnumerateArray().Should().ContainSingle().Subject;

        row.GetProperty("brandName").GetString().Should().Be("OKKO");
        row.GetProperty("companyName").GetString().Should().Be("ACME LLC");
        row.GetProperty("customerName").GetString().Should().Be("+380000000305");
        row.GetProperty("contractId").GetGuid().Should().Be(contractId);
        // The version that was signed - what the customer actually read.
        row.GetProperty("termsVersion").GetInt32().Should().Be(1);
        row.GetProperty("signatureData").GetString().Should().Be("[\"M0,0 L10,10\"]");
        row.GetProperty("signedAtUtc").ValueKind.Should().Be(JsonValueKind.String);

        // The three fields the tab read and the server never returned.
        row.TryGetProperty("userName", out _).Should().BeFalse();
        row.TryGetProperty("contractTitle", out _).Should().BeFalse();
    }

    [Fact]
    public async Task Publishing_A_Text_Gives_The_Companies_That_Already_Existed_A_Contract()
    {
        await ResetAsync();

        // The company comes first, while no provider has published anything: it has nothing
        // to sign yet.
        var customer = await AuthenticatedClientAsync("+380000000307");
        await customer.PostAsJsonAsync("/api/legal-entity/profile", new
        {
            Name = "ACME LLC",
            Edrpou = "12345678"
        });

        var before = await customer.GetFromJsonAsync<JsonElement>("/api/legal-entity/contracts");
        before.EnumerateArray().Should().BeEmpty();

        // The operator publishes the terms. The company that already existed gets a contract:
        // the owner's answer to "do the companies that already existed get contracts too?".
        var staff = await StaffClientAsync("+380000000308");
        var published = await staff.PostAsJsonAsync("/api/admin/contract-versions", new
        {
            brandId = await BrandIdAsync("okko"),
            body = "Terms of OKKO, version 1.",
            termDays = 30
        });
        published.StatusCode.Should().Be(HttpStatusCode.OK);
        (await published.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("contractsCreated").GetInt32().Should().Be(1);

        var after = await customer.GetFromJsonAsync<JsonElement>("/api/legal-entity/contracts");
        var row = after.EnumerateArray().Should().ContainSingle().Subject;
        row.GetProperty("brandName").GetString().Should().Be("OKKO");
        row.GetProperty("termsText").GetString().Should().Be("Terms of OKKO, version 1.");
    }

    private async Task<Guid> BrandIdAsync(string slug)
    {
        using var scope = _fixture.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var brand = await context.Brands.FirstOrDefaultAsync(b => b.Slug == slug);
        if (brand is null)
        {
            brand = new Brand
            {
                Id = Guid.NewGuid(),
                Slug = slug,
                Name = slug.ToUpperInvariant(),
                Color = "#00ff80",
                LogoText = slug.ToUpperInvariant(),
                CreatedAtUtc = TestClock.UtcNow
            };
            context.Brands.Add(brand);
            await context.SaveChangesAsync();
        }

        return brand.Id;
    }

    private async Task SeedProviderTextAsync(string slug, string body, int termDays)
    {
        var brandId = await BrandIdAsync(slug);

        using var scope = _fixture.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        context.ContractVersions.Add(new ContractVersion
        {
            Id = Guid.NewGuid(),
            BrandId = brandId,
            Version = 1,
            Body = body,
            TermDays = termDays,
            CreatedAtUtc = TestClock.UtcNow
        });

        await context.SaveChangesAsync();
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

    /// <summary>
    /// A staff client: the user is seeded WITH its role, so the token carries the role claim
    /// the "Staff" policy checks.
    /// </summary>
    private async Task<HttpClient> StaffClientAsync(string phoneNumber)
    {
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

        return await AuthenticatedClientAsync(phoneNumber);
    }

    private async Task ResetAsync()
    {
        using var scope = _fixture.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await context.Database.ExecuteSqlRawAsync(
            """
            TRUNCATE TABLE "user_contracts", "contract_versions", "contracts", "brands", "payments",
            "fulfillments", "orders", "order_line_items", "outbox_events", "fuel_vouchers",
            "company_invitations", "company_members", "legal_entities", "verification_codes", "users",
            "provider_event_outbox", "app_settings" RESTART IDENTITY CASCADE
            """);
    }
}
