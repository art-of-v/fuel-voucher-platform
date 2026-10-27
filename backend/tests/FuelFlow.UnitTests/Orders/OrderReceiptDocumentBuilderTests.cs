using System.Text;
using FluentAssertions;
using FuelFlow.Features.Orders.Receipt;
using FuelFlow.SharedKernel.Pdf;
using SkiaSharp;
using Xunit;

namespace FuelFlow.UnitTests.Orders;

/// <summary>
/// The per-order customer document (#119).
/// </summary>
/// <remarks>
/// The risk this suite exists to retire is silent: a missing glyph does not throw, it renders as
/// nothing. A receipt that quietly drops the product name or the issuer's name still looks like a
/// receipt, so "the PDF generated" is not evidence that it is readable - it has to be checked.
/// </remarks>
public sealed class OrderReceiptDocumentBuilderTests
{
    /// <summary>
    /// The characters the document is built from, paired with their code points.
    /// </summary>
    /// <remarks>
    /// Cyrillic alone is not enough: the Ukrainian letters below are exactly what a Latin-first
    /// typeface drops without complaining, and the hryvnia sign appears on every total.
    /// </remarks>
    private static readonly (char Character, int Codepoint)[] RequiredCharacters =
    [
        ('А', 0x0410),  // Cyrillic A
        ('я', 0x044F),  // Cyrillic ya
        ('і', 0x0456),  // Ukrainian i
        ('ї', 0x0457),  // Ukrainian yi
        ('є', 0x0454),  // Ukrainian ie
        ('ґ', 0x0491),  // Ukrainian g with upturn
        ('₴', 0x20BD),  // hryvnia sign
        ('—', 0x2014)   // em dash
    ];

    private static SKTypeface LoadEmbeddedFont(string fileName)
    {
        var stream = typeof(PdfFonts).Assembly
            .GetManifestResourceStream($"FuelFlow.API.Resources.Fonts.{fileName}");

        stream.Should().NotBeNull($"{fileName} must ship with the assembly");

        return SKTypeface.FromStream(stream!);
    }

    private static OrderReceiptDocument Sample() => new()
    {
        OrderId = Guid.Parse("11111111-2222-3333-4444-555555555555"),
        CreatedAtUtc = new DateTime(2026, 10, 11, 13, 45, 0, DateTimeKind.Utc),
        Status = "FULFILLED",
        Total = 1234.56m,
        AcquirerInvoiceId = "2A3B4C5D",
        AcquirerName = "Monobank",
        AcquirerStatus = "Success",
        CompanyName = "ТОВ «Європейський Паливо»",
        Lines = [new ReceiptLine(2, 24.20m, 1234.56m)]
    };

    [Fact]
    public void Generates_A_Pdf()
    {
        var pdf = OrderReceiptDocumentBuilder.Build(Sample());

        pdf.Should().NotBeEmpty();
        // Every PDF starts with the %PDF- magic; a truncated or empty stream does not.
        Encoding.ASCII.GetString(pdf, 0, 5).Should().Be("%PDF-");
    }

    [Fact]
    public void Generates_A_Pdf_ForARefund()
    {
        var document = Sample() with
        {
            Refund = new RefundReceiptInfo(
                new DateTime(2026, 10, 12, 9, 0, 0, DateTimeKind.Utc),
                1234.56m,
                Guid.Parse("99999999-8888-7777-6666-555555555555"))
        };

        var pdf = OrderReceiptDocumentBuilder.Build(document);

        Encoding.ASCII.GetString(pdf, 0, 5).Should().Be("%PDF-");
    }

    [Fact]
    public void The_document_sells_the_advertising_service_and_names_nothing_else()
    {
        // The QR code is bought under the advertising contract; the fuel is sold and dispensed by the
        // station. A receipt whose line names a fuel describes a sale of fuel, and that is the claim
        // this document must not make. ReceiptLine carries no fuel and no volume, so no line can.
        OrderReceiptDocumentBuilder.ServiceLabel.Should().Be("Рекламні послуги");
    }

    [Theory]
    [InlineData("PT_Sans-Web-Regular.ttf")]
    [InlineData("PT_Sans-Web-Bold.ttf")]
    public void TheEmbeddedFont_CoversEveryCharacterTheDocumentUses(string fileName)
    {
        using var typeface = LoadEmbeddedFont(fileName);
        // Glyph lookup lives on SKFont now; SKTypeface keeps the face, SKFont the shaping side of it.
        using var font = new SKFont(typeface);

        // Read the shipped bytes back through Skia rather than a hand-rolled cmap parser, which
        // would only prove the parser's own arithmetic was right. The native library behind this
        // call is what made the first version of this test pass on a Windows workBrand and fail
        // on the Linux runner, so the test project now carries the native assets for both - a font
        // assertion that only runs on the machine it was written on is not an assertion.
        var missing = RequiredCharacters
            .Where(rc => !font.ContainsGlyph(rc.Codepoint))
            .Select(rc => rc.Character)
            .ToArray();

        missing.Should().BeEmpty(
            "{0} has no glyph for {1}. A missing glyph renders as nothing rather than failing, " +
            "so the receipt would ship with part of its text silently absent.",
            fileName,
            string.Join(", ", missing));
    }

    [Fact]
    public void ACharacterNoReceiptContains_IsReportedMissing()
    {
        // Guards the guard: if ContainsGlyph answered true for everything, the test above would pass
        // for the wrong reason. U+E000 is a private-use codepoint that belongs to no font.
        using var typeface = LoadEmbeddedFont("PT_Sans-Web-Regular.ttf");
        using var font = new SKFont(typeface);

        font.ContainsGlyph(0xE000).Should().BeFalse();
        font.ContainsGlyph(0x0456).Should().BeTrue("і must be present");
    }
}