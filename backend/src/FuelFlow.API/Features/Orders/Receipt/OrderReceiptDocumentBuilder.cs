using System.Globalization;
using FuelFlow.SharedKernel.Pdf;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace FuelFlow.Features.Orders.Receipt;

/// <summary>One line of a receipt, already resolved to what the customer should read.</summary>
/// <param name="FuelName">
/// The fuel the advertised QR is redeemable for - the name from the catalogue the offer came from,
/// never the id. The id is a uuid, and a receipt that printed one would be unreadable. It belongs to
/// the nominal column, not the service column: the fuel is what the code is worth, not what was sold.
/// </param>
public sealed record ReceiptLine(string FuelName, decimal Liters, int Quantity, decimal UnitPrice, decimal LineTotal);

/// <summary>
/// Everything a receipt needs. Resolved by the caller from the order, so this type stays a document
/// concern rather than an EF one - the same document serves the customer's copy and the operator's.
/// </summary>
public sealed record OrderReceiptDocument
{
    public required Guid OrderId { get; init; }
    public required DateTime CreatedAtUtc { get; init; }
    public required string Status { get; init; }
    public required decimal Total { get; init; }
    public required IReadOnlyList<ReceiptLine> Lines { get; init; }

    /// <summary>The acquirer's invoice reference, when the order was paid through one.</summary>
    public string? AcquirerInvoiceId { get; init; }

    public string? AcquirerName { get; init; }
    public string? AcquirerStatus { get; init; }
    public string? CompanyName { get; init; }

    /// <summary>Set on a refund document; null on a receipt for a payment.</summary>
    public RefundReceiptInfo? Refund { get; init; }
}

public sealed record RefundReceiptInfo(DateTime RefundedAtUtc, decimal Amount, Guid OriginalOrderId);

/// <summary>
/// Builds the per-order customer document (#119).
/// </summary>
/// <remarks>
/// <para>
/// Not a fiscal receipt - that is ПРРО and a separate decision (#120). This is the document a customer
/// or an accountant asks for: what was bought, what it cost, and what the acquirer recorded.
/// </para>
/// <para>
/// The sale the document describes is the advertising service, not the fuel. The customer buys a QR
/// code under the advertising contract; the fuel is sold and dispensed by the station, which is why
/// the service column reads <see cref="ServiceLabel"/> and the fuel appears only as the nominal of
/// the code. The Monobank basket line says the same thing, so the acquirer's receipt and this one
/// describe one payment in one vocabulary.
/// </para>
/// <para>
/// Money is formatted through the invariant culture. On a machine whose locale uses a comma as the
/// decimal separator, <c>ToString("N2")</c> would print 1234.50 as "1 234,50" - and worse, a receipt
/// whose numbers disagree with the invoice is the exact problem this document exists to solve.
/// </para>
/// </remarks>
public static class OrderReceiptDocumentBuilder
{
    /// <summary>
    /// What every line of this document sells. One constant, because every sale through the platform
    /// is the same service and the fuel differs only in the nominal.
    /// </summary>
    internal const string ServiceLabel = "Рекламні послуги";

