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
/// The settlement code is money data: it is the identifier the settlement channel resolves a
/// payment to, and it is what an operator quotes when they place a customer's payment. So it is a
/// capability of its own (<c>AuthPolicies.SettlementCode</c>) and not a field of the staff read
/// surface.
/// </summary>
/// <remarks>
/// <para>
/// The finding behind this (planning #530, S-10) was written when the staff role QA still stood
/// for "any staff": every staff role could read every customer's settlement code, and the QA
/// account was one of them. That role left the staff surface with planning #521 (S-1) — what was
/// missing was the proof, and the acceptance criterion the finding names: a QA token receives no
/// settlement code.
/// </para>
/// <para>
/// The second half of the finding is the workflow: the operator reads the code in the users grid
/// and searches by it, because a customer quotes the code rather than their name. The tests below
/// pin both halves, so a narrowing of the capability that breaks the settlement channel shows up
/// as a failure rather than as a support ticket.
/// </para>
/// </remarks>
[Collection("Integration Tests")]
public sealed class SettlementCodeCapabilityTests : IClassFixture<TestDatabaseFixture>
{
    private readonly TestDatabaseFixture _fixture;

    public SettlementCodeCapabilityTests(TestDatabaseFixture fixture) => _fixture = fixture;

    /// <summary>
    /// The acceptance criterion of the finding: a QA token does not receive the settlement code.
    /// </summary>
    [Fact]
    public async Task A_Qa_Token_Receives_No_Settlement_Code()
    {
        var code = await SeedCustomerAsync();
        var qa = await ClientWithRoleAsync("+15550009501", SeedRoles.QaName);

        // The staff list that used to carry every customer's code.
        var users = await qa.GetAsync($"/api/admin/users?q={code}");
        users.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "the retired QA staff role is admitted by no policy - it is not staff any more");
        (await users.Content.ReadAsStringAsync()).Should().NotContain(code);

        // The endpoints that serve the code: the picker, the lookup by code, and the worklist whose
        // rows carry the code a message declared.
        var picker = await qa.GetAsync($"/api/admin/payments/customers?search={code}");
        picker.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await picker.Content.ReadAsStringAsync()).Should().NotContain(code);

        var byCode = await qa.GetAsync($"/api/admin/payments/customers/by-code?code={code}");
        byCode.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await byCode.Content.ReadAsStringAsync()).Should().NotContain(code);

        var unallocated = await qa.GetAsync("/api/admin/payments/unallocated");
        unallocated.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await unallocated.Content.ReadAsStringAsync()).Should().NotContain(code);
    }

    /// <summary>
    /// The workflow the code exists for: the operator reads it in the users grid, and the
    /// settlement channel resolves a payment by it.
    /// </summary>
    [Fact]
    public async Task The_Money_Tier_Reads_The_Code_Where_It_Is_Needed()
    {
        var code = await SeedCustomerAsync();
        var manager = await ClientWithRoleAsync("+15550009502", SeedRoles.ManagerName);

        // The grid. The search is the code itself, because a customer quotes the code and not
        // their name.
        var users = await manager.GetAsync($"/api/admin/users?q={code}");
        users.StatusCode.Should().Be(HttpStatusCode.OK);

        using var document = JsonDocument.Parse(await users.Content.ReadAsStringAsync());
        var items = document.RootElement.GetProperty("items").EnumerateArray().ToList();
        items.Should().HaveCount(1, "the search is the code, and one customer has it");
        items[0].GetProperty("settlementCode").GetString().Should().Be(code);

        // The picker, which offers the customer to the operator who places a held payment.
        var picker = await manager.GetAsync($"/api/admin/payments/customers?search={code}");
        picker.StatusCode.Should().Be(HttpStatusCode.OK);
        (await picker.Content.ReadAsStringAsync()).Should().Contain(code);

        // The lookup: "is this a typo, or a code for a customer who does not exist?"
        var byCode = await manager.GetAsync($"/api/admin/payments/customers/by-code?code={code}");
        byCode.StatusCode.Should().Be(HttpStatusCode.OK);
        (await byCode.Content.ReadAsStringAsync()).Should().Contain(code);
    }

    /// <summary>
    /// The capability is the money tier, and it is a list of its own: a role that joins the staff
    /// read surface does not inherit every customer's settlement code.
    /// </summary>
    [Fact]
    public async Task The_Capability_Admits_The_Money_Tier_And_Nobody_Else()
    {
        using var scope = _fixture.Services.CreateScope();
        var authorization = scope.ServiceProvider
            .GetRequiredService<Microsoft.AspNetCore.Authorization.IAuthorizationService>();

        var admitted = new List<string>();
        foreach (var role in new[] { SeedRoles.ProductOwnerName, SeedRoles.AdminName, SeedRoles.ManagerName })
        {
            var principal = new System.Security.Claims.ClaimsPrincipal(
                new System.Security.Claims.ClaimsIdentity(
                    [new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Role, role)],
                    "Test"));

            (await authorization.AuthorizeAsync(principal, null, AuthPolicies.SettlementCode))
                .Succeeded.Should().BeTrue($"{role} places a customer's payment, and the code is what it is placed by");
            admitted.Add(role);
        }

        foreach (var role in new[] { SeedRoles.UserName, SeedRoles.QaName })
        {
            var principal = new System.Security.Claims.ClaimsPrincipal(
                new System.Security.Claims.ClaimsIdentity(
                    [new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Role, role)],
                    "Test"));

            (await authorization.AuthorizeAsync(principal, null, AuthPolicies.SettlementCode))
                .Succeeded.Should().BeFalse($"{role} is not the money tier, and the code is money");
        }

        admitted.Should().BeEquivalentTo(SeedRoles.SettlementCodeRoleNames,
            "the policy and the list it is built from cannot disagree");
    }

    /// <summary>
    /// A customer with a settlement code of their own, so a test can assert against a known value
    /// rather than reading back whatever the database issued.
    /// </summary>
    private async Task<string> SeedCustomerAsync()
    {
        using var scope = _fixture.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var roleId = await context.Roles
            .Where(r => r.Name == SeedRoles.UserName)
            .Select(r => r.Id)
            .FirstAsync();
        var now = TestClock.UtcNow;
        var code = NewCode();

        context.Users.Add(new User
        {
            Id = Guid.NewGuid(),
            PhoneNumber = "+380" + Random.Shared.Next(100000000, 999999999),
            RoleId = roleId,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            IsActive = true,
            SettlementCode = code,
        });
        await context.SaveChangesAsync();

        return code;
    }

    /// <summary>
    /// A distinct code per call. The column is uniquely indexed across every row, and every test in
    /// this class shares one database - so a fixed code would make the second test fail on the
    /// first one's row rather than on anything it is actually asserting.
    /// </summary>
    private static string NewCode()
    {
        const string alphabet = "23456789ABCDEFGHJKMNPQRSTUVWXYZ";
        return string.Create(8, alphabet, static (span, chars) =>
        {
            for (var i = 0; i < span.Length; i++)
            {
                span[i] = chars[Random.Shared.Next(chars.Length)];
            }
        });
    }

    /// <summary>
    /// A user with the given role, created with it - so the token the login mints carries the role
    /// claim the capability policies check.
    /// </summary>
    private async Task SeedUserAsync(string phone, string roleName)
    {
        using var scope = _fixture.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var role = await context.Roles.FirstAsync(r => r.Name == roleName);
        var now = TestClock.UtcNow;

        context.Users.Add(new User
        {
            Id = Guid.NewGuid(),
            PhoneNumber = phone,
            RoleId = role.Id,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            IsActive = true,
            TokenVersion = 1,
        });
        await context.SaveChangesAsync();
    }

    private async Task<HttpClient> ClientWithRoleAsync(string phone, string roleName)
    {
        await SeedUserAsync(phone, roleName);

        var client = _fixture.CreateClient();

        var sendCode = await client.PostAsJsonAsync("/api/auth/send-code", new { phoneNumber = phone });
        sendCode.EnsureSuccessStatusCode();

        var verify = await client.PostAsJsonAsync("/api/auth/verify", new { phoneNumber = phone, code = "000000" });
        verify.EnsureSuccessStatusCode();
        var token = (await verify.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("accessToken").GetString();

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }
}
