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

        /// <summary>A row exactly as the acquirer documents it: the envelope is
        /// <c>{"list":[...]}</c>, and the row carries no id of its own.</summary>
        private const string OneRow = """
            {
              "list": [
                {
                  "invoiceId": "2205175v4MfatvmUL2oR",
                  "status": "success",
                  "maskedPan": "444403*******1902",
                  "date": "2026-10-05T09:00:00Z",
                  "amount": 123456,
                  "ccy": 980,
                  "profitAmount": 120000,
                  "reference": "FuelFlow Order 7258a522",
                  "destination": "Паливо",
                  "approvalCode": "662476",
                  "rrn": "060189181768",
                  "paymentScheme": "full",
                  "shortQrId": null,
                  "cancelList": []
                }
              ]
            }
            """;

        [Fact]
        public async Task GetStatementAsync_ShouldGetTheRangeAndDeserialize()
        {
            var (client, handler) = CreateClient(OneRow);

            var items = await client.GetStatementAsync(From, To);

            items.Should().HaveCount(1);
            var item = items[0];
            item.InvoiceId.Should().Be("2205175v4MfatvmUL2oR");
            item.Reference.Should().Be("FuelFlow Order 7258a522");
            item.Destination.Should().Be("Паливо");
            // Kopecks, never converted to a decimal here - the reconciler compares them against
            // our own kopeck-exact amounts and a conversion is where the 978.57/979 class of bug
            // comes from.
            item.Amount.Should().Be(123456);
            item.Ccy.Should().Be(980);
            item.Status.Should().Be("success");
            item.MaskedPan.Should().Be("444403*******1902");
            item.Rrn.Should().Be("060189181768");
            item.ApprovalCode.Should().Be("662476");
            // `date` is the financial operation time, and it is what a period is filtered on.
            item.Date.Should().Be(new DateTime(2026, 10, 5, 9, 0, 0, DateTimeKind.Utc));
            item.RawJson.Should().Contain("\"cancelList\"");

            handler.Request.Should().NotBeNull();
            handler.Request!.Method.Should().Be(HttpMethod.Get);
            handler.Request.RequestUri!.AbsolutePath.Should().Be("/api/merchant/statement");
            handler.Request.RequestUri.Query.Should().Contain("from=");
            handler.Request.RequestUri.Query.Should().Contain("to=");
            handler.Request.Headers.GetValues("X-Token").Should().Contain(Token);
        }

        // The acquirer documents from/to as UTC unix timestamps and answers an ISO-8601 instant
        // with 400 {"errCode":"1001","errText":"invalid 'from'"} - which is what every import pass
        // used to send, so the statement was never fetched and the act built on it stayed empty.
        [Fact]
        public async Task GetStatementAsync_ShouldSendTheBoundsAsUnixTimestamps()
        {
            var (client, handler) = CreateClient("""{ "list": [] }""");

            await client.GetStatementAsync(From, To);

            var query = Uri.UnescapeDataString(handler.Request!.RequestUri!.Query);
            query.Should().Contain($"from={new DateTimeOffset(From).ToUnixTimeSeconds()}");
            query.Should().Contain($"to={new DateTimeOffset(To).ToUnixTimeSeconds()}");
            query.Should().NotContain("T00:00:00Z");
        }

        // Consecutive periods must tile without a boundary row landing in both months, and the
        // bounds carry a time of day rather than being rounded out to whole days.
        [Fact]
        public async Task GetStatementAsync_ShouldSendTheBoundsVerbatim()
        {
            var (client, handler) = CreateClient("""{ "list": [] }""");
            var from = new DateTime(2026, 10, 1, 6, 17, 5, DateTimeKind.Utc);
            var to = new DateTime(2026, 11, 3, 6, 17, 5, DateTimeKind.Utc);

            await client.GetStatementAsync(from, to);

            var query = Uri.UnescapeDataString(handler.Request!.RequestUri!.Query);
            query.Should().Contain($"from={new DateTimeOffset(from).ToUnixTimeSeconds()}");
            query.Should().Contain($"to={new DateTimeOffset(to).ToUnixTimeSeconds()}");
        }

        // The statement has no per-row id, so the key is composed from what it does send. Two fetches
        // of the same period have to agree on it or every pass inserts the whole window again.
        [Fact]
        public async Task GetStatementAsync_ShouldComposeTheSameKeyForTheSameRow()
        {
            var (first, _) = CreateClient(OneRow);
            var (second, _) = CreateClient(OneRow);

            var one = await first.GetStatementAsync(From, To);
            var two = await second.GetStatementAsync(From, To);

            one[0].StatementId.Should().NotBeNullOrWhiteSpace();
            two[0].StatementId.Should().Be(one[0].StatementId);
        }

        // Two payments on the same card, same second, same amount, are only distinguishable by the
        // invoice or the payment-system id - so the composed key has to include the identity, not
        // just the money.
        [Fact]
        public async Task GetStatementAsync_ShouldTellTwoRowsOfTheSameAmountApart()
        {
            var (client, _) = CreateClient("""
                { "list": [
                    { "invoiceId": "INV-A", "date": "2026-10-05T09:00:00Z", "amount": 5000, "ccy": 980 },
                    { "invoiceId": "INV-B", "date": "2026-10-05T09:00:00Z", "amount": 5000, "ccy": 980 }
                  ] }
                """);

            var items = await client.GetStatementAsync(From, To);

            items.Should().HaveCount(2);
            items[0].StatementId.Should().NotBe(items[1].StatementId);
        }

        [Fact]
        public async Task GetStatementAsync_ShouldThrowOnAnErrorStatus()
        {
            var (client, _) = CreateClient(
                """{ "errCode": "1001", "errText": "invalid 'from'" }""", HttpStatusCode.BadRequest);

            var act = () => client.GetStatementAsync(From, To);

            // The status is rendered as the enum name, not the numeric code - the message reads
            // "Monobank API returned BadRequest: ...". The acquirer's own error body rides along, so
            // the log says which parameter it objected to without anyone opening their docs.
            await act.Should().ThrowAsync<HttpRequestException>()
                .WithMessage("*BadRequest*invalid 'from'*");
        }

        // A 200 we cannot read is not a period with nothing in it: reading it as one would publish an
        // act that silently omits every payment the acquirer reported.
        [Fact]
        public async Task GetStatementAsync_ShouldRefuseABodyWithoutAList()
        {
            var (client, _) = CreateClient("""[ { "invoiceId": "INV1" } ]""");

            var act = () => client.GetStatementAsync(From, To);

            await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*no 'list'*");
        }

        // A row that is missing a field must not take the whole period down with it: the acquirer
        // is the authority here, and losing the rows that did parse hides the ones that matter.
        [Fact]
        public async Task GetStatementAsync_ShouldKeepRowsThatOmitOptionalFields()
        {
            var (client, _) = CreateClient("""{ "list": [ { "invoiceId": "INV1", "date": "2026-10-05T09:00:00Z" } ] }""");

            var items = await client.GetStatementAsync(From, To);

            items.Should().HaveCount(1);
            items[0].InvoiceId.Should().Be("INV1");
            items[0].Amount.Should().BeNull();
            items[0].Rrn.Should().BeNull();
        }

        // Nothing in the row names it, so it cannot be filed without risking one such row overwriting
        // another. Skipping it loudly beats a period that imports half of itself.
        [Fact]
        public async Task GetStatementAsync_ShouldSkipARowWithNoIdentity()
        {
            var (client, _) = CreateClient("""{ "list": [ { "amount": 5000, "ccy": 980 }, { "invoiceId": "INV1" } ] }""");

            var items = await client.GetStatementAsync(From, To);

            items.Should().ContainSingle();
            items[0].InvoiceId.Should().Be("INV1");
        }

        [Fact]
        public async Task GetStatementAsync_ShouldTreatAnEmptyListAsNoRows()
        {
            var (client, _) = CreateClient("""{ "list": [] }""");

            var items = await client.GetStatementAsync(From, To);

            items.Should().BeEmpty();
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
