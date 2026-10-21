using FluentAssertions;
using FuelFlow.Features.Acquiring;
using FuelFlow.Features.Acquiring.Reconciliation;
using FuelFlow.Features.Orders.SharedModels;
using FuelFlow.Payments.SharedModels;
using FuelFlow.SharedKernel.Options;
using FuelFlow.Testing;
using Xunit;

namespace FuelFlow.UnitTests.Acquiring;

public class StatementMatcherTests
{
    private static MonobankStatementEntry Entry(
        string statementId,
        string? invoiceId,
        long? amountKopecks) => new()
        {
            Id = Guid.NewGuid(),
            Merchant = MonobankMerchant.Live,
            StatementId = statementId,
            InvoiceId = invoiceId,
            AmountKopecks = amountKopecks,
            AcquirerCreatedAtUtc = new DateTime(2026, 10, 5, 9, 0, 0, DateTimeKind.Utc)
        };

    /// <summary>
    /// The note on a matched reversal names the order's own idempotency key.
    /// </summary>
/// <remarks>
/// It used to name <c>refunds.ext_ref</c>, a copy of that key taken when the refund was written.
    /// The copy could drift from the order - nothing kept them equal - and nothing asserted on the
/// text, so a stale or wrong reference would have reached an operator reading the reconciliation
/// report without any test noticing. Reading the order closes both gaps at once.
/// </remarks>
[Fact]
    public void A_Refund_Match_Names_The_Orders_Own_Reference()
    {
        var order = Order("INV1", 978.57m);
        order.IdempotencyKey = "checkout-abc-123";

        var refund = RefundFor("INV1", 97857);
        refund.OrderId = order.Id;

        var rows = StatementMatcher.Match(
            new[] { Entry("ST1", "INV1", 97857) },
            new[] { order },
            new[] { refund });

        rows.Should().ContainSingle();
        rows[0].Status.Should().Be(StatementMatchStatus.RefundMatch);
        rows[0].Note.Should().Contain("checkout-abc-123");
    }

    private static Order Order(string invoiceId, decimal price) => new()
    {
        Id = Guid.NewGuid(),
        UserId = Guid.NewGuid(),
        MonobankInvoiceId = invoiceId,
        Price = price,
        Status = OrderStatus.Fulfilled
    };

    /// <summary>
    /// A completed refund payment - the shape <c>StatementMatcher</c> now matches on. The note it
    /// writes names the refund's order, so the order's idempotency key is the reference the
    /// assertion below expects rather than a copy of it on the refund.
    /// </summary>
    private static Payment RefundFor(string invoiceId, int amountKopecks) => new()
    {
        Id = Guid.NewGuid(),
        OrderId = Guid.NewGuid(),
        Kind = PaymentKind.Refund,
        Provider = "monobank",
        ProviderInvoiceId = invoiceId,
        AmountUah = amountKopecks / 100m,
        AmountKopecks = amountKopecks,
        Currency = "UAH",
        Status = PaymentStatus.Succeeded,
        PaidAtUtc = TestClock.UtcNow,
        CreatedAtUtc = TestClock.UtcNow,
        UpdatedAtUtc = TestClock.UtcNow
    };

    [Fact]
    public void An_Invoice_We_Know_And_An_Amount_That_Agrees_Is_Matched()
    {
        var rows = StatementMatcher.Match(
            new[] { Entry("ST1", "INV1", 97857) },
            new[] { Order("INV1", 978.57m) },
            Array.Empty<Payment>());

        rows.Should().ContainSingle();
        rows[0].Status.Should().Be(StatementMatchStatus.Matched);
        rows[0].AcquirerAmount.Should().Be(978.57m);
        rows[0].LocalAmount.Should().Be(978.57m);
        rows[0].Note.Should().BeNull();
    }

