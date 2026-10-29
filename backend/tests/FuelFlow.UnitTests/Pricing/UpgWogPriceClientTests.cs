using System.Net;
using FluentAssertions;
using FuelFlow.Features.Pricing.ScrapeUpgWogPrices;
using FuelFlow.SharedKernel.Options;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace FuelFlow.UnitTests.Pricing;

/// <summary>
/// What the fetch says when the vendor answers something other than a page.
/// </summary>
/// <remarks>
/// On 2026-10-29 the WOG host answered <c>301</c> and then <c>204 No Content</c> with an empty body
/// to the production host for nineteen hours. The sync logged "the page carried neither the
/// averagePrices blob nor a __NEXT_DATA__ state", which reads like a markup change - the actual
/// answer, a status code and an empty body, was the thing worth saying out loud.
/// </remarks>
public sealed class UpgWogPriceClientTests
{
    private static UpgWogPriceClient Client(Func<HttpRequestMessage, HttpResponseMessage> respond)
        => new(
            new HttpClient(new StubHandler(respond)),
            Options.Create(new UpgWogPriceOptions()),
            NullLogger<UpgWogPriceClient>.Instance);

    [Fact]
    public async Task An_empty_two_hundred_is_refused_with_the_status_that_arrived()
    {
        var client = Client(_ => new HttpResponseMessage(HttpStatusCode.NoContent));

        var act = async () => await client.FetchAsync("wog");

        (await act.Should().ThrowAsync<UpgWogPriceScrapeException>())
            .Which.Message.Should().Contain("HTTP 204").And.Contain("empty body",
                "an empty body is the answer the vendor gave, not a page with a new shape");
    }

    [Fact]
    public async Task A_page_is_read_from_the_configured_url()
    {
        HttpRequestMessage? seen = null;
        var client = Client(request =>
        {
            seen = request;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""
                    <html><body><script>
                    let averagePrices = {"data":[{"Title":"upg95","Price":91.9}]};
                    </script></body></html>
                    """)
            };
        });

        var prices = await client.FetchAsync("upg");

        prices.Should().ContainSingle().Which.PricePerLiter.Should().Be(91.90m);
        seen!.RequestUri!.ToString().Should().Be(new UpgWogPriceOptions().UpgEndpointUrl);
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) => _respond = respond;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            => Task.FromResult(_respond(request));
    }
}
