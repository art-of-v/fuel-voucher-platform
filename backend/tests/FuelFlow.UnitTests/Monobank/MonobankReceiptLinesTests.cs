using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using FuelFlow.API.Features.Orders.SharedServices.Monobank;
using FuelFlow.API.Features.Orders.SharedServices.Monobank.Models;
using FuelFlow.SharedKernel.Options;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace FuelFlow.UnitTests.Monobank;

/// <summary>
/// What actually goes on the wire to Monobank.
///
/// The receipt's goods lines are the part that cannot be unit-tested by inspecting a DTO: either
/// <c>merchantPaymInfo.basketOrder</c> is there or the fiscal receipt Monobank registers for us has
/// a total and no goods. Both directions are asserted here by reading the captured request body.
/// </summary>
public sealed class MonobankReceiptLinesTests
{
    private sealed class CapturingHandler : HttpMessageHandler
    {
        public string Body = null!;
        public string RequestPath = null!;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Body = request.Content!.ReadAsStringAsync(cancellationToken).GetAwaiter().GetResult();
            RequestPath = request.RequestUri!.AbsolutePath;

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """{"invoiceId":"p2_TEST","pageUrl":"https://pay.mbnk.biz/p2_TEST"}""",
                    Encoding.UTF8,
                    "application/json"),
            });
        }
    }

    private static (MonobankClient Client, CapturingHandler Handler) Build()
    {
        var handler = new CapturingHandler();
        var options = Options.Create(new MonobankOptions
        {
            RedirectUrl = "https://example.test/return",
            WebhookUrl = "https://example.test/hook",
        });

        var client = new MonobankClient(
            options,
            new HttpClient(handler),
            NullLogger<MonobankClient>.Instance,
            new MonobankCredentials("test-token", "https://api.monobank.ua", "test-public-key"),
            MonobankMerchant.Live);

        return (client, handler);
    }

    private static JsonElement MerchantPaymInfo(string body)
        => JsonDocument.Parse(body).RootElement.GetProperty("merchantPaymInfo");

    [Fact]
    public async Task CreateInvoice_SendsBasketOrder_ForEveryLine()
    {
        var (client, handler) = Build();

        await client.CreateInvoiceAsync(new MonobankInvoiceRequest
        {
            Amount = 20000,
            MerchantPaymentInfo = "FuelFlow Order 7258a522",
            Destination = "Паливо",
            Items =
            [
                new MonobankBasketItem
                {
                    Name = "А-95", Qty = 10m, Sum = 9500, Total = 95000,
                    Unit = "л", Code = "okko-a95",
                },
                new MonobankBasketItem
                {
                    Name = "ДП ЄВРО", Qty = 2m, Sum = 10000, Total = 20000,
                    Unit = "л", Code = "okko-diesel",
                },
            ],
        });

        handler.RequestPath.Should().Be("/api/merchant/invoice/create");

        var info = MerchantPaymInfo(handler.Body);
        info.GetProperty("reference").GetString().Should().Be("FuelFlow Order 7258a522");

        // The purchase description used to be a copy of the reference, so the receipt read
        // "FuelFlow Order 7258a522-d402-..." instead of what the customer bought.
        info.GetProperty("destination").GetString().Should().Be("Паливо");

        var basket = info.GetProperty("basketOrder");
        basket.GetArrayLength().Should().Be(2);

        var first = basket[0];
        first.GetProperty("name").GetString().Should().Be("А-95");
        first.GetProperty("qty").GetDecimal().Should().Be(10m);
        first.GetProperty("sum").GetInt64().Should().Be(9500, "the price of ONE litre");
        first.GetProperty("total").GetInt64().Should().Be(95000);
        first.GetProperty("unit").GetString().Should().Be("л");
        first.GetProperty("code").GetString().Should().Be("okko-a95");
    }

    [Fact]
    public async Task CreateInvoice_OmitsBasketOrder_Entirely_WhenThereIsNothingToDeclare()
    {
        var (client, handler) = Build();

        await client.CreateInvoiceAsync(new MonobankInvoiceRequest
        {
            Amount = 20000,
            MerchantPaymentInfo = "FuelFlow Order 7258a522",
        });

        // Absent, not an empty array: once fiscalisation is linked Monobank treats the two
        // differently, and an empty basket is a receipt that declares nothing.
        MerchantPaymInfo(handler.Body).TryGetProperty("basketOrder", out _).Should().BeFalse();
    }

    [Fact]
    public async Task CreateInvoice_FallsBackToTheFuel_WhenTheCallerDeclaresNoDestination()
    {
        var (client, handler) = Build();

        await client.CreateInvoiceAsync(new MonobankInvoiceRequest
        {
            Amount = 20000,
            MerchantPaymentInfo = "FuelFlow Voucher Renewal",
        });

        // The acquirer prints what we declare it, so the fallback is on a fiscal receipt. It used
        // to be the CP437 twin of "Паливо" - box-drawing characters in place of the letters - and
        // two checkout paths reach it: a voucher renewal and a multi-item basket, neither of which
        // declares a destination of its own. A single checkout declared "Паливо" and hid the
        // damage, which is why the garbled string survived in the source.
        MerchantPaymInfo(handler.Body)
            .GetProperty("destination")
            .GetString()
            .Should()
            .Be("Паливо");
    }

    [Fact]
    public async Task CancelInvoice_SendsTheReturnedGoods()
    {
        var (client, handler) = Build();

        await client.CancelInvoiceAsync(
            "261012BKDuf6zFPD4pME",
            9500,
            "idem-1",
            [
                new MonobankBasketItem
                {
                    Name = "А-95", Qty = 5m, Sum = 9500, Total = 47500,
                    Unit = "л", Code = "okko-a95",
                },
            ]);

        handler.RequestPath.Should().Be("/api/merchant/invoice/cancel");

        using var doc = JsonDocument.Parse(handler.Body);
        doc.RootElement.GetProperty("invoiceId").GetString().Should().Be("261012BKDuf6zFPD4pME");
        doc.RootElement.GetProperty("amount").GetInt64().Should().Be(9500);
        doc.RootElement.GetProperty("extRef").GetString().Should().Be("idem-1");

        var item = doc.RootElement.GetProperty("items")[0];
        item.GetProperty("name").GetString().Should().Be("А-95");
        item.GetProperty("qty").GetDecimal().Should().Be(5m);
    }

    [Fact]
    public async Task CancelInvoice_OmitsItems_WhenNoneGiven()
    {
        var (client, handler) = Build();

        await client.CancelInvoiceAsync("261012BKDuf6zFPD4pME", 9500, "idem-1");

        using var doc = JsonDocument.Parse(handler.Body);
        doc.RootElement.TryGetProperty("items", out _).Should().BeFalse();
    }
}
