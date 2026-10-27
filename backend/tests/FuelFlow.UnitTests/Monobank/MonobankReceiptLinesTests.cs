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
            // The invoice total and the basket total are the same money, so the fixture keeps them
            // agreeing: 2 × 862.00 + 1 × 276.00.
            Amount = 200000,
            MerchantPaymentInfo = "FuelFlow Order 7258a522",
            Destination = "Рекламні послуги",
            Items =
            [
                new MonobankBasketItem
                {
                    Name = "Рекламні послуги", Qty = 2m, Sum = 86200, Total = 172400,
                    Unit = "послуга", Code = "3808d410-0000-0000-0000-000000000000",
                },
                new MonobankBasketItem
                {
                    Name = "Рекламні послуги", Qty = 1m, Sum = 27600, Total = 27600,
                    Unit = "послуга", Code = "4c1f0a11-0000-0000-0000-000000000000",
                },
            ],
        });

        handler.RequestPath.Should().Be("/api/merchant/invoice/create");

        var info = MerchantPaymInfo(handler.Body);
        info.GetProperty("reference").GetString().Should().Be("FuelFlow Order 7258a522");

        // The purchase description used to be a copy of the reference, so the receipt read
        // "FuelFlow Order 7258a522-d402-..." instead of what the customer bought.
        info.GetProperty("destination").GetString().Should().Be("Рекламні послуги");

        var basket = info.GetProperty("basketOrder");
        basket.GetArrayLength().Should().Be(2);

        var first = basket[0];
        first.GetProperty("name").GetString().Should().Be("Рекламні послуги");
        first.GetProperty("qty").GetDecimal().Should().Be(2m);
        first.GetProperty("sum").GetInt64().Should().Be(86200, "the price of ONE code");
        first.GetProperty("total").GetInt64().Should().Be(172400);
        first.GetProperty("unit").GetString().Should().Be("послуга");
        first.GetProperty("code").GetString().Should().Be("3808d410-0000-0000-0000-000000000000");
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
    public async Task CreateInvoice_FallsBackToTheService_WhenTheCallerDeclaresNoDestination()
    {
        var (client, handler) = Build();

        await client.CreateInvoiceAsync(new MonobankInvoiceRequest
        {
            Amount = 20000,
            MerchantPaymentInfo = "FuelFlow Voucher Renewal",
        });

        // The acquirer prints what we declare it, so the fallback is on a fiscal receipt. It used
        // to be the CP437 twin of the fuel purpose - box-drawing characters in place of the letters
        // - and two checkout paths reach it: a voucher renewal and a multi-item basket, neither of
        // which declares a destination of its own. A single checkout declared one and hid the
        // damage, which is why the garbled string survived in the source.
        //
        // What it says now is the answer to "what did the customer buy", and the answer is the
        // advertising service the QR code is sold under, not the fuel the station sells.
        MerchantPaymInfo(handler.Body)
            .GetProperty("destination")
            .GetString()
            .Should()
            .Be(MonobankClient.DefaultDestination)
            .And.Be("Рекламні послуги");
    }

    [Fact]
    public async Task CancelInvoice_SendsTheReturnedServices()
    {
        var (client, handler) = Build();

        await client.CancelInvoiceAsync(
            "261012BKDuf6zFPD4pME",
            86200,
            "idem-1",
            [
                new MonobankBasketItem
                {
                    Name = "Рекламні послуги", Qty = 1m, Sum = 86200, Total = 86200,
                    Unit = "послуга", Code = "3808d410-0000-0000-0000-000000000000",
                },
            ]);

        handler.RequestPath.Should().Be("/api/merchant/invoice/cancel");

        using var doc = JsonDocument.Parse(handler.Body);
        doc.RootElement.GetProperty("invoiceId").GetString().Should().Be("261012BKDuf6zFPD4pME");
        doc.RootElement.GetProperty("amount").GetInt64().Should().Be(86200);
        doc.RootElement.GetProperty("extRef").GetString().Should().Be("idem-1");

        var item = doc.RootElement.GetProperty("items")[0];
        item.GetProperty("name").GetString().Should().Be("Рекламні послуги");
        item.GetProperty("qty").GetDecimal().Should().Be(1m);
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
