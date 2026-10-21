using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FuelFlow.IntegrationTests;

/// <summary>
/// Every action declares the HTTP method it answers — and every endpoint that lost that
/// declaration is caught here.
/// </summary>
/// <remarks>
/// <para>
/// The failure this guards against is not hypothetical. On 2026-10-12 seventeen endpoints were
/// "disabled" by commenting out their <c>[HttpGet]</c> / <c>[HttpPost]</c> attribute, and
/// commenting an attribute out does not remove an endpoint: the controller-level <c>[Route]</c>
/// stays, and under <c>[ApiController]</c> an action with no HTTP attribute binds to that prefix
/// and answers <em>every</em> method at it. The result was not a 404 — it was a live endpoint at
/// a path nobody had chosen, three of them claiming the same pattern so the router threw
/// <c>AmbiguousMatchException</c> and took live siblings down with it
/// (<see cref="DuplicateGetRouteTests"/>).
/// </para>
/// <para>
/// That test catches the collision. This one catches the collision's cause: an action that answers
/// every method is an endpoint that was never meant to be there, whether or not a sibling claims
/// the same pattern. Deleting the endpoint is the only way to take one away — commenting its
/// attribute out is not deletion, and it is not even a way to switch one off.
/// </para>
/// </remarks>
[Collection("Integration Tests")]
public sealed class EndpointMethodContractTests : IClassFixture<TestDatabaseFixture>
{
    private readonly TestDatabaseFixture _fixture;

    public EndpointMethodContractTests(TestDatabaseFixture fixture) => _fixture = fixture;

    [Fact]
    public void Every_Action_Declares_The_Http_Method_It_Answers()
    {
        // CreateClient forces the host - and therefore Program.cs - to build.
        _ = _fixture.CreateClient();

        var actions = _fixture.Services
            .GetServices<EndpointDataSource>()
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            // Only MVC actions. The graph also holds the health check, the Hangfire dashboard and
            // the framework's own endpoints, and they are not what this is about.
            .Where(e => e.Metadata.GetMetadata<ControllerActionDescriptor>() is not null)
            .ToList();

        actions.Should().NotBeEmpty(
            "the endpoint graph should be visible to this test; an empty scan would pass forever");

        var methodless = actions
            .Where(e =>
            {
                var methods = e.Metadata.GetMetadata<HttpMethodMetadata>();
                return methods is null || methods.HttpMethods.Count == 0;
            })
            .Select(e => $"{e.RoutePattern.RawText}  ({e.DisplayName})")
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToArray();

        methodless.Should().BeEmpty(
            "an action with no HTTP attribute binds to its controller's prefix and answers EVERY " +
            "method at that path - commenting out an attribute is not a way to disable an endpoint, " +
            "it is a way to publish one (planning #289: three endpoints were disabled that way on " +
            "2026-10-12, and one of them answered every method at /api/admin)");
    }
}
