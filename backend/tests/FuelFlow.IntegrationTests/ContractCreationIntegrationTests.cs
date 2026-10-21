using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using FuelFlow.Features.Contracts.SharedModels;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FuelFlow.IntegrationTests;

/// <summary>
/// The contracts feature had no writer and no text: nothing created a contract row, so
/// <c>GET /api/legal-entity/contracts</c> answered with an empty list for every customer,
/// always, and the mobile's "REVIEW THE TEXT" had nothing behind it. The owner decided on
/// 2026-10-21 that a company is created with a contract for every provider, that the text
/// lives once per provider and is versioned, and that a contract says which version it was
/// signed against.
/// <para>
/// These tests are about that decision being true, end to end: a company that comes into
/// existence comes into existence with contracts, the list carries the text, and a signature
/// records the version it was given against.
/// </para>
/// </summary>
[Collection("Integration Tests")]
public sealed class ContractCreationIntegrationTests : IClassFixture<TestDatabaseFixture>
{
    private readonly TestDatabaseFixture _fixture;

    public ContractCreationIntegrationTests(TestDatabaseFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task A_Company_Comes_With_A_Contract_For_Every_Provider_That_Has_A_Text()
    {
        await ResetAsync();
        var client = await AuthenticatedClientAsync("+380000000201");
        await SeedProviderTextAsync("okko", "Terms of OKKO, version 1.", 30);
        await SeedProviderTextAsync("wog", "Terms of WOG, version 1.", 90);

        var profile = await client.PostAsJsonAsync("/api/legal-entity/profile", new
        {
            Name = "ACME LLC",
            Edrpou = "12345678"
        });
        if (!profile.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                "the profile was refused: " + await profile.Content.ReadAsStringAsync());
        }

        var contracts = await client.GetFromJsonAsync<JsonElement>("/api/legal-entity/contracts");
        var rows = contracts.EnumerateArray().ToList();

        // One contract per provider WITH a text - and no more. A provider nobody has written
        // terms for gets no contract: an empty document with a sign button is worse than no
        // document, because it looks like something to sign.
        rows.Should().HaveCount(2);
        rows.Select(r => r.GetProperty("brandName").GetString())
            .Should().BeEquivalentTo(["OKKO", "WOG"]);

        // The term the text implies, not a term of our choosing: OKKO's text said 30 days.
        var okko = rows.Single(r => r.GetProperty("brandName").GetString() == "OKKO");
        var wog = rows.Single(r => r.GetProperty("brandName").GetString() == "WOG");
        var okkoExpiry = okko.GetProperty("expiresAt").GetDateTime();
        var wogExpiry = wog.GetProperty("expiresAt").GetDateTime();
        okkoExpiry.Should().BeCloseTo(DateTime.UtcNow.AddDays(30), TimeSpan.FromMinutes(2));
        wogExpiry.Should().BeCloseTo(DateTime.UtcNow.AddDays(90), TimeSpan.FromMinutes(2));
    }

    [Fact]
    public async Task The_List_Carries_The_Text_The_Customer_Is_Asked_To_Read()
    {
        await ResetAsync();
        var client = await AuthenticatedClientAsync("+380000000202");
        await SeedProviderTextAsync("okko", "The terms the customer reads before signing.", 30);

        await client.PostAsJsonAsync("/api/legal-entity/profile", new
        {
            Name = "ACME LLC",
            Edrpou = "12345678"
        });

        var contracts = await client.GetFromJsonAsync<JsonElement>("/api/legal-entity/contracts");
        var row = contracts.EnumerateArray().Should().ContainSingle().Subject;

        // Before this, the screen's "REVIEW THE TEXT" had nothing behind it.
        row.GetProperty("termsText").GetString().Should().Be("The terms the customer reads before signing.");
        row.GetProperty("hasText").GetBoolean().Should().BeTrue();
        row.GetProperty("termsVersion").GetInt32().Should().Be(1);
    }

    [Fact]
    public async Task A_Signature_Records_The_Version_It_Was_Given_Against()
    {
        await ResetAsync();
        var client = await AuthenticatedClientAsync("+380000000203");
        await SeedProviderTextAsync("okko", "Terms of OKKO, version 1.", 30);

        await client.PostAsJsonAsync("/api/legal-entity/profile", new
        {
            Name = "ACME LLC",
            Edrpou = "12345678"
        });

        var contracts = await client.GetFromJsonAsync<JsonElement>("/api/legal-entity/contracts");
        var contractId = contracts.EnumerateArray().Should().ContainSingle().Subject.GetProperty("id").GetGuid();

        var signed = await client.PostAsJsonAsync("/api/legal-entity/sign", new
        {
            contractId,
            signatureData = "data:image/png;base64,SIG"
        });
        signed.StatusCode.Should().Be(HttpStatusCode.OK);

        using var scope = _fixture.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var signature = await context.UserContracts.SingleAsync();
        signature.TermsVersion.Should().Be(1);
    }

    [Fact]
    public async Task A_Provider_With_No_Text_Gets_No_Contract()
    {
        await ResetAsync();
        var client = await AuthenticatedClientAsync("+380000000204");
        await SeedProviderTextAsync("okko", "Terms of OKKO, version 1.", 30);

        // WOG exists as a provider, but nobody has written its terms.
        using (var scope = _fixture.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            context.Brands.Add(new Brand
            {
                Id = Guid.NewGuid(),
                Slug = "wog",
                Name = "WOG",
                Color = "#00ff80",
                LogoText = "WOG",
                CreatedAtUtc = DateTime.UtcNow
            });
            await context.SaveChangesAsync();
        }

        await client.PostAsJsonAsync("/api/legal-entity/profile", new
        {
            Name = "ACME LLC",
            Edrpou = "12345678"
        });

        var contracts = await client.GetFromJsonAsync<JsonElement>("/api/legal-entity/contracts");
        var rows = contracts.EnumerateArray().ToList();

        rows.Should().HaveCount(1);
        rows[0].GetProperty("brandName").GetString().Should().Be("OKKO");
    }

    private async Task SeedProviderTextAsync(string slug, string body, int termDays)
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
                CreatedAtUtc = DateTime.UtcNow
            };
            context.Brands.Add(brand);
        }

        context.ContractVersions.Add(new ContractVersion
        {
            Id = Guid.NewGuid(),
            BrandId = brand.Id,
            Version = 1,
            Body = body,
            TermDays = termDays,
            CreatedAtUtc = DateTime.UtcNow
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
