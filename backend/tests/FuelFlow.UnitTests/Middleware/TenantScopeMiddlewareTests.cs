using System.Security.Claims;
using FluentAssertions;
using FuelFlow.Middleware;
using FuelFlow.SharedKernel.Database;
using FuelFlow.SharedKernel.Domain;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace FuelFlow.UnitTests.Middleware;

/// <summary>
/// The request-to-scope mapping, tested as the pure function it is.
/// </summary>
/// <remarks>
/// The scope is the input to the database gate, so the cases that matter are the ones where it
/// could be wider than the caller is entitled to: an unauthenticated request, a token without a
/// usable subject, and a staff role that legitimately reads across tenants.
/// </remarks>
public sealed class TenantScopeMiddlewareTests
{
    [Fact]
    public void An_anonymous_request_is_anonymous()
    {
        var context = new DefaultHttpContext();

        TenantScopeMiddleware.Resolve(context).Should().Be(
            TenantScope.Anonymous,
            "no identity is no rows, not every row");
    }

    [Fact]
    public void A_customer_is_scoped_to_their_own_user_id()
    {
        var userId = Guid.NewGuid();
        var context = ContextWith(userId, roles: []);

        TenantScopeMiddleware.Resolve(context).Should().Be(
            TenantScope.ForUser(userId),
            "an ordinary customer reads their own rows and their company's");
    }

    [Theory]
    [InlineData(SeedRoles.ProductOwnerName)]
    [InlineData(SeedRoles.AdminName)]
    [InlineData(SeedRoles.ManagerName)]
    public void A_staff_member_is_the_system(string role)
    {
        var context = ContextWith(Guid.NewGuid(), roles: [role]);

        TenantScopeMiddleware.Resolve(context).Should().Be(
            TenantScope.System,
            "the staff surfaces legitimately read across tenants");
    }

    [Fact]
    public void A_system_scope_endpoint_is_the_system_without_an_identity()
    {
        var context = new DefaultHttpContext();
        context.SetEndpoint(new Endpoint(
            requestDelegate: null,
            new EndpointMetadataCollection(new SystemScopeAttribute()),
            displayName: "test webhook"));

        TenantScopeMiddleware.Resolve(context).Should().Be(
            TenantScope.System,
            "the webhooks authenticate by signature and must act on every tenant's rows");
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-guid")]
    public void An_authenticated_request_without_a_usable_subject_is_anonymous(string rawSubject)
    {
        var identity = new ClaimsIdentity(authenticationType: "test");
        identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, rawSubject));
        var context = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(identity),
        };

        TenantScopeMiddleware.Resolve(context).Should().Be(
            TenantScope.Anonymous,
            "a token we cannot attribute must not widen the scope");
    }

    private static DefaultHttpContext ContextWith(Guid userId, string[] roles)
    {
        var identity = new ClaimsIdentity(authenticationType: "test");
        identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, userId.ToString("D")));
        identity.AddClaims(roles.Select(role => new Claim(ClaimTypes.Role, role)));

        return new DefaultHttpContext
        {
            User = new ClaimsPrincipal(identity),
        };
    }
}
