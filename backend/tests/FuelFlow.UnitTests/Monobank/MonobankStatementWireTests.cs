using System.Text.Json;
using FluentAssertions;
using FuelFlow.API.Features.Orders.SharedServices.Monobank.Models;
using Xunit;

namespace FuelFlow.UnitTests.Monobank;

/// <summary>
/// The statement row as the acquirer sends it, read through the model that receives it.
/// </summary>
/// <remarks>
/// <para>
/// The payloads here are the <b>documented</b> shape, not an observed one: the #118 campaign's
/// P6/S12 - a real sandbox reversal, read back out of the stored <c>raw_payload</c> - has not been
/// run, so nothing here has been checked against a captured response. They pin the model against
/// the documentation, which is what the model claims to be field-for-field with.
/// </para>
/// </remarks>
public class MonobankStatementWireTests
{
    private static readonly JsonSerializerOptions WireOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public void The_Cancellations_Are_Read_Off_The_Wire()
    {
        // The reversals Monobank nests under the payment they reverse. The link to that payment is
        // the NESTING - the parent row - and the entry itself carries no invoice id, which is why
        // it can never be a match key on its own.
        const string payload = """
        {
          "invoiceId": "INV-1",
          "status": "success",
          "maskedPan": "444403*******1902",
          "date": "2026-10-05T09:00:00Z",
          "amount": 97857,
          "ccy": 980,
          "reference": "FuelFlow Order 1",
          "destination": "Паливо",
          "approvalCode": "AA123",
          "rrn": "RRN-1",
          "cancelList": [
            {
              "amount": 97857,
              "ccy": 980,
              "date": "2026-10-06T09:00:00Z",
              "maskedPan": "444403*******1902",
              "approvalCode": "AA123",
              "rrn": "RRN-2"
            }
          ]
        }
        """;

        var item = JsonSerializer.Deserialize<MonobankStatementItem>(payload, WireOptions);

        item.Should().NotBeNull();
        item!.CancelList.Should().ContainSingle();

        var reversal = item.CancelList[0];
        reversal.Amount.Should().Be(97857, "kopecks, as the acquirer reports them");
        reversal.Ccy.Should().Be(980);
        reversal.Date.Should().Be(new DateTime(2026, 10, 6, 9, 0, 0, DateTimeKind.Utc));
        reversal.MaskedPan.Should().Be("444403*******1902");
        reversal.ApprovalCode.Should().Be("AA123");
        reversal.Rrn.Should().Be("RRN-2");
    }

    [Fact]
    public void A_Payment_Nobody_Reversed_Carries_No_Cancellations()
    {
        const string payload = """{ "invoiceId": "INV-1", "amount": 100, "ccy": 980 }""";

        var item = JsonSerializer.Deserialize<MonobankStatementItem>(payload, WireOptions);

        item.Should().NotBeNull();
        item!.CancelList.Should().BeEmpty("a payment nobody reversed has nothing nested under it");
    }

    [Fact]
    public void The_Entry_That_Is_Not_There_Leaves_The_Reversal_Empty_Rather_Than_Failing_The_Row()
    {
        // A field that is suddenly absent from a response is information the reconciliation needs to
        // see - not a reason to lose the whole row.
        const string payload = """{ "invoiceId": "INV-1", "amount": 100, "ccy": 980, "cancelList": null }""";

        var item = JsonSerializer.Deserialize<MonobankStatementItem>(payload, WireOptions);

        item.Should().NotBeNull();
        item!.InvoiceId.Should().Be("INV-1");
        item.CancelList.Should().BeEmpty();
    }
}
