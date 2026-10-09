using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using FuelFlow.Features.Pricing.ImportOkkoPumpPrices;
using FuelFlow.Features.Pricing.ScrapeOkkoPrices;
using FuelFlow.SharedKernel.Options;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace FuelFlow.UnitTests.Pricing;

/// <summary>
/// A trimmed capture of the real <c>okko.ua/api/uk/fuels</c> response: the same nested component
/// tree, with the same field names and the same string-typed prices. Path deliberately shifted from
/// the live <c>data.layout[0].data.bullets.items</c> to prove the walk is shape-based, not path-based.
/// </summary>
internal static class OkkoApiSample
{
    public const string Json = """
    {
      "data": {
        "layout": [
          {
            "componentName": "FuelBaner",
            "data": {
              "bullets": {
                "items": [
                  { "title": "100",  "price": "102.90", "fuel_code": "Pulls 100",    "type": "pulls" },
                  { "title": "ДП",   "price": "102.90", "fuel_code": "Pulls Diesel", "type": "pulls" },
                  { "title": "ДП",   "price": "99.90",  "fuel_code": "DP",           "type": "euro"  },
                  { "title": "95",   "price": "95.90",  "fuel_code": "Pulls 95",     "type": "pulls" },
                  { "title": "95",   "price": "92.90",  "fuel_code": "A-95",         "type": "euro"  },
                  { "title": "ГАЗ",  "price": "47.90",  "fuel_code": "SPBT",         "type": ""      },
                  { "title": "AdBlue","price": "59.90", "fuel_code": "AdBlue",       "type": ""      }
                ]
              }
            }
          }
        ]
      }
    }
    """;
}

