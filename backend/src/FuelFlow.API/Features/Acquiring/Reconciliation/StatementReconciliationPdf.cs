using System.Globalization;
using FuelFlow.SharedKernel.Pdf;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace FuelFlow.Features.Acquiring.Reconciliation;

/// <summary>
/// Renders a reconciliation act as a PDF - the form it is actually signed off in.
/// </summary>
/// <remarks>
/// Reuses the PT Sans setup and the licence declaration from #119 rather than declaring its own:
/// two entry points into PDF generation, each setting the licence and the fonts, is two places for
/// them to drift apart.
/// </remarks>
public static class StatementReconciliationPdf
{
    public static byte[] Build(StatementReconciliationResponse act)
    {
        // Declares the QuestPDF licence and registers the fonts. Nothing in this method works
        // without it, and it is the one call that guarantees it: a licence set only in service
        // registration is absent for a background job or a test, and the failure lands on the first
        // page rather than at startup.
        PdfFonts.EnsureRegistered();

        return Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4.Landscape());
                page.Margin(1.5f, Unit.Centimetre);
                page.DefaultTextStyle(t => t.FontFamily(PdfFonts.Family).FontSize(9f));

                page.Header().Element(e => ComposeHeader(e, act));
                page.Content().Element(e => ComposeBody(e, act));
                page.Footer().Element(ComposeFooter);
            });
        }).GeneratePdf();
    }

    private static void ComposeHeader(IContainer container, StatementReconciliationResponse act)
    {
        container.Column(column =>
        {
            column.Item().Text("Акт звірки з еквайром")
                .FontFamily(PdfFonts.Family).Bold().FontSize(16).FontColor(Colors.Blue.Darken4);
            column.Item().Text(
                $"{act.Merchant} · {act.FromUtc:yyyy-MM-dd} — {act.ToUtc:yyyy-MM-dd} (UTC)")
                .FontSize(10).FontColor(Colors.Grey.Darken1);
            column.Item().PaddingTop(6).LineHorizontal(1).LineColor(Colors.Grey.Lighten3);
        });
    }

    private static void ComposeBody(IContainer container, StatementReconciliationResponse act)
    {
        container.Column(column =>
        {
            column.Spacing(8);

            column.Item().Element(e => ComposeSummary(e, act));
            column.Item().Element(e => ComposeTable(e, act));

            if (act.RefundsIssued.Count > 0)
            {
                column.Item().PaddingTop(12).Text("Повернення за період")
                    .FontFamily(PdfFonts.Family).Bold().FontSize(10);
                column.Item().Element(e => ComposeRefunds(e, act));
            }

            if (act.MissingFromStatement.Count > 0)
            {
                column.Item().PaddingTop(12).Text("Замовлення, яких немає у реєстрі acquirer'а")
                    .FontFamily(PdfFonts.Family).Bold().FontSize(10);
                column.Item().Element(e => ComposeMissing(e, act));
            }

            column.Item().PaddingTop(14).Text(
                    "Акт звіряє два незалежні джерела: записи acquirer'а та власні замовлення. " +
                    "Рядки без збігу показано окремо і не зведено до підсумку. Повернення, які ми " +
                    "видали за період, показані окремо і не вираховані з узгодженої суми: acquirer " +
                    "не виносить скасування окремим рядком, тому звіряти їх доводиться з нашого боку.")
                .FontSize(8).FontColor(Colors.Grey.Medium);
        });
    }

    private static void ComposeSummary(IContainer container, StatementReconciliationResponse act)
    {
        var s = act.Summary;

        container.Table(table =>
        {
            table.ColumnsDefinition(columns =>
            {
                for (var i = 0; i < 6; i++)
                {
                    columns.RelativeColumn();
                }
            });

            table.Cell().Element(SummaryCell).Column(inner =>
            {
                inner.Item().Text("Збігів").FontSize(8).FontColor(Colors.Grey.Darken1);
                inner.Item().Text(M(s.MatchedCount)).Bold().FontSize(13);
            });
            table.Cell().Element(SummaryCell).Column(inner =>
            {
                inner.Item().Text("Розбіжностей").FontSize(8).FontColor(Colors.Grey.Darken1);
                inner.Item().Text(M(s.MismatchCount)).Bold().FontSize(13);
            });
            table.Cell().Element(SummaryCell).Column(inner =>
            {
                inner.Item().Text("Без збігу").FontSize(8).FontColor(Colors.Grey.Darken1);
                inner.Item().Text(M(s.UnmatchedCount)).Bold().FontSize(13);
            });
            table.Cell().Element(SummaryCell).Column(inner =>
            {
                inner.Item().Text("Узгоджено, ₴").FontSize(8).FontColor(Colors.Grey.Darken1);
                inner.Item().Text(Money(s.MatchedTotalUah)).Bold().FontSize(13);
            });
            table.Cell().Element(SummaryCell).Column(inner =>
            {
                inner.Item().Text("Без збігу, ₴").FontSize(8).FontColor(Colors.Grey.Darken1);
                inner.Item().Text(Money(s.UnmatchedTotalUah)).Bold().FontSize(13);
            });
            // The refunds we issued in the period, beside the matched total and never folded into it:
            // the acquirer files a reversal nested under the payment it reverses, so the statement
            // alone cannot show that money going back - and netting the two figures would mix the
            // periods in a document someone signs.
            table.Cell().Element(SummaryCell).Column(inner =>
            {
                inner.Item().Text("Повернень за період, ₴").FontSize(8).FontColor(Colors.Grey.Darken1);
                inner.Item().Text(Money(s.IssuedRefundTotalUah)).Bold().FontSize(13);
            });
        });
    }

    /// <summary>
    /// The refunds we issued in the period: what went back, against the payment it went back
    /// against.
    /// </summary>
    private static void ComposeRefunds(IContainer container, StatementReconciliationResponse act)
    {
        container.Table(table =>
        {
            table.ColumnsDefinition(columns =>
            {
                columns.ConstantColumn(160);   // invoice
                columns.ConstantColumn(90);    // amount
                columns.ConstantColumn(120);   // issued at
                columns.RelativeColumn();      // note
            });

            table.Header(header =>
            {
                header.Cell().Element(HeaderCell).Text("Рахунок");
                header.Cell().Element(HeaderCell).AlignRight().Text("Сума");
                header.Cell().Element(HeaderCell).Text("Дата повернення");
                header.Cell().Element(HeaderCell).Text("Примітка");
            });

            foreach (var refund in act.RefundsIssued)
            {
                table.Cell().Element(BodyCell).Text(refund.InvoiceId ?? "-");
                table.Cell().Element(BodyCell).AlignRight().Text(Money(refund.Amount));
                table.Cell().Element(BodyCell).Text(refund.IssuedAtUtc.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
                table.Cell().Element(BodyCell).Text(refund.Note ?? string.Empty);
            }
        });
    }

    private static void ComposeTable(IContainer container, StatementReconciliationResponse act)
    {
        container.Table(table =>
        {
            table.ColumnsDefinition(columns =>
            {
                columns.ConstantColumn(110);  // statement id
                columns.ConstantColumn(80);   // status
                columns.ConstantColumn(110);  // invoice
                columns.ConstantColumn(90);   // destination
                columns.ConstantColumn(70);   // acquirer amount
                columns.ConstantColumn(70);   // local amount
                columns.RelativeColumn();     // note
            });

            table.Header(header =>
            {
                header.Cell().Element(HeaderCell).Text("Statement ID");
                header.Cell().Element(HeaderCell).Text("Статус");
                header.Cell().Element(HeaderCell).Text("Рахунок");
                header.Cell().Element(HeaderCell).Text("Призначення");
                header.Cell().Element(HeaderCell).AlignRight().Text("Acquirer");
                header.Cell().Element(HeaderCell).AlignRight().Text("В нас");
                header.Cell().Element(HeaderCell).Text("Примітка");
            });

            foreach (var row in act.Rows)
            {
                table.Cell().Element(BodyCell).Text(row.StatementId);
                table.Cell().Element(BodyCell).Text(StatusLabel(row.Status));
                table.Cell().Element(BodyCell).Text(row.InvoiceId ?? "-");
                // The payment purpose the acquirer shows on the receipt: evidence for a dispute,
                // never a match key.
                table.Cell().Element(BodyCell).Text(row.Destination ?? "-");
                table.Cell().Element(BodyCell).AlignRight().Text(Money(row.AcquirerAmount));
                table.Cell().Element(BodyCell).AlignRight().Text(Money(row.LocalAmount));
                table.Cell().Element(BodyCell).Text(row.Note ?? string.Empty);
            }
        });
    }

    private static void ComposeMissing(IContainer container, StatementReconciliationResponse act)
    {
        container.Table(table =>
        {
            table.ColumnsDefinition(columns =>
            {
                columns.ConstantColumn(160);
                columns.RelativeColumn();
                columns.RelativeColumn();
            });

            foreach (var missing in act.MissingFromStatement)
            {
                table.Cell().Element(BodyCell).Text(missing.InvoiceId);
                table.Cell().Element(BodyCell).AlignRight().Text(Money(missing.Amount));
                table.Cell().Element(BodyCell).Text(missing.Status.ToString());
            }
        });
    }

    private static IContainer HeaderCell(IContainer c) => c
        .Background(Colors.Grey.Lighten3)
        .BorderBottom(1).BorderColor(Colors.Grey.Lighten2)
        .PaddingVertical(4).PaddingHorizontal(4);

    private static IContainer BodyCell(IContainer c) => c.PaddingVertical(3).PaddingHorizontal(4);

    private static IContainer SummaryCell(IContainer c) => c
        .PaddingVertical(6).PaddingHorizontal(8)
        .Background(Colors.Grey.Lighten4)
        .Border(1).BorderColor(Colors.Grey.Lighten3);

    /// <summary>
    /// A body cell with a row tint.
    /// </summary>
    /// <remarks>
    /// The return type is spelled out because QuestPDF has two <c>Element</c> overloads: one taking
    /// an <c>Action</c> and returning void, one taking a <c>Func</c> and returning the container so
    /// it stays chainable. An un-annotated lambda here binds to the Action one and
    /// <c>.Element(...).Text(...)</c> stops compiling.
    /// </remarks>
    private static void ComposeFooter(IContainer container)
    {
        // Text() with a lambda returns void, so the style is set on the container beforehand
        // rather than chained after the call.
        container
            .DefaultTextStyle(t => t.FontSize(8f).FontColor(Colors.Grey.Medium))
            .AlignRight()
            .Text(text =>
            {
                text.Span("Сторінка ");
                text.CurrentPageNumber();
                text.Span(" з ");
                text.TotalPages();
            });
    }

    private static string StatusLabel(StatementMatchStatus status) => status switch
    {
        StatementMatchStatus.Matched => "збіг",
        StatementMatchStatus.AmountMismatch => "розбіжність",
        StatementMatchStatus.Unmatched => "без збігу",
        StatementMatchStatus.RefundMatch => "повернення",
        _ => status.ToString()
    };

    private static string M(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static string Money(decimal? value)
        => value is null ? "-" : value.Value.ToString("0.00", CultureInfo.InvariantCulture);
}