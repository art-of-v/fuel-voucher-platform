using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using FluentAssertions;
using FuelFlow.Features.Contracts.SharedModels;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using FuelFlow.Testing;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FuelFlow.IntegrationTests;

/// <summary>
/// A customer's handwritten signature is a biometric artefact: it cannot be revoked and it cannot
/// be rotated, so a leaked copy of a person's handwriting is not repairable. It is therefore a
/// capability of its own, narrower than the staff read surface - the same answer the redeemable
/// voucher payload got (planning #622, S-13).
/// </summary>
/// <remarks>
/// The endpoint used to answer with EVERY signature on the platform at once, each row carrying the
/// customer's signature in base64 and the customer's phone number beside it, on the Staff policy -
/// one call was a harvest of the whole customer base. The image is now behind
/// <c>AuthPolicies.SignaturePayload</c>, the list carries no image at all, the list is a page, and
/// opening an image writes an audit row naming the operator.
/// </remarks>
[Collection("Integration Tests")]
public sealed class SignaturePayloadCapabilityTests : IClassFixture<TestDatabaseFixture>
{
    private readonly TestDatabaseFixture _fixture;

    public SignaturePayloadCapabilityTests(TestDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task A_Manager_Is_Refused_By_The_Signed_Contracts_Endpoints()
    {
        var signatureId = await SeedSignedContractAsync();
        var (manager, _) = await StaffClientAsync("+15550009201", SeedRoles.ManagerName);

        var list = await manager.GetAsync("/api/admin/legal-entity/contracts/signed-contracts");
        list.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "the signed-contracts list is behind the SignaturePayload capability, and a Manager "
            + "does not hold it");

        var detail = await manager.GetAsync(
            $"/api/admin/legal-entity/contracts/signed-contracts/{signatureId}");
        detail.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "the signature image is behind the SignaturePayload capability, and a Manager does "
            + "not hold it");
    }

    [Fact]
    public async Task The_Capability_That_Refuses_A_Manager_Is_The_Signature_Payload_One()
    {
        // Which policy refused it. The endpoint carries one policy, and the test names it: a
        // refusal nobody can attribute is a refusal nobody can fix.
        using var scope = _fixture.Services.CreateScope();
        var authorization = scope.ServiceProvider.GetRequiredService<IAuthorizationService>();

        var manager = new ClaimsPrincipal(
            new ClaimsIdentity(
                [new Claim(ClaimTypes.Role, SeedRoles.ManagerName)],
                authenticationType: "Test"));

        (await authorization.AuthorizeAsync(manager, null, AuthPolicies.SignaturePayload))
            .Succeeded.Should().BeFalse(
                "the SignaturePayload policy is what refuses the Manager, and it admits "
                + "ProductOwner and Admin only");

        var admin = new ClaimsPrincipal(
            new ClaimsIdentity(
                [new Claim(ClaimTypes.Role, SeedRoles.AdminName)],
                authenticationType: "Test"));

        (await authorization.AuthorizeAsync(admin, null, AuthPolicies.SignaturePayload))
            .Succeeded.Should().BeTrue("the Admin tier holds the signature capability");
    }

    [Fact]
    public async Task The_List_Carries_No_Signature_Image()
    {
        await SeedSignedContractAsync();
        var (admin, _) = await StaffClientAsync("+15550009202", SeedRoles.AdminName);

        var response = await admin.GetAsync("/api/admin/legal-entity/contracts/signed-contracts");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(body);
        var rows = document.RootElement.GetProperty("items");
        rows.GetArrayLength().Should().BeGreaterThan(0, "the fixture seeded a signature");

        foreach (var row in rows.EnumerateArray())
        {
            row.TryGetProperty("signatureData", out _).Should().BeFalse(
                "one list call must not hand out every customer's handwriting");
        }

        body.Should().NotContain("signatureData");
    }

    [Fact]
    public async Task The_List_Is_A_Page_And_Not_The_Whole_Table()
    {
        await SeedSignedContractAsync();
        await SeedSignedContractAsync();
        var (admin, _) = await StaffClientAsync("+15550009203", SeedRoles.AdminName);

        var response = await admin.GetAsync(
            "/api/admin/legal-entity/contracts/signed-contracts?page=1&limit=1");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;

        root.GetProperty("items").GetArrayLength().Should().Be(1, "the limit is the limit");
        root.GetProperty("totalCount").GetInt32().Should().BeGreaterThanOrEqualTo(2,
            "the count is the size of the table, not of the page");
        root.GetProperty("page").GetInt32().Should().Be(1);
        root.GetProperty("pageSize").GetInt32().Should().Be(1);
    }

    [Fact]
    public async Task The_Detail_Carries_The_Image_And_Writes_An_Audit_Row()
    {
        var signatureId = await SeedSignedContractAsync();
        var (admin, adminUserId) = await StaffClientAsync("+15550009204", SeedRoles.AdminName);

        var response = await admin.GetAsync(
            $"/api/admin/legal-entity/contracts/signed-contracts/{signatureId}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;

        root.GetProperty("signatureData").GetString().Should().Be(SignatureImage);
        root.GetProperty("termsVersion").GetInt32().Should().Be(1);

        // The audit row: who opened the image. A capability says who may look; the row says who
        // did, and a signature that cannot be rotated is exactly the artefact where that
        // difference matters.
        using var scope = _fixture.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var auditRow = await context.AuditEventOutbox
            .Where(a => a.EventType == "SignatureViewed" && a.AggregateId == signatureId.ToString())
            .FirstOrDefaultAsync();

        auditRow.Should().NotBeNull("opening a signature writes an audit row");
        auditRow!.ChangedByUserId.Should().Be(adminUserId,
            "the row names the operator who opened the image");
        auditRow.AggregateType.Should().Be("ContractSignature");

        // The audit trail is read by a wider surface than the capability, so the row must not
        // carry the image itself - that would hand it out a second time.
        auditRow.NewValue.Should().NotContain(SignatureImage);
    }

    [Fact]
    public async Task A_Signature_That_Is_Not_There_Is_Not_Found()
    {
        var (admin, _) = await StaffClientAsync("+15550009205", SeedRoles.AdminName);

        var response = await admin.GetAsync(
            $"/api/admin/legal-entity/contracts/signed-contracts/{Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    private const string SignatureImage = "[\"M0,0 L10,10\"]";

    /// <summary>
    /// A customer, a company, a provider, a contract and a signature on it - the whole chain the
    /// admin list reads.
    /// </summary>
    private async Task<Guid> SeedSignedContractAsync()
    {
        using var scope = _fixture.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var now = TestClock.UtcNow;

        var brand = await context.Brands.FirstOrDefaultAsync(b => b.Slug == "okko");
        if (brand is null)
        {
            brand = new Brand
            {
                Id = Guid.NewGuid(),
                Slug = "okko",
                Name = "OKKO",
                Color = "#00ff80",
                LogoText = "OKKO",
                CreatedAtUtc = now
            };
            context.Brands.Add(brand);
            await context.SaveChangesAsync();
        }

        var customer = new User
        {
            Id = Guid.NewGuid(),
            // The phone number and the EDRPOU are both unique columns, and this fixture is
            // shared with the other integration test classes - so the values are drawn rather
            // than fixed, or the second seeded company collides with the first.
            PhoneNumber = $"+3809{Random.Shared.Next(10_000_000, 99_999_999)}",
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            IsActive = true,
            TokenVersion = 1
        };
        context.Users.Add(customer);

        var legalEntity = new LegalEntity
        {
            Id = Guid.NewGuid(),
            UserId = customer.Id,
            Name = "ACME LLC",
            Edrpou = Random.Shared.Next(10_000_000, 99_999_999).ToString(),
            CreatedAtUtc = now
        };
        context.LegalEntities.Add(legalEntity);

        var contract = new Contract
        {
            Id = Guid.NewGuid(),
            UserId = customer.Id,
            LegalEntityId = legalEntity.Id,
            BrandId = brand.Id,
            CreatedAtUtc = now,
            ExpiresAtUtc = now.AddYears(1),
            TermsVersion = 1
        };
        context.Contracts.Add(contract);

        var signature = new UserContract
        {
            Id = Guid.NewGuid(),
            UserId = customer.Id,
            ContractId = contract.Id,
            SignatureData = SignatureImage,
            SignedAtUtc = now,
            TermsVersion = 1
        };
        context.UserContracts.Add(signature);

        await context.SaveChangesAsync();
        return signature.Id;
    }

    /// <summary>
    /// A user with the given role, created with it - so the token the login mints carries the role
    /// claim the capability policies check.
    /// </summary>
    private async Task<Guid> SeedUserAsync(string phone, string roleName)
    {
        using var scope = _fixture.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var role = await context.Roles.FirstAsync(r => r.Name == roleName);
        var now = TestClock.UtcNow;

        var user = new User
        {
            Id = Guid.NewGuid(),
            PhoneNumber = phone,
            RoleId = role.Id,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            IsActive = true,
            TokenVersion = 1,
        };
        context.Users.Add(user);
        await context.SaveChangesAsync();
        return user.Id;
    }

    /// <summary>
    /// A staff client and the id of the account behind it - the audit row has to name that
    /// account, so the test has to know it.
    /// </summary>
    private async Task<(HttpClient Client, Guid UserId)> StaffClientAsync(string phone, string roleName)
    {
        var userId = await SeedUserAsync(phone, roleName);

        var client = _fixture.CreateClient();

        var sendCode = await client.PostAsJsonAsync("/api/auth/send-code", new { phoneNumber = phone });
        sendCode.EnsureSuccessStatusCode();

        var verify = await client.PostAsJsonAsync("/api/auth/verify", new { phoneNumber = phone, code = "000000" });
        verify.EnsureSuccessStatusCode();
        var token = (await verify.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("accessToken").GetString();

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return (client, userId);
    }
}