public sealed class OkkoPriceClientTests
{
    private static OkkoPriceClient Client(HttpMessageHandler handler, OkkoPriceOptions? options = null)
        => new(
            new HttpClient(handler),
            Options.Create(options ?? new OkkoPriceOptions()),
            NullLogger<OkkoPriceClient>.Instance);

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;
        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) => _respond = respond;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            => Task.FromResult(_respond(request));
    }

    private static HttpResponseMessage Json(string body, HttpStatusCode status = HttpStatusCode.OK)
        => new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    [Fact]
    public async Task FetchAsync_ShouldReadEveryPublishedPrice()
    {
        var prices = await Client(new StubHandler(_ => Json(OkkoApiSample.Json))).FetchAsync();

        prices.Should().HaveCount(7);
        prices.Single(p => p.SiteFuelCode == "A-95").PricePerLiter.Should().Be(92.90m);
        prices.Single(p => p.SiteFuelCode == "DP").PricePerLiter.Should().Be(99.90m);
        prices.Single(p => p.SiteFuelCode == "Pulls Diesel").PricePerLiter.Should().Be(102.90m);
        prices.Single(p => p.SiteFuelCode == "SPBT").PricePerLiter.Should().Be(47.90m);
    }

    [Fact]
    public async Task FetchAsync_ShouldKeepTheSiteFuelCode_NotTheLocalizedTitle()
    {
        // The whole mapping downstream keys off fuel_code; the title is translated per request and
        // must never be mistaken for the identity.
        var prices = await Client(new StubHandler(_ => Json(OkkoApiSample.Json))).FetchAsync();

        prices.Should().OnlyContain(p => !p.SiteFuelCode.Contains("ДП"));
        prices.Single(p => p.SiteFuelCode == "DP").PricePerLiter.Should().BeGreaterThan(0m);
    }

    [Fact]
    public async Task FetchAsync_ShouldFindPrices_WhereverTheySitInTheTree()
    {
        // Same rows, buried under unrelated nesting and repeated components: the walk keys off the
        // fuel_code/price pair, so moving them must not change the result.
        var shuffled = """
        { "data": { "layout": [ { "data": { "banner": { "items": [
            { "title": "no code", "price": "1.00" }
        ] } } },
        { "data": { "widget": { "deep": [ [ { "title": "95", "price": "92.90", "fuel_code": "A-95" } ] ] } } } ] } }
        """;

        var prices = await Client(new StubHandler(_ => Json(shuffled))).FetchAsync();

        prices.Should().ContainSingle();
        prices[0].SiteFuelCode.Should().Be("A-95");
        prices[0].PricePerLiter.Should().Be(92.90m);
    }

    [Fact]
    public async Task FetchAsync_ShouldTakeTheFirstOfADuplicateCode()
    {
        // OKKO repeats page components per section; the result must not depend on tree order.
        var duplicated = """
        { "a": { "fuel_code": "A-95", "price": "92.90" },
          "b": { "fuel_code": "A-95", "price": "88.00" } }
        """;

        var prices = await Client(new StubHandler(_ => Json(duplicated))).FetchAsync();

        prices.Should().ContainSingle();
        prices[0].PricePerLiter.Should().Be(92.90m);
    }

    [Fact]
    public async Task FetchAsync_ShouldIgnoreRowsThatAreNotAPrice()
    {
        // A node with a price but no fuel_code is not a fuel; a zero or unparseable price cannot be
        // written as a ceiling without silently zeroing the customer price.
        var noisy = """
        { "items": [
            { "title": "some banner", "price": "999.00" },
            { "fuel_code": "A-95", "price": "0" },
            { "fuel_code": "DP",  "price": "abc" },
            { "fuel_code": "SPBT", "price": "" },
            { "fuel_code": "Pulls 95", "price": "95.90" }
        ] }
        """;

        var prices = await Client(new StubHandler(_ => Json(noisy))).FetchAsync();

        prices.Should().ContainSingle();
        prices[0].SiteFuelCode.Should().Be("Pulls 95");
    }

    [Fact]
    public async Task FetchAsync_ShouldThrow_OnANonSuccessStatus()
    {
        var client = Client(new StubHandler(_ => Json("nope", HttpStatusCode.ServiceUnavailable)));

        await client.Invoking(c => c.FetchAsync())
            .Should().ThrowAsync<OkkoPriceScrapeException>()
            .WithMessage("*503*");
    }

    [Fact]
    public async Task FetchAsync_ShouldThrow_WhenThePayloadCarriesNoPrices()
    {
        // The loud failure is the point: an empty list here would silently skip every price write and
        // leave the catalogue stale with no indication anything went wrong.
        var client = Client(new StubHandler(_ => Json("""{ "data": { "layout": [] } }""")));

        await client.Invoking(c => c.FetchAsync())
            .Should().ThrowAsync<OkkoPriceScrapeException>()
            .WithMessage("*no rows with both fuel_code and price*");
    }

    [Fact]
    public async Task FetchAsync_ShouldSendAnIdentifiableUserAgent()
    {
        // We are polling a third party on a schedule; they should be able to see who and reach us.
        string? userAgent = null;
        var client = Client(new StubHandler(request =>
        {
            userAgent = request.Headers.UserAgent.ToString();
            return Json(OkkoApiSample.Json);
        }), new OkkoPriceOptions { UserAgent = "FuelFlow/1.0 (ops@example.test)" });

        await client.FetchAsync();

        userAgent.Should().Be("FuelFlow/1.0 (ops@example.test)");
    }

    [Fact]
    public async Task FetchAsync_ShouldHitTheConfiguredEndpoint()
    {
        Uri? requested = null;
        var client = Client(new StubHandler(request =>
        {
            requested = request.RequestUri;
            return Json(OkkoApiSample.Json);
        }), new OkkoPriceOptions { EndpointUrl = "https://example.test/api/uk/fuels" });

        await client.FetchAsync();

        requested.Should().Be(new Uri("https://example.test/api/uk/fuels"));
    }

    [Fact]
    public async Task FetchAsync_ShouldParsePricesAsInvariantDecimals()
    {
        // The API always writes a dot; a machine whose culture expects a comma must not read "92.90"
        // as ninety-two thousandths or fail outright.
        var previous = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("uk-UA");
            var prices = await Client(new StubHandler(_ => Json(OkkoApiSample.Json))).FetchAsync();
            prices.Single(p => p.SiteFuelCode == "A-95").PricePerLiter.Should().Be(92.90m);
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public async Task FetchAsync_ShouldAcceptNumericPrices_AsWellAsStrings()
    {
        // Defensive: today every price is a JSON string, but a future revision that emits a bare
        // number must not read as "no prices" and stall the sync.
        var numeric = """{ "items": [ { "fuel_code": "A-95", "price": 92.90 } ] }""";

        var prices = await Client(new StubHandler(_ => Json(numeric))).FetchAsync();

        prices.Should().ContainSingle();
        prices[0].PricePerLiter.Should().Be(92.90m);
    }

    [Fact]
    public async Task FetchAsync_ShouldThrow_OnMalformedJson()
    {
        var client = Client(new StubHandler(_ => Json("{ not json")));

        await client.Invoking(c => c.FetchAsync()).Should().ThrowAsync<JsonException>();
    }
}