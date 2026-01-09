using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Xunit;

namespace FuelFlow.Tests.Integration;

public class StationsEndpointTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly HttpClient _client;
    private readonly HttpClient _nodeClient;

    public StationsEndpointTests(TestWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
        _nodeClient = new HttpClient { BaseAddress = new Uri("http://localhost:4000") };
    }

    [Fact]
    public async Task GetStations_ShouldMatchNodeJsResponse()
    {
        var dotnetResponse = await _client.GetFromJsonAsync<List<object>>("/api/stations");
        var nodeResponse = await _nodeClient.GetFromJsonAsync<List<object>>("/api/stations");

        dotnetResponse.Should().BeEquivalentTo(nodeResponse);
    }

    [Fact]
    public async Task CreateStation_ShouldMatchNodeJsResponse()
    {
        var request = new
        {
            id = "test-station",
            name = "Test Station",
            color = "#00ff80",
            logoText = "TEST"
        };

        var dotnetResponse = await _client.PostAsJsonAsync("/api/admin/stations", request);
        var nodeResponse = await _nodeClient.PostAsJsonAsync("/api/admin/stations", request);

        dotnetResponse.StatusCode.Should().Be(nodeResponse.StatusCode);
        
        var dotnetBody = await dotnetResponse.Content.ReadAsStringAsync();
        var nodeBody = await nodeResponse.Content.ReadAsStringAsync();
        
        dotnetBody.Should().BeEquivalentTo(nodeBody);
    }

    [Fact]
    public async Task GetNonExistentStation_ShouldReturn404()
    {
        var dotnetResponse = await _client.GetAsync("/api/admin/stations/nonexistent");
        var nodeResponse = await _nodeClient.GetAsync("/api/admin/stations/nonexistent");

        dotnetResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
        nodeResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
