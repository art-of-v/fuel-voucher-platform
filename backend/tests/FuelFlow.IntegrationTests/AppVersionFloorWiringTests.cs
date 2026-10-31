using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace FuelFlow.IntegrationTests;

/// <summary>
/// Proves the update-floor middleware runs in the real pipeline, not just in a unit test that
/// constructs it by hand. A guard that exists is not a guard that runs: the wiring lives in
/// <c>PipelineSetup</c>, and this class is what fails if a later reordering drops it.
/// </summary>
[Collection("Integration Tests")]
public class AppVersionFloorWiringTests : WebApplicationFactory<Program>, IClassFixture<TestDatabaseFixture>
{
    // The fixture is constructed only to start the containers this host needs before it boots.
    public AppVersionFloorWiringTests(TestDatabaseFixture fixture)
    {
        _ = fixture;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
    }

    [Fact]
    public async Task A_Native_Client_Below_The_Floor_Is_Refused_With_426()
    {
        var response = await Send("/api/brands", "mobile", "0.0.1");

        response.StatusCode.Should().Be(HttpStatusCode.UpgradeRequired);

        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("code").GetString().Should().Be("APP_UPDATE_REQUIRED");
        problem.GetProperty("data").GetProperty("minimumVersion").GetString()
            .Should().Be(await FloorAsync());
    }

    /// <summary>
    /// <c>/api/app-version</c> is how the client learns the floor and draws the update screen -
    /// the refusal would be a dead end if the same client could not read it.
    /// </summary>
    [Fact]
    public async Task The_Client_Can_Still_Read_The_Floor_It_Is_Below()
    {
        var response = await Send("/api/app-version", "mobile", "0.0.1");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_Current_Client_Reaches_The_Endpoint()
    {
        var response = await Send("/api/brands", "mobile", "99.0.0");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    /// <summary>
    /// The public reads are cached for 300s, and the cache key does not know which build asked.
    /// The refusal must run before it - a cached 200 replayed to a stale client is the refusal
    /// never being sent at all.
    /// </summary>
    [Fact]
    public async Task A_Cached_Reading_Does_Not_Replace_The_Refusal()
    {
        (await Send("/api/brands", "mobile", "99.0.0")).StatusCode.Should().Be(HttpStatusCode.OK);

        var response = await Send("/api/brands", "mobile", "0.0.1");

        response.StatusCode.Should().Be(HttpStatusCode.UpgradeRequired);
    }

    private async Task<HttpResponseMessage> Send(string path, string platform, string version)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Add("X-Client-Platform", platform);
        request.Headers.Add("X-App-Version", version);

        return await CreateClient().SendAsync(request);
    }

    private async Task<string> FloorAsync()
    {
        var response = await CreateClient().GetAsync("/api/app-version");
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        return body.GetProperty("minimumVersion").GetString()!;
    }
}
