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
        var (client, handler) = CreateClient(
            "{ \"invoiceId\": \"INV1\", \"pageUrl\": \"https://pay/x\", \"appUrl\": \"https://mbnk.app/or/x\" }");

        var response = await client.CreateInvoiceAsync(new MonobankInvoiceRequest
        {
            Amount = 10000,
            Currency = "UAH",
            MerchantPaymentInfo = "order-123"
        });

        response.InvoiceId.Should().Be("INV1");
        response.PageUrl.Should().Be("https://pay/x");
        response.AppUrl.Should().Be("https://mbnk.app/or/x");

        handler.Request.Should().NotBeNull();
        handler.Request!.Method.Should().Be(HttpMethod.Post);
        handler.Request.RequestUri!.PathAndQuery.Should().Be("/api/merchant/invoice/create");
        handler.Request.Headers.GetValues("X-Token").Should().Contain(Token);

        handler.RequestBody.Should().NotBeNull();
        handler.RequestBody.Should().Contain("\"amount\":10000");
        handler.RequestBody.Should().Contain("\"ccy\":980");
        handler.RequestBody.Should().Contain("\"reference\":\"order-123");
        handler.RequestBody.Should().Contain("\"redirectUrl\":\"https://redirect.example.com/complete");
        handler.RequestBody.Should().Contain("\"webHookUrl\":\"https://webhook.example.com/hook");

        // The customer must not be pushed out to a browser to pay. These two flags are what make
        // that possible, so they are asserted rather than left to a comment: withAppUrl adds the
        // monobank-app deep link, displayType switches pageUrl to the embeddable /frame/ variant.
        handler.RequestBody.Should().Contain("\"withAppUrl\":true");
        handler.RequestBody.Should().Contain("\"displayType\":\"iframe\"");
    }

    [Fact]
    public async Task CreateInvoiceAsync_ShouldTolerateMissingAppUrl()
    {
        // An older or partial Monobank response simply omits appUrl. Every consumer treats it as
        // optional, so a missing one must deserialise to null rather than throw - a null here would
        // otherwise take down checkout for a payment that was created fine.
        var (client, _) = CreateClient("{ \"invoiceId\": \"INV3\", \"pageUrl\": \"https://pay/y\" }");

        var response = await client.CreateInvoiceAsync(new MonobankInvoiceRequest { Amount = 100 });

        response.InvoiceId.Should().Be("INV3");
        response.AppUrl.Should().BeNull();
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

    /// <summary>
    /// The statement is the only source that can contradict us - invoice/status can only confirm
    /// what we already assumed - so its parsing is pinned rather than assumed.
    /// </summary>
    public class StatementTests
    {
        private static readonly DateTime From = new(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);
        private static readonly DateTime To = new(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc);

        [Fact]
        public async Task GetStatementAsync_ShouldGetTheRangeAndDeserialize()
        {
            var (client, handler) = CreateClient(
                """
                [
                  { "statementId": "ST1", "invoiceId": "INV1", "reference": "order-1",
                    "amount": 123456, "ccy": 980, "status": "settled",
                    "createdDate": "2026-10-05T09:00:00Z", "maskedCard": "5375 **** **** 1234" }
                ]
                """);

            var items = await client.GetStatementAsync(From, To);

            items.Should().HaveCount(1);
            var item = items[0];
            item.StatementId.Should().Be("ST1");
            item.InvoiceId.Should().Be("INV1");
            item.Reference.Should().Be("order-1");
            // Kopecks, never converted to a decimal here - the reconciler compares them against
            // our own kopeck-exact amounts and a conversion is where the 978.57/979 class of bug
            // comes from.
            item.Amount.Should().Be(123456);
            item.Ccy.Should().Be(980);
            item.Status.Should().Be("settled");
            item.MaskedCard.Should().Be("5375 **** **** 1234");

            handler.Request.Should().NotBeNull();
            handler.Request!.Method.Should().Be(HttpMethod.Get);
            handler.Request.RequestUri!.AbsolutePath.Should().Be("/api/merchant/statement");
            handler.Request.RequestUri.Query.Should().Contain("from=");
            handler.Request.RequestUri.Query.Should().Contain("to=");
            handler.Request.Headers.GetValues("X-Token").Should().Contain(Token);
        }

        // Consecutive periods must tile without a boundary row landing in both months.
        [Fact]
        public async Task GetStatementAsync_ShouldSendTheBoundsVerbatim()
        {
            var (client, handler) = CreateClient("[]");

            await client.GetStatementAsync(From, To);

            var query = Uri.UnescapeDataString(handler.Request!.RequestUri!.Query);
            query.Should().Contain("from=2026-10-01T00:00:00Z");
            query.Should().Contain("to=2026-11-01T00:00:00Z");
        }

        [Fact]
        public async Task GetStatementAsync_ShouldThrowOnAnErrorStatus()
        {
            var (client, _) = CreateClient("{ \"error\": \"nope\" }", HttpStatusCode.Unauthorized);

            var act = () => client.GetStatementAsync(From, To);

            // The status is rendered as the enum name, not the numeric code - the message reads
            // "Monobank API returned Unauthorized: ...".
            await act.Should().ThrowAsync<HttpRequestException>()
                .WithMessage("*Unauthorized*nope*");
        }

        // A row that is missing a field must not take the whole period down with it: the acquirer
        // is the authority here, and losing the rows that did parse hides the ones that matter.
        [Fact]
        public async Task GetStatementAsync_ShouldKeepRowsThatOmitOptionalFields()
        {
            var (client, _) = CreateClient("""[ { "statementId": "ST1" } ]""");

            var items = await client.GetStatementAsync(From, To);

            items.Should().HaveCount(1);
            items[0].StatementId.Should().Be("ST1");
            items[0].Amount.Should().BeNull();
            items[0].InvoiceId.Should().BeNull();
        }

        [Fact]
        public async Task GetStatementAsync_ShouldTreatAnEmptyBodyAsNoRows()
        {
            var (client, _) = CreateClient("null");

            var items = await client.GetStatementAsync(From, To);

            items.Should().BeEmpty();
        }
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

        // The client is bound to one merchant's credentials at construction (the factory does
        // this in production); the merchant itself only labels log lines.
        var client = new MonobankClient(
            options,
            httpClient,
            NullLogger<MonobankClient>.Instance,
            new MonobankCredentials(Token, BaseUrl, PublicKey: "test-public-key"),
            MonobankMerchant.Live);
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
