using System.Net;
using System.Text;
using FluentAssertions;
using FuelFlow.API.Features.Orders.SharedServices.Monobank;
using FuelFlow.API.Features.Orders.SharedServices.Monobank.Models;
using FuelFlow.SharedKernel.Options;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace FuelFlow.UnitTests.Monobank;

public class MonobankClientTests
{
    private const string BaseUrl = "https://api.monobank.ua";
    private const string Token = "test-token";

    [Fact]
    public async Task CreateInvoiceAsync_ShouldPostAndDeserialize()
    {
        var (client, handler) = CreateClient("{ \"invoiceId\": \"INV1\", \"pageUrl\": \"https://pay/x\" }");

        var response = await client.CreateInvoiceAsync(new MonobankInvoiceRequest
        {
            Amount = 10000,
            Currency = "UAH",
            MerchantPaymentInfo = "order-123"
        });

        response.InvoiceId.Should().Be("INV1");
        response.PageUrl.Should().Be("https://pay/x");

        handler.Request.Should().NotBeNull();
        handler.Request!.Method.Should().Be(HttpMethod.Post);
        handler.Request.RequestUri!.PathAndQuery.Should().Be("/api/merchant/invoice/create");
        handler.Request.Headers.GetValues("X-Token").Should().Contain(Token);

        handler.RequestBody.Should().NotBeNull();
        handler.RequestBody.Should().Contain("\"amount\":10000");
        handler.RequestBody.Should().Contain("\"ccy\":980");
        handler.RequestBody.Should().Contain("\"reference\":\"order-123\"");
        handler.RequestBody.Should().Contain("\"redirectUrl\":\"https://redirect.example.com/complete\"");
        handler.RequestBody.Should().Contain("\"webHookUrl\":\"https://webhook.example.com/hook\"");
    }

    [Fact]
    public async Task CreateInvoiceAsync_ShouldThrow_OnNonSuccess()
    {
        var (client, _) = CreateClient("internal server error", HttpStatusCode.InternalServerError);

        var act = () => client.CreateInvoiceAsync(new MonobankInvoiceRequest { Amount = 100 });

        await act.Should().ThrowAsync<HttpRequestException>();
    }

    [Fact]
    public async Task GetInvoiceStatusAsync_ShouldGetAndDeserialize()
    {
        var json = "{ \"invoiceId\": \"INV2\", \"status\": \"created\", \"amount\": 5000, " +
                   "\"createdDate\": \"2026-01-01T10:00:00Z\", \"modifiedDate\": \"2026-01-02T10:00:00Z\" }";
        var (client, handler) = CreateClient(json);

        var response = await client.GetInvoiceStatusAsync("INV2");

        response.InvoiceId.Should().Be("INV2");
        response.Status.Should().Be("created");
        response.Amount.Should().Be(5000);
        response.CreatedDate.Should().Be(new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc));
        response.ModifiedDate.Should().Be(new DateTime(2026, 1, 2, 10, 0, 0, DateTimeKind.Utc));

        handler.Request.Should().NotBeNull();
        handler.Request!.Method.Should().Be(HttpMethod.Get);
        handler.Request.RequestUri!.PathAndQuery.Should().Contain("invoiceId=INV2");
    }

    private static (MonobankClient Client, RecordingHttpMessageHandler Handler) CreateClient(
        string responseJson,
        HttpStatusCode statusCode = HttpStatusCode.OK)
    {
        var handler = new RecordingHttpMessageHandler();
        handler.Responder = _ => new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(responseJson, Encoding.UTF8, "application/json")
        };

        var httpClient = new HttpClient(handler);
        var options = Options.Create(new MonobankOptions
        {
            Token = Token,
            BaseUrl = BaseUrl,
            WebhookUrl = "https://webhook.example.com/hook",
            RedirectUrl = "https://redirect.example.com/complete"
        });

        var client = new MonobankClient(options, httpClient, NullLogger<MonobankClient>.Instance);
        return (client, handler);
    }

    private sealed class RecordingHttpMessageHandler : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }
        public string? RequestBody { get; private set; }
        public Func<HttpRequestMessage, HttpResponseMessage> Responder { get; set; } = null!;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Request = request;
            if (request.Content != null)
            {
                RequestBody = await request.Content.ReadAsStringAsync(cancellationToken);
            }

            return Responder(request);
        }
    }
}