    public static byte[] Build(OrderReceiptDocument document)
    {
        PdfFonts.EnsureRegistered();

        return Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(2, Unit.Centimetre);
                page.DefaultTextStyle(t => t.FontFamily(PdfFonts.Family).FontSize(10));

                page.Header().Element(e => ComposeHeader(e, document));
                page.Content().Element(e => ComposeBody(e, document));
                page.Footer().Element(ComposeFooter);
            });
        }).GeneratePdf();
    }

    private static void ComposeHeader(IContainer container, OrderReceiptDocument d)
    {
        container.Column(column =>
        {
            column.Item().Row(row =>
            {
                row.RelativeItem().Column(left =>
                {
                    left.Item().Text("Квитанція про оплату")
                        .FontFamily(PdfFonts.Family).Bold().FontSize(18).FontColor(Colors.Blue.Darken4);
                    left.Item().Text(d.Refund is null ? "Рахунок" : "Повернення коштів")
                        .FontSize(9).FontColor(Colors.Grey.Medium);
                });
                row.ConstantItem(140).AlignRight().Column(right =>
                {
                    right.Item().Text($"Замовлення").FontSize(9).FontColor(Colors.Grey.Medium);
                    right.Item().Text(ShortId(d.OrderId)).FontFamily(PdfFonts.Family).Bold().FontSize(11);
                    right.Item().Text(FormatDate(d.CreatedAtUtc)).FontSize(9).FontColor(Colors.Grey.Medium);
                    right.Item().Text($"Статус: {d.Status}").FontSize(9).FontColor(Colors.Grey.Medium);
                });
            });

            column.Item().PaddingTop(8).LineHorizontal(1).LineColor(Colors.Grey.Lighten3);

            if (d.CompanyName is not null)
            {
                column.Item().PaddingTop(8).Text($"Компанія: {d.CompanyName}").FontSize(10);
            }
        });
    }

    private static void ComposeBody(IContainer container, OrderReceiptDocument d)
    {
        container.Column(column =>
        {
            column.Spacing(6);

            column.Item().Text("Позиції").FontFamily(PdfFonts.Family).Bold().FontSize(11);
            column.Item().Element(e => e.Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    columns.RelativeColumn(3);   // service
                    columns.RelativeColumn(2);   // nominal
                    columns.RelativeColumn(1);   // qty
                    columns.RelativeColumn(2);   // price
                    columns.RelativeColumn(2);   // total
                });

                table.Cell().Element(HeaderCell).Text("Послуга");
                table.Cell().Element(HeaderCell).Text("Номінал QR");
                table.Cell().Element(HeaderCell).AlignRight().Text("К-сть");
                table.Cell().Element(HeaderCell).AlignRight().Text("Ціна");
                table.Cell().Element(HeaderCell).AlignRight().Text("Сума");

                foreach (var line in d.Lines)
                {
                    table.Cell().PaddingVertical(4).Text(ServiceLabel);
                    table.Cell().PaddingVertical(4).Text(NominalOf(line));
                    table.Cell().PaddingVertical(4).AlignRight().Text(line.Quantity.ToString(CultureInfo.InvariantCulture));
                    table.Cell().PaddingVertical(4).AlignRight().Text(FormatMoney(line.UnitPrice));
                    table.Cell().PaddingVertical(4).AlignRight().Text(FormatMoney(line.LineTotal));
                }
            }));

            column.Item().PaddingTop(2).Text(
                    "Пальне реалізується АЗС. Цей документ підтверджує оплату послуг з розміщення " +
                    "рекламної інформації (QR-код).")
                .FontSize(8).FontColor(Colors.Grey.Medium);

            column.Item().PaddingTop(10).AlignRight().Text("РАЗОМ")
                .FontFamily(PdfFonts.Family).Bold().FontSize(12);
            column.Item().AlignRight().Text($"{FormatMoney(d.Total)} ₴")
                .FontFamily(PdfFonts.Family).Bold().FontSize(14).FontColor(Colors.Blue.Darken4);

            if (d.Refund is { } refund)
            {
                column.Item().PaddingTop(12).Element(e => e.Border(1, Colors.Grey.Lighten2)
                    .Padding(8).Column(inner =>
                    {
                        inner.Item().Text("Повернення").FontFamily(PdfFonts.Family).Bold().FontSize(11);
                        inner.Item().Text($"Сума повернення: {FormatMoney(refund.Amount)} ₴");
                        inner.Item().Text($"Дата повернення: {FormatDate(refund.RefundedAtUtc)}");
                        inner.Item().Text($"Замовлення: {ShortId(refund.OriginalOrderId)}");
                    }));
            }

            if (d.AcquirerInvoiceId is not null)
            {
                column.Item().PaddingTop(12).Element(e => e.Border(1, Colors.Grey.Lighten2)
                    .Padding(8).Column(inner =>
                    {
                        inner.Item().Text("Платіжний провайдер").FontFamily(PdfFonts.Family).Bold().FontSize(11);
                        inner.Item().Text($"{d.AcquirerName ?? "Monobank"} · {d.AcquirerStatus ?? "—"}");
                        inner.Item().Text($"Інвойс: {d.AcquirerInvoiceId}");
                    }));
            }

            column.Item().PaddingTop(14).Text(
                    "Цей документ підтверджує замовлення та оплату. Він не є фіскальним чеком.")
                .FontSize(8).FontColor(Colors.Grey.Medium);
        });
    }

    private static void ComposeFooter(IContainer container)
    {
        // Text() takes a void lambda, so the style is applied to the container before it rather than
        // chained after the call.
        container.AlignCenter().DefaultTextStyle(t => t.FontSize(8).FontColor(Colors.Grey.Medium))
            .Text(x =>
            {
                x.Span("FuelFlow");
                x.Span(" · ");
                x.CurrentPageNumber();
                x.Span(" / ");
                x.TotalPages();
            });
    }

    private static IContainer HeaderCell(IContainer c)
        => c.BorderBottom(1).BorderColor(Colors.Grey.Lighten2).PaddingBottom(4)
            .DefaultTextStyle(t => t.FontFamily(PdfFonts.Family).Bold().FontSize(9));

    /// <summary>
    /// What the QR code is redeemable for, as the nominal column reads it: "А-95 / 10 л".
    /// </summary>
    /// <remarks>
    /// Internal rather than inlined in the table so the test can assert the wording without a PDF
    /// text extractor - the suite reads the shipped font, not the rendered page.
    /// </remarks>
    internal static string NominalOf(ReceiptLine line)
        => $"{line.FuelName} / {FormatNumber(line.Liters)} л";

    /// <summary>
    /// A short, human-quotable order reference. The full GUID would be unusable on a printed receipt
    /// and nobody reads it aloud anyway.
    /// </summary>
    private static string ShortId(Guid id) => id.ToString("N")[..8].ToUpperInvariant();

    private static string FormatDate(DateTime utc)
        => utc.ToString("dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture);

    private static string FormatMoney(decimal amount)
        => amount.ToString("N2", CultureInfo.InvariantCulture);

    private static string FormatNumber(decimal value)
        => value.ToString("0.##", CultureInfo.InvariantCulture);
}