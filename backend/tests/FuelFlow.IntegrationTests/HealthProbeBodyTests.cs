using FluentAssertions;
using System.Net;
using Xunit;

namespace FuelFlow.IntegrationTests;

/// <summary>
/// What the anonymous probe is allowed to say.
/// </summary>
/// <remarks>
/// <para>
/// <c>/health</c> is anonymous because an uptime monitor has to reach it without a credential, and
/// that reachability is a reviewed decision - it is in the pinned anonymous surface
/// (<see cref="AnonymousEndpointSurfaceTests"/>). What was never reviewed is the body naming the
/// state of the database and of Redis: an unauthenticated caller learning which dependency is down
/// is a free reconnaissance result, and the monitor only reads the status code.
/// </para>
/// <para>
/// So the answer stays <c>200</c> or <c>503</c> with one word about health, and the dependency that
/// failed goes to the log with the trace id the response still carries.
/// </para>
/// </remarks>
[Collection("Integration Tests")]
public sealed class HealthProbeBodyTests : IClassFixture<TestDatabaseFixture>
{
    private readonly TestDatabaseFixture _fixture;

    public HealthProbeBodyTests(TestDatabaseFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task The_Anonymous_Probe_Says_It_Is_Up_And_Names_Nothing_Else()
    {
        var client = _fixture.CreateClient();

        var response = await client.GetAsync("/health");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("healthy");
        body.Should().NotContain("database");
        body.Should().NotContain("redis");
    }

    [Fact]
    public async Task The_Head_Probe_Answers_The_Same_way()
    {
        var client = _fixture.CreateClient();

        using var request = new HttpRequestMessage(HttpMethod.Head, "/health");

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