    // The case the table exists for: the acquirer says one figure, we recorded another.
    [Fact]
    public void An_Invoice_We_Know_But_A_Different_Amount_Is_A_Mismatch()
    {
        var rows = StatementMatcher.Match(
            new[] { Entry("ST1", "INV1", 97900) },
            new[] { Order("INV1", 978.57m) },
            Array.Empty<Payment>());

        rows.Should().ContainSingle();
        rows[0].Status.Should().Be(StatementMatchStatus.AmountMismatch);
        rows[0].Note.Should().Contain("979.00");
        rows[0].Note.Should().Contain("978.57");
    }

    // 978.57 UAH is 97857 kopecks exactly. If the comparison went the other way - multiplying the
    // price by 100 - a rounding decision would decide whether a correct payment reported as a
    // mismatch.
    [Fact]
    public void Kopecks_And_Uah_Compare_Without_A_Rounding_Decision()
    {
        var rows = StatementMatcher.Match(
            new[] { Entry("ST1", "INV1", 97857) },
            new[] { Order("INV1", 978.57m) },
            Array.Empty<Payment>());

        rows[0].Status.Should().Be(StatementMatchStatus.Matched);
    }

    [Fact]
    public void A_Price_With_More_Decimal_Places_Than_Kopecks_Is_A_Mismatch_Not_A_Match()
    {
        // The acquirer cannot pay fractional kopecks, so this is a genuine disagreement and must not
        // be quietly rounded into agreement.
        var rows = StatementMatcher.Match(
            new[] { Entry("ST1", "INV1", 97857) },
            new[] { Order("INV1", 978.571m) },
            Array.Empty<Payment>());

        rows[0].Status.Should().Be(StatementMatchStatus.AmountMismatch);
    }

    // The whole reason the category exists: real money we cannot attribute to anything.
    [Fact]
    public void An_Invoice_We_Have_Never_Seen_Is_Unmatched_And_Still_Reported()
    {
        var rows = StatementMatcher.Match(
            new[] { Entry("ST1", "INV-UNKNOWN", 50000) },
            new[] { Order("INV1", 500m) },
            Array.Empty<Payment>());

        rows.Should().ContainSingle();
        rows[0].Status.Should().Be(StatementMatchStatus.Unmatched);
        rows[0].OrderId.Should().BeNull();
        rows[0].Note.Should().Contain("No order");
    }

    [Fact]
    public void A_Row_With_No_Invoice_At_All_Is_Unmatched()
    {
        var rows = StatementMatcher.Match(
            new[] { Entry("ST1", null, 50000) },
            new[] { Order("INV1", 500m) },
            Array.Empty<Payment>());

        rows[0].Status.Should().Be(StatementMatchStatus.Unmatched);
    }

    // Bulk checkouts send "FuelFlow Bundle" and renewals send "FuelFlow Voucher Renewal". Matching
    // on that string would hand one acquirer row to whichever order happened to come first.
    [Fact]
    public void Reference_Is_Never_Used_To_Match()
    {
        var live = Order("INV-LIVE", 500m);
        var alsoLive = Order("INV-OTHER", 700m);
        live.MonobankInvoiceId = "INV-LIVE";

        var entry = Entry("ST1", "INV-LIVE", 50000);
        entry.Reference = "FuelFlow Bundle";

        var rows = StatementMatcher.Match(
            new[] { entry },
            new[] { alsoLive, live },
            Array.Empty<Payment>());

        rows.Should().ContainSingle();
        rows[0].Status.Should().Be(StatementMatchStatus.Matched);
        rows[0].OrderId.Should().Be(live.Id);
    }

    [Fact]
    public void A_Refund_We_Issued_Is_Reported_As_A_Refund_Match()
    {
        var refund = RefundFor("INV1", 50000);

        var rows = StatementMatcher.Match(
            new[] { Entry("ST1", "INV1", -50000) },
            new[] { Order("INV1", 500m) },
            new[] { refund });

        rows.Should().ContainSingle();
        rows[0].Status.Should().Be(StatementMatchStatus.RefundMatch);
        rows[0].OrderId.Should().Be(refund.OrderId);
    }

