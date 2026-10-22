using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using FluentAssertions;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using FuelFlow.Testing;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FuelFlow.IntegrationTests;

/// <summary>
/// Pins the capability model of the staff surface.
/// </summary>
/// <remarks>
/// <para>
/// Until 2026-10-21 every staff endpoint carried <c>[Authorize(Policy = "Staff")]</c>. One policy
/// for a refund and for a report means the attribute said only "this is for staff" and never what
/// it allowed, so the finding behind the change (planning #521, S-1) had to be read out of the
/// handlers instead: which of these endpoints move money, and which of them a Manager may touch.
/// </para>
/// <para>
/// The surface is now split into capabilities - read, catalogue, voucher stock, money, settings,
/// staff operations, account administration - and this test is what keeps the split honest in
/// both directions:
/// </para>
/// <list type="number">
/// <item><b>Every endpoint declares one capability, and the map is a reviewed one.</b> A new
/// endpoint cannot join a policy by accident: it fails the test until somebody says which
/// capability it belongs to, and the table below is that decision in one place.</item>
/// <item><b>Every capability admits exactly the roles the owner decided.</b> A Manager moves money
/// and may not ban anybody; the retired QA staff role is admitted by nothing.</item>
/// <item><b>And the same thing over HTTP, with a real token.</b> The policy is the gate a request
/// actually meets, so the last test asks the endpoint itself.</item>
/// </list>
/// </remarks>
[Collection("Integration Tests")]
public sealed class AuthorizePolicySurfaceTests : IClassFixture<TestDatabaseFixture>
{
    private readonly TestDatabaseFixture _fixture;

