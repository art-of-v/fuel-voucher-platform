using System.Net;
using System.Text;
using FluentAssertions;
using FuelFlow.API.Features.Orders.SharedServices.Monobank;
using FuelFlow.API.Features.Orders.SharedServices.Monobank.Models;
using FuelFlow.SharedKernel.Options;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace FuelFlow.UnitTests.Monobank;

/// <summary>
/// The factory is what makes a second merchant possible: it binds one merchant's credentials to a
/// client at construction, so a live and a sandbox call can never share a token. These tests assert
/// the binding itself by inspecting the X-Token header of a real (recorded) HTTP call - asserting
/// only that no exception is thrown would pass even if both merchants were handed the live token.
/// </summary>
public class MonobankClientFactoryTests
{
    private const string LiveToken = "live-token";
    private const string SandboxToken = "sandbox-token";
    private const string BaseUrl = "https://api.monobank.ua";

    [Fact]
    public async Task ForMerchant_Live_SendsTheLiveToken()
    {
        var (factory, handler, httpClientFactory) = CreateFactory();

        var client = factory.ForMerchant(MonobankMerchant.Live);
        await client.CreateInvoiceAsync(new MonobankInvoiceRequest { Amount = 1000 });

        handler.Request!.Headers.GetValues("X-Token").Should().Contain(LiveToken);
        httpClientFactory.Verify(x => x.CreateClient("monobank-Live"), Times.Once);
    }

    [Fact]
    public async Task ForMerchant_Sandbox_SendsTheSandboxToken()
    {
        var (factory, handler, httpClientFactory) = CreateFactory();

        var client = factory.ForMerchant(MonobankMerchant.Sandbox);
        await client.CreateInvoiceAsync(new MonobankInvoiceRequest { Amount = 1000 });

        handler.Request!.Headers.GetValues("X-Token").Should().Contain(SandboxToken);
        httpClientFactory.Verify(x => x.CreateClient("monobank-Sandbox"), Times.Once);
    }

    [Fact]
    public async Task ForMerchant_Sandbox_UsesTheSameHostAsLive()
    {
        // The sandbox is a separate merchant profile, not a separate endpoint: both merchants live
        // on api.monobank.ua. A BaseUrl drift here would send sandbox invoices to the live host.
        var (factory, handler, _) = CreateFactory();

        await factory.ForMerchant(MonobankMerchant.Sandbox)
            .CreateInvoiceAsync(new MonobankInvoiceRequest { Amount = 1000 });

        handler.Request!.RequestUri!.Host.Should().Be("api.monobank.ua");
    }

    [Fact]
    public void ForMerchant_SandboxWithoutToken_RefusesInsteadOfUsingLive()
    {
        var (factory, _, _) = CreateFactory(sandboxToken: string.Empty);

        var act = () => factory.ForMerchant(MonobankMerchant.Sandbox);

        act.Should().Throw<MonobankMerchantUnavailableException>()
            .Which.Merchant.Should().Be(MonobankMerchant.Sandbox);
    }

    [Fact]
    public void ForMerchant_LiveWithoutToken_Refuses()
    {
        var (factory, _, _) = CreateFactory(liveToken: string.Empty);

        var act = () => factory.ForMerchant(MonobankMerchant.Live);

        act.Should().Throw<MonobankMerchantUnavailableException>()
            .Which.Merchant.Should().Be(MonobankMerchant.Live);
    }

    [Fact]
    public void ForMerchant_WhenMonobankDisabled_ReturnsTheMockClient()
    {
        // Enabled=false switches the integration off entirely (dev/demo). No real money can move, so
        // routing is moot and the in-process mock keeps checkout and refunds exercisable without a
        // token - which is how these environments worked before the second merchant existed.
        var (factory, _, _) = CreateFactory(enabled: false, liveToken: string.Empty, sandboxToken: string.Empty);

        factory.ForMerchant(MonobankMerchant.Live).Should().BeOfType<MockMonobankClient>();
        factory.ForMerchant(MonobankMerchant.Sandbox).Should().BeOfType<MockMonobankClient>();
    }

    private static (MonobankClientFactory Factory, RecordingHttpMessageHandler Handler, Mock<IHttpClientFactory> HttpClientFactory)
        CreateFactory(bool enabled = true, string liveToken = LiveToken, string sandboxToken = SandboxToken)
    {
        var handler = new RecordingHttpMessageHandler();
        handler.Responder = _ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                "{ \"invoiceId\": \"INV1\", \"pageUrl\": \"https://pay/x\" }",
                Encoding.UTF8,
                "application/json")
        };

        var httpClientFactory = new Mock<IHttpClientFactory>();
        httpClientFactory.Setup(x => x.CreateClient(It.IsAny<string>())).Returns(new HttpClient(handler));

        var options = Options.Create(new MonobankOptions
        {
            Enabled = enabled,
            Token = liveToken,
            SandboxToken = sandboxToken,
            BaseUrl = BaseUrl,
            PublicKey = "live-public-key",
            SandboxPublicKey = "sandbox-public-key",
            RedirectUrl = "https://redirect.example.com/complete",
            WebhookUrl = "https://webhook.example.com/hook"
        });

        var factory = new MonobankClientFactory(
            httpClientFactory.Object,
            options,
            NullLogger<MonobankClient>.Instance,
            NullLoggerFactory.Instance);

        return (factory, handler, httpClientFactory);
    }

    private sealed class RecordingHttpMessageHandler : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }
        public Func<HttpRequestMessage, HttpResponseMessage> Responder { get; set; } = null!;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Request = request;
            return Task.FromResult(Responder(request));
        }
    }
}