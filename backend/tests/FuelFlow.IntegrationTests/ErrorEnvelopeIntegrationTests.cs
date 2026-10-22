using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using FuelFlow.Testing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace FuelFlow.IntegrationTests;

/// <summary>
/// The runtime half of planning finding C-6: the API emits exactly one error envelope.
/// </summary>
/// <remarks>
/// <para>
/// The architecture rule
/// (<c>backend/tests/FuelFlow.UnitTests/Architecture/DependencyRules/OneErrorEnvelopeTests.cs</c>)
/// proves that no call site builds an ad-hoc body. This proves what actually leaves the API: an
/// RFC 9457 problem document, served as <c>application/problem+json</c>, with the same fields
/// whatever failed - and with none of the shapes the API used to send.
/// </para>
/// <para>
/// The endpoints here are chosen to cover the three ways a failure leaves the API: a controller's
/// own rejection (a 400 from the auth controller), a refusal by the middleware before the
/// endpoint runs (a 401 from the session middleware), and a failure the exception handler
/// answers.
/// </para>
/// </remarks>
[Collection("Integration Tests")]
public sealed class ErrorEnvelopeIntegrationTests : WebApplicationFactory<Program>, IClassFixture<TestDatabaseFixture>
{
    public ErrorEnvelopeIntegrationTests(TestDatabaseFixture fixture)
    {
        _ = fixture;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
    }

    [Fact]
    public async Task ARejectedRequest_LeavesAsOneProblemDocument()
    {
        var client = CreateClient();

        // A phone number that is not a phone number: the controller's own refusal.
        var response = await client.PostAsJsonAsync("/api/auth/send-code", new { phoneNumber = "" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");

        var document = await ReadDocument(response);

        document.Should().ContainKey("type");
        document.Should().ContainKey("title").WhoseValue.Should().Be("Bad Request");
        document.Should().ContainKey("status").WhoseValue.Should().Be(400);
        document.Should().ContainKey("detail").WhoseValue.Should().Be("Phone number is required");
        document.Should().ContainKey("instance").WhoseValue.Should().Be("/api/auth/send-code");
        document.Should().ContainKey("traceId");

        // The shapes the API used to send. A client that still sniffs for them is sniffing a
        // contract that no longer exists.
        document.Should().NotContainKey("error");
        document.Should().NotContainKey("message");
        document.Should().NotContainKey("success");
    }

    [Fact]
    public async Task ARefusedRequest_LeavesAsTheSameDocument()
    {
        var client = CreateClient();

        // No credentials at all: the session middleware refuses before the endpoint runs.
        var response = await client.GetAsync("/api/company/members");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");

        var document = await ReadDocument(response);

        document.Should().ContainKey("title").WhoseValue.Should().Be("Unauthorized");
        document.Should().ContainKey("status").WhoseValue.Should().Be(401);
        document.Should().ContainKey("detail");
        document.Should().ContainKey("instance").WhoseValue.Should().Be("/api/company/members");
        document.Should().ContainKey("traceId");
        document.Should().NotContainKey("error");
        document.Should().NotContainKey("message");
    }

    [Fact]
    public async Task AnUnhandledFault_LeavesAsTheSameDocument()
    {
        var client = CreateClient();

        // A body the model binder cannot bind: the exception handler answers this one.
        var response = await client.PostAsJsonAsync("/api/auth/send-code", new { phoneNumber = new { nested = true } });

        response.StatusCode.Should().BeOneOf(HttpStatusCode.BadRequest, HttpStatusCode.InternalServerError);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");

        var document = await ReadDocument(response);

        document.Should().ContainKey("title");
        document.Should().ContainKey("status");
        document.Should().ContainKey("traceId");
        document.Should().NotContainKey("error");
        document.Should().NotContainKey("message");
    }

    private static async Task<Dictionary<string, JsonElement>> ReadDocument(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        var parsed = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(body);
        parsed.Should().NotBeNull("a failure leaves as a JSON document: " + body);
        return parsed!;
    }
}