    public AuthorizePolicySurfaceTests(TestDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    /// <summary>
    /// The reviewed map: every endpoint of the staff surface, and the capability it declares.
    /// "METHOD path" - the same shape <c>AnonymousEndpointSurfaceTests</c> uses, so the two tables
    /// can be read side by side. Route templates are compared lowercased, because routing itself
    /// is case-insensitive and a rename that changes nothing about reachability should not fail a
    /// test about authorization.
    /// </summary>
    private static readonly Dictionary<string, string> ExpectedCapabilities = new()
    {
        // ---- the read surface -------------------------------------------------------
        ["GET /api/admin/audit"] = AuthPolicies.Staff,
        ["GET /api/admin/audit/facets"] = AuthPolicies.Staff,
        ["GET /api/admin/brand-fuels/{fuelid}/price-at"] = AuthPolicies.Staff,
        ["GET /api/admin/brand-fuels/{fuelid}/price-history"] = AuthPolicies.Staff,
        ["GET /api/admin/brand-stations"] = AuthPolicies.Staff,
        ["GET /api/admin/brand-stations/{id}"] = AuthPolicies.Staff,
        ["GET /api/admin/brands"] = AuthPolicies.Staff,
        ["GET /api/admin/brands/{id}"] = AuthPolicies.Staff,
        ["GET /api/admin/brands/{id}/history"] = AuthPolicies.Staff,
        ["GET /api/admin/contract-versions"] = AuthPolicies.Staff,
        ["GET /api/admin/dashboard"] = AuthPolicies.Staff,
        ["GET /api/admin/errors"] = AuthPolicies.Staff,
        ["GET /api/admin/errors/facets"] = AuthPolicies.Staff,
        ["GET /api/admin/feedback"] = AuthPolicies.Staff,
        ["GET /api/admin/feedback/facets"] = AuthPolicies.Staff,
        ["GET /api/admin/feedback/{id:guid}"] = AuthPolicies.Staff,
        ["GET /api/admin/fuel-types"] = AuthPolicies.Staff,
        ["GET /api/admin/fuel-types/{id}"] = AuthPolicies.Staff,
        ["GET /api/admin/legal-entity/contracts"] = AuthPolicies.Staff,
        ["GET /api/admin/legal-entity/contracts/signed-contracts"] = AuthPolicies.Staff,
        ["GET /api/admin/orders"] = AuthPolicies.Staff,
        ["GET /api/admin/orders/{id}"] = AuthPolicies.Staff,
        ["GET /api/admin/orders/{id:guid}/receipt"] = AuthPolicies.Staff,
        ["GET /api/admin/payments/customers"] = AuthPolicies.Staff,
        ["GET /api/admin/payments/customers/by-code"] = AuthPolicies.Staff,
        ["GET /api/admin/payments/unallocated"] = AuthPolicies.Staff,
        ["GET /api/admin/purchases"] = AuthPolicies.Staff,
        ["GET /api/admin/qa-test-access"] = AuthPolicies.Staff,
        ["GET /api/admin/reconciliation"] = AuthPolicies.Staff,
        ["GET /api/admin/reconciliation/statement"] = AuthPolicies.Staff,
        ["GET /api/admin/reconciliation/statement/csv"] = AuthPolicies.Staff,
        ["GET /api/admin/reconciliation/statement/pdf"] = AuthPolicies.Staff,
        ["GET /api/admin/report"] = AuthPolicies.Staff,
        ["GET /api/admin/settings"] = AuthPolicies.Staff,
        ["GET /api/admin/suppliers"] = AuthPolicies.Staff,
        ["GET /api/admin/suppliers/acquisition-sources"] = AuthPolicies.Staff,
        ["GET /api/admin/suppliers/legal-forms"] = AuthPolicies.Staff,
        ["GET /api/admin/users"] = AuthPolicies.Staff,
        ["GET /api/admin/users/options"] = AuthPolicies.Staff,
        ["GET /api/admin/voucher-exchange/attention"] = AuthPolicies.Staff,
        ["GET /api/admin/voucher-exchange/attention/count"] = AuthPolicies.Staff,
        ["GET /api/admin/voucher-exchange/batches"] = AuthPolicies.Staff,
        ["GET /api/admin/voucher-exchange/batches/{exchangebatchid:guid}"] = AuthPolicies.Staff,
        ["GET /api/admin/voucher-exchange/cost-context"] = AuthPolicies.Staff,
        ["GET /api/admin/voucher-imports"] = AuthPolicies.Staff,
        ["GET /api/admin/voucher-imports/{id:guid}"] = AuthPolicies.Staff,
        ["GET /api/admin/voucher-imports/{id:guid}/batch-costs"] = AuthPolicies.Staff,
        ["GET /api/admin/voucher-imports/{id:guid}/batch-pnl"] = AuthPolicies.Staff,
        ["GET /api/admin/voucher-imports/{id:guid}/cost-timeline"] = AuthPolicies.Staff,
        ["GET /api/admin/voucher-imports/{id:guid}/vouchers"] = AuthPolicies.Staff,
        ["GET /api/admin/voucher-renewal/terms"] = AuthPolicies.Staff,
        ["GET /api/admin/voucher-renewal/vouchers"] = AuthPolicies.Staff,
        ["GET /api/admin/vouchers"] = AuthPolicies.Staff,
        ["GET /api/admin/vouchers/{id}"] = AuthPolicies.Staff,
        ["GET /api/admin/vouchers/{id:guid}/lifecycle"] = AuthPolicies.Staff,
        ["GET /api/admin/vouchers/{id:guid}/verification"] = AuthPolicies.Staff,
        ["GET /api/voucher-catalog"] = AuthPolicies.Staff,
        ["GET /api/vouchers/inventory"] = AuthPolicies.Staff,

        // ---- the catalogue ----------------------------------------------------------
        ["POST /api/admin/brand-fuels/brands/{id}/fuels"] = AuthPolicies.CatalogueWrite,
        ["POST /api/admin/brand-prices/{brand}/import"] = AuthPolicies.CatalogueWrite,
        ["POST /api/admin/brand-stations"] = AuthPolicies.CatalogueWrite,
        ["POST /api/admin/brand-stations/import"] = AuthPolicies.CatalogueWrite,
        ["POST /api/admin/brands"] = AuthPolicies.CatalogueWrite,
        ["POST /api/admin/catalogue/sync"] = AuthPolicies.CatalogueWrite,
        ["POST /api/admin/contract-versions"] = AuthPolicies.CatalogueWrite,
        ["POST /api/admin/contract-versions/upload"] = AuthPolicies.CatalogueWrite,
        ["POST /api/admin/suppliers"] = AuthPolicies.CatalogueWrite,
        ["POST /api/admin/suppliers/{id:guid}/reactivate"] = AuthPolicies.CatalogueWrite,
        ["DELETE /api/admin/brand-fuels/{fuelid}"] = AuthPolicies.CatalogueWrite,
        ["DELETE /api/admin/brand-stations/{id}"] = AuthPolicies.CatalogueWrite,
        ["DELETE /api/admin/brands/{id}"] = AuthPolicies.CatalogueWrite,
        ["DELETE /api/admin/suppliers/{id:guid}"] = AuthPolicies.CatalogueWrite,
        ["PUT /api/admin/brand-fuels/brands/{id}/nominals"] = AuthPolicies.CatalogueWrite,
        ["PUT /api/admin/brand-fuels/{fuelid}"] = AuthPolicies.CatalogueWrite,
        ["PUT /api/admin/brand-stations/{id}"] = AuthPolicies.CatalogueWrite,
        ["PUT /api/admin/brands/{id}"] = AuthPolicies.CatalogueWrite,
        ["PUT /api/admin/suppliers/{id:guid}"] = AuthPolicies.CatalogueWrite,

        // ---- the voucher stock -------------------------------------------------------
        ["POST /api/admin/voucher-exchange/confirm"] = AuthPolicies.VoucherWrite,
        ["POST /api/admin/voucher-imports/{id:guid}/bulk-action"] = AuthPolicies.VoucherWrite,
        ["POST /api/admin/voucher-imports/{id:guid}/parse-invoice"] = AuthPolicies.VoucherWrite,
        ["POST /api/admin/voucher-renewal/confirm"] = AuthPolicies.VoucherWrite,
        ["POST /api/admin/vouchers/bulk-action"] = AuthPolicies.VoucherWrite,
        ["POST /api/admin/vouchers/{id:guid}/unarchive"] = AuthPolicies.VoucherWrite,
        ["POST /api/admin/vouchers/{id:guid}/unblock"] = AuthPolicies.VoucherWrite,
        ["POST /api/voucher-catalog/import"] = AuthPolicies.VoucherWrite,
        ["DELETE /api/admin/vouchers/{id}"] = AuthPolicies.VoucherWrite,
        ["PUT /api/admin/voucher-imports/{id:guid}/batch-costs/{fueltypeid}"] = AuthPolicies.VoucherWrite,
        ["PUT /api/admin/vouchers/{id}"] = AuthPolicies.VoucherWrite,

        // ---- the money ---------------------------------------------------------------
        ["DELETE /api/admin/orders/{id}"] = AuthPolicies.MoneyMovement,
        ["POST /api/admin/orders/{id}/refund"] = AuthPolicies.MoneyMovement,
        ["POST /api/admin/payments/{id:guid}/attach"] = AuthPolicies.MoneyMovement,
        ["POST /api/purchases/simulate"] = AuthPolicies.MoneyMovement,
        ["PUT /api/admin/orders/{id}"] = AuthPolicies.MoneyMovement,

        // ---- the settings ------------------------------------------------------------
        ["POST /api/admin/settings/feedback/webhook"] = AuthPolicies.SettingsWrite,
        ["PUT /api/admin/settings"] = AuthPolicies.SettingsWrite,

        // ---- the staff operations ----------------------------------------------------
        ["DELETE /api/admin/errors"] = AuthPolicies.StaffOperations,
        ["DELETE /api/admin/feedback"] = AuthPolicies.StaffOperations,
        ["POST /api/admin/errors/resolve"] = AuthPolicies.StaffOperations,
        ["POST /api/admin/feedback/status"] = AuthPolicies.StaffOperations,
        ["POST /api/admin/users/{id}/activate"] = AuthPolicies.StaffOperations,
        ["POST /api/admin/users/{id}/deactivate"] = AuthPolicies.StaffOperations,
        ["POST /api/admin/users/{id}/email"] = AuthPolicies.StaffOperations,

        // ---- the account administration ----------------------------------------------
        ["DELETE /api/admin/users/{id}"] = AuthPolicies.UserAdministration,
        ["POST /api/admin/users/{id}/ban"] = AuthPolicies.UserAdministration,
        ["POST /api/admin/users/{id}/role"] = AuthPolicies.UserAdministration,
        ["POST /api/admin/users/{id}/unban"] = AuthPolicies.UserAdministration,

        // ---- the QA test access ------------------------------------------------------
        ["PUT /api/admin/qa-test-access"] = AuthPolicies.QaTestAccess,
    };

    /// <summary>
    /// The roles each capability admits, as the owner decided them on 2026-10-21 (planning #521,
    /// §7.1): money movement belongs to Admin, ProductOwner and Manager; ban, delete and the
    /// assignment of a role stay with Admin and ProductOwner; the QA test-access switch is an
    /// authentication mechanism and goes with them.
    /// </summary>
    private static readonly (string Capability, string[] Roles)[] DecidedRoles =
    [
        (AuthPolicies.Staff, [SeedRoles.ProductOwnerName, SeedRoles.AdminName, SeedRoles.ManagerName]),
        (AuthPolicies.CatalogueWrite, [SeedRoles.ProductOwnerName, SeedRoles.AdminName, SeedRoles.ManagerName]),
        (AuthPolicies.VoucherWrite, [SeedRoles.ProductOwnerName, SeedRoles.AdminName, SeedRoles.ManagerName]),
        (AuthPolicies.MoneyMovement, [SeedRoles.ProductOwnerName, SeedRoles.AdminName, SeedRoles.ManagerName]),
        (AuthPolicies.SettingsWrite, [SeedRoles.ProductOwnerName, SeedRoles.AdminName, SeedRoles.ManagerName]),
        (AuthPolicies.StaffOperations, [SeedRoles.ProductOwnerName, SeedRoles.AdminName, SeedRoles.ManagerName]),
        (AuthPolicies.UserAdministration, [SeedRoles.ProductOwnerName, SeedRoles.AdminName]),
        (AuthPolicies.QaTestAccess, [SeedRoles.ProductOwnerName, SeedRoles.AdminName]),
        // The redeemable payload of a voucher is a bearer instrument: whoever holds it can spend
        // the customer's fuel, and nothing records that they did. So it is narrower than the money
        // movement a Manager may do - a refund is an audited row, a redemption is neither.
        (AuthPolicies.VoucherPayload, [SeedRoles.ProductOwnerName, SeedRoles.AdminName]),
    ];

    /// <summary>
    /// Every endpoint of the staff surface declares exactly one capability, and the map of
    /// endpoint to capability is the reviewed one.
    /// </summary>
    [Fact]
    public void Every_Staff_Endpoint_Declares_One_Reviewed_Capability()
    {
        var actual = StaffEndpointCapabilities();

        // Guard against an empty or partial endpoint graph making the comparison vacuous: a
        // false "no staff endpoints" would otherwise look like a pass.
        actual.Should().HaveCountGreaterThan(50,
            "the whole staff surface should be visible to this test");

        actual.Should().BeEquivalentTo(ExpectedCapabilities,
            "an endpoint's capability is a reviewed decision: add it to "
            + nameof(ExpectedCapabilities) + " deliberately, with the capability it belongs to");
    }

    /// <summary>
    /// An endpoint of the staff surface cannot carry a capability nobody knows, and cannot carry
    /// two of them: the combination of two policies is an AND, and a reader would have to know
    /// that to know what the endpoint allows.
    /// </summary>
    [Fact]
    public void Every_Staff_Endpoint_Carries_Exactly_One_Known_Capability()
    {
        var known = DecidedRoles.Select(d => d.Capability).ToArray();

        var offenders = new List<string>();

        foreach (var endpoint in RoutedEndpoints())
        {
            var policies = endpoint.Metadata
                .GetOrderedMetadata<IAuthorizeData>()
                .Where(a => a.Policy is not null)
                .Select(a => a.Policy!)
                .ToArray();

            if (policies.Length == 0)
                continue;

            if (policies.Length > 1)
                offenders.Add($"{Describe(endpoint)} carries {policies.Length} policies: {string.Join(", ", policies)}");
            else if (!known.Contains(policies[0]))
                offenders.Add($"{Describe(endpoint)} carries the unknown policy {policies[0]}");
        }

        offenders.Should().BeEmpty(
            "every staff endpoint declares exactly one capability, and the capability is one the "
            + "owner decided");
    }

    /// <summary>
    /// Every capability admits exactly the roles the owner decided, and nobody else - including
    /// the retired QA staff role, which is not a staff role any more.
    /// </summary>
    [Fact]
    public async Task Every_Capability_Admins_Exactly_The_Decided_Roles()
    {
        using var scope = _fixture.Services.CreateScope();
        var authorization = scope.ServiceProvider.GetRequiredService<IAuthorizationService>();

        var failures = new List<string>();

        foreach (var (capability, roles) in DecidedRoles)
        {
            foreach (var role in AllRoles)
            {
                var admitted = await authorization.AuthorizeAsync(PrincipalFor(role), resource: null, capability);
                var expected = roles.Contains(role);

                if (admitted.Succeeded != expected)
                {
                    failures.Add($"{capability} {(admitted.Succeeded ? "admits" : "refuses")} {role}, "
                                 + $"and the decision is the other way round");
                }
            }
        }

        failures.Should().BeEmpty();
    }

    /// <summary>
    /// The same gate, over HTTP, with a real Manager token: the account administration is
    /// refused, and the money is not.
    /// </summary>
    /// <remarks>
    /// The acceptance criterion of the finding was written before the decision of 2026-10-21 and
    /// said that "a Manager token is refused by every money-moving endpoint". The owner decided
    /// the opposite for the money: a Manager moves money - a refund, a payment placed for a
    /// customer, an order's status - and what no role may do is hand out fuel that was not paid
    /// for, which is an invariant of the order rather than of the role (see
    /// <c>UpdateOrderStatusCommandHandler</c> and the fulfilment job).
    /// <para>
    /// So the refusal that is left to prove is the one the decision kept: ban, delete and the
    /// assignment of a role are Admin and ProductOwner only.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task A_Manager_May_Move_Money_And_May_Not_Administer_Accounts()
    {
        var targetId = await SeedUserAsync("+15550009001", SeedRoles.UserName);
        var manager = await StaffClientAsync("+15550009002", SeedRoles.ManagerName);

        // The account administration: refused by the policy, before the handler is reached.
        (await manager.PostAsync($"/api/admin/users/{targetId}/ban", null))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden,
                "a Manager may not ban: the ban is Admin and ProductOwner's");
        (await manager.PostAsync($"/api/admin/users/{targetId}/unban", null))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await manager.DeleteAsync($"/api/admin/users/{targetId}"))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden,
                "a Manager may not delete an account");
        (await manager.PostAsJsonAsync($"/api/admin/users/{targetId}/role", new { role = "User" }))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden,
                "a Manager may not mint a role");
        (await manager.PutAsJsonAsync("/api/admin/qa-test-access", new { enabled = true }))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden,
                "the QA test-access switch is an authentication mechanism, and it is not a Manager's");

        // The money: the policy lets a Manager through, and the request fails on the order that
        // is not there rather than on the permission. A 404 is the proof that authorization passed.
        var missingOrder = Guid.NewGuid();
        (await manager.PostAsync($"/api/admin/orders/{missingOrder}/refund", null))
            .StatusCode.Should().NotBe(HttpStatusCode.Forbidden,
                "money movement belongs to Admin, ProductOwner and Manager");
        (await manager.PutAsJsonAsync($"/api/admin/orders/{missingOrder}", new { status = "PendingFulfillment" }))
            .StatusCode.Should().NotBe(HttpStatusCode.Forbidden);
        (await manager.PostAsync($"/api/admin/payments/{Guid.NewGuid()}/attach", null))
            .StatusCode.Should().NotBe(HttpStatusCode.Forbidden);
    }

    private static readonly string[] AllRoles =
    [
        SeedRoles.ProductOwnerName,
        SeedRoles.AdminName,
        SeedRoles.ManagerName,
        SeedRoles.UserName,
        SeedRoles.QaName,
    ];

    /// <summary>
    /// The endpoint map of the staff surface: "METHOD path" to the capability it declares.
    /// </summary>
    private Dictionary<string, string> StaffEndpointCapabilities()
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var endpoint in RoutedEndpoints())
        {
            var policies = endpoint.Metadata
                .GetOrderedMetadata<IAuthorizeData>()
                .Where(a => a.Policy is not null)
                .Select(a => a.Policy!)
                .Distinct()
                .ToArray();

            if (policies.Length == 0)
                continue;

            foreach (var described in Describe(endpoint))
            {
                map[described] = policies.Length == 1
                    ? policies[0]
                    : string.Join("+", policies);
            }
        }

        return map;
    }

    private IReadOnlyList<RouteEndpoint> RoutedEndpoints()
    {
        // The composite EndpointDataSource is assembled by UseEndpoints, so ask the root brand
        // for every registered source and flatten. CreateClient() forces the host - and therefore
        // the endpoint graph - to be built before this runs.
        _ = _fixture.CreateClient();

        return _fixture.Services
            .GetServices<EndpointDataSource>()
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .ToList();
    }

    private static IEnumerable<string> Describe(RouteEndpoint endpoint)
    {
        var path = "/" + endpoint.RoutePattern.RawText?.TrimStart('/').ToLowerInvariant();

        var methods = endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods;
        if (methods is null || methods.Count == 0)
            return [$"* {path}"];

        return methods.Select(m => $"{m.ToUpperInvariant()} {path}");
    }

    private static ClaimsPrincipal PrincipalFor(string role) =>
        new(new ClaimsIdentity([new Claim(ClaimTypes.Role, role)], authenticationType: "Test"));

    /// <summary>
    /// A user with the given role, created with it - so the token minted by the login carries the
    /// role claim the capability policies check.
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
    /// A staff client: the user is seeded WITH its role, then signs in through the ordinary flow.
    /// </summary>
    private async Task<HttpClient> StaffClientAsync(string phone, string roleName)
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
