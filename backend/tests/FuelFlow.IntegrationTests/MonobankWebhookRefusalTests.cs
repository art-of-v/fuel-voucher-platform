using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace FuelFlow.IntegrationTests;

/// <summary>
/// Proves the payments-off refusal runs in the real pipeline, not just in the startup validator.
///
/// <para>
/// The webhook used to process an unsigned body whenever <c>Monobank:Enabled</c> was false, with a
/// warning: a host that lost the flag looked healthy while any caller could name an invoice and
/// declare it paid. The validator now refuses to boot Production in that state, and the endpoint
/// itself stays closed even when the state is acknowledged - this test is what fails if a later
/// change reopens the unsigned path.
/// </para>
/// </summary>
[Collection("Integration Tests")]
public class MonobankWebhookRefusalTests : WebApplicationFactory<Program>, IClassFixture<TestDatabaseFixture>
{
    // The fixture is constructed only to start the containers this host needs before it boots.
    public MonobankWebhookRefusalTests(TestDatabaseFixture fixture)
    {
        _ = fixture;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Development ships payments off (appsettings.Development.json), which is the state this
        // test needs; the environment is pinned so a stray process-level override cannot turn the
        // endpoint back on and make the assertion vacuous.
        builder.UseEnvironment("Development");
    }

    [Fact]
    public async Task A_Host_Without_Payments_Refuses_An_Unsigned_Delivery()
    {
        var response = await CreateClient().PostAsJsonAsync(
            "/api/monobank/webhook",
            new { invoiceId = "MOCK_anything", status = "success", amount = 100_00 });

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable,
            "a host with no acquirer has nothing that could have signed a delivery");
    }
}
