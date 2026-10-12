using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FuelFlow.IntegrationTests;

/// <summary>
/// Asserts that no two GET endpoints resolve to the same route pattern.
/// </summary>
/// <remarks>
/// <para>
/// Found in production on 2026-10-12: every call to
/// <c>/api/admin/reconciliation/statement</c> answered <c>500</c> with
/// <c>AmbiguousMatchException</c>. The endpoints had been disabled by commenting out their
/// <c>[HttpGet]</c> attributes - but the controller-level <c>[Route]</c> stayed, and under
/// <c>[ApiController]</c> an action with no HTTP attribute binds to the controller prefix. Three
/// disabled actions therefore all claimed the same pattern and the router threw, instead of
/// answering <c>404</c>.
/// </para>
/// <para>
/// That is the defect worth guarding: <em>disabling</em> an endpoint by commenting out its route
/// does not remove it. It turns a clean <c>404</c> into a <c>500</c> that takes every sibling
/// action in the same controller down with it. No static check can see a commented-out attribute,
/// so the guard has to ask the live endpoint graph.
/// </para>
/// </remarks>
[Collection("Integration Tests")]
public sealed class DuplicateGetRouteTests : IClassFixture<TestDatabaseFixture>
{
    private readonly TestDatabaseFixture _fixture;

    public DuplicateGetRouteTests(TestDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    private static string Template(RouteEndpoint endpoint) =>
        Regex.Replace(endpoint.RoutePattern.RawText ?? string.Empty, "[*]", "{**slug}");

    [Fact]
    public void No_two_GET_endpoints_share_a_route_pattern()
    {
        // CreateClient forces the host - and therefore Program.cs - to build.
        _ = _fixture.CreateClient();

        var dataSource = _fixture.Services.GetRequiredService<EndpointDataSource>();

        var duplicates = dataSource.Endpoints
            .OfType<RouteEndpoint>()
            // An endpoint with NO HttpMethodMetadata answers every method - which is exactly what an
            // action stripped of its [HttpGet] becomes under [ApiController]. Excluding those would
            // have hidden the very defect this file exists for.
            .Where(e =>
            {
                var methods = e.Metadata.GetMetadata<Microsoft.AspNetCore.Routing.HttpMethodMetadata>();
                return methods is null || methods.HttpMethods.Contains("GET");
            })
            .GroupBy(Template)
            .Where(g => g.Count() > 1)
            .Select(g => $"{g.Key} -> {string.Join(" | ", g.Select(e => e.DisplayName ?? "?"))}")
            .ToList();

        duplicates.Should().BeEmpty(
            "an action with no HTTP attribute binds to its controller prefix, so disabling one by " +
            "commenting out [HttpGet] makes several actions claim the same pattern; the router then " +
            "throws AmbiguousMatchException (500) instead of answering 404, taking live siblings down too");
    }
}