    // A refund takes precedence over the order it reverses: the same invoice id legitimately appears
    // on both sides, and the acquirer row is describing the reversal.
    [Fact]
    public void A_Refund_Wins_Over_The_Order_It_Reverses()
    {
        var rows = StatementMatcher.Match(
            new[] { Entry("ST1", "INV1", -50000) },
            new[] { Order("INV1", 500m) },
            new[] { RefundFor("INV1", 50000) });

        rows[0].Status.Should().Be(StatementMatchStatus.RefundMatch);
    }

    [Fact]
    public void A_Row_With_No_Amount_Is_Not_Treated_As_A_Mismatch()
    {
        // We cannot compare an amount we were not told. Reporting that as a mismatch would fill the
        // act with false alarms; reporting it as matched would overstate what we know.
        var rows = StatementMatcher.Match(
            new[] { Entry("ST1", "INV1", null) },
            new[] { Order("INV1", 978.57m) },
            Array.Empty<Payment>());

        rows[0].Status.Should().Be(StatementMatchStatus.Matched);
        rows[0].AcquirerAmount.Should().BeNull();
    }

    [Fact]
    public void The_Summary_Never_Folds_Unmatched_Money_Into_The_Matched_Total()
    {
        var rows = StatementMatcher.Match(
            new[]
            {
                Entry("ST1", "INV1", 97857),
                Entry("ST2", "INV2", 10000),
                Entry("ST3", "INV-MISSING", 25000)
            },
            new[] { Order("INV1", 978.57m), Order("INV2", 100m) },
            Array.Empty<Payment>());

        var summary = StatementMatcher.Summarise(rows);

        summary.TotalRows.Should().Be(3);
        summary.MatchedCount.Should().Be(2);
        summary.UnmatchedCount.Should().Be(1);
        summary.MatchedTotalUah.Should().Be(1078.57m);
        // 250.00 of money we cannot explain, held separately from the 1078.57 we can.
        summary.UnmatchedTotalUah.Should().Be(250m);
    }

    [Fact]
    public void The_Summary_Reports_The_Signed_Difference_On_Mismatches()
    {
        var rows = StatementMatcher.Match(
            new[] { Entry("ST1", "INV1", 97900) },
            new[] { Order("INV1", 978.57m) },
            Array.Empty<Payment>());

        StatementMatcher.Summarise(rows).MismatchDeltaUah.Should().Be(0.43m);
    }

    [Fact]
    public void An_Empty_Period_Summarises_Without_Throwing()
    {
        var summary = StatementMatcher.Summarise(Array.Empty<StatementMatchRow>());

        summary.TotalRows.Should().Be(0);
        summary.MatchedTotalUah.Should().Be(0m);
    }

    // The refunds we issued in the period get their own figure, beside the matched total and never
    // folded into it. The matched total is about matching, not about net money: a refund issued in
    // the period can reverse a payment from an earlier one, so netting the two would mix the
    // periods in a document someone signs.
    [Fact]
    public void The_Summary_Carries_The_Refunds_We_Issued_Without_Netting_Them_Into_The_Matched_Total()
    {
        var rows = StatementMatcher.Match(
            new[] { Entry("ST1", "INV1", 97857) },
            new[] { Order("INV1", 978.57m) },
            Array.Empty<Payment>());

        var summary = StatementMatcher.Summarise(rows, new[]
        {
            new IssuedRefundRow(
                Guid.NewGuid(), Guid.NewGuid(), "INV1", 478.57m,
                new DateTime(2026, 10, 8, 12, 0, 0, DateTimeKind.Utc), null, false, null)
        });

        summary.IssuedRefundCount.Should().Be(1);
        summary.IssuedRefundTotalUah.Should().Be(478.57m);
        // Gross, exactly as it was: the refund is reported beside it, not inside it.
        summary.MatchedTotalUah.Should().Be(978.57m);
    }
}
