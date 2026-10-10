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
    /// The characters the document is built from. Cyrillic alone is not enough: the Ukrainian
    /// letters below are the ones a Latin-first typeface drops without complaint, and the hryvnia
    /// sign appears on every total.
    /// </summary>
    private static readonly int[] RequiredCodepoints =
    [
        0x0410, // А  Cyrillic A
        0x044F, // я  Cyrillic ya
        0x0456, // і  Ukrainian i
        0x0457, // ї  Ukrainian yi
        0x0454, // є  Ukrainian ie
        0x0491, // ґ  Ukrainian g with upturn
        0x20BD, // ₴  hryvnia sign
        0x2014  // —  em dash
    ];

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
        Lines = [new ReceiptLine("okko-dp", 25.5m, 2, 24.20m, 1234.56m)]
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

    [Theory]
    [InlineData("PT_Sans-Web-Regular.ttf")]
    [InlineData("PT_Sans-Web-Bold.ttf")]
    public void TheEmbeddedFont_CoversEveryCharacterTheDocumentUses(string fileName)
    {
        using var stream = typeof(PdfFonts).Assembly
            .GetManifestResourceStream($"FuelFlow.API.Resources.Fonts.{fileName}");
        stream.Should().NotBeNull($"{fileName} must ship with the assembly");

        using var typeface = SKTypeface.FromStream(stream!);

        // Read from the shipped bytes through Skia - the same engine that renders the PDF - rather
        // than a hand-rolled cmap parser, which would only prove that my own arithmetic was right.
        var missing = RequiredCodepoints
            .Where(cp => !typeface.ContainsGlyph(cp))
            .ToArray();

        missing.Should().BeEmpty(
            "{0} has no glyph for U+{1:X4}. A missing glyph renders as nothing rather than " +
            "failing, so the receipt would ship with part of its text silently absent.",
            fileName,
            string.Join(", U+", missing.Select(cp => cp.ToString("X4"))));
    }

    [Fact]
    public void A_TextWithoutThoseGlyphs_IsReportedAsMissing()
    {
        // Guards the guard: if ContainsGlyph answered true for everything, the test above would pass
        // for the wrong reason. Latin codepoints are absent from PT Sans's Cyrillic-only subsets? No -
        // they are present, so use a codepoint no real receipt contains: an unassigned slot.
        using var stream = typeof(PdfFonts).Assembly
            .GetManifestResourceStream("FuelFlow.API.Resources.Fonts.PT_Sans-Web-Regular.ttf");
        using var typeface = SKTypeface.FromStream(stream!);

        typeface.ContainsGlyph(0xE000).Should().BeFalse("a private-use codepoint is in no font");
        typeface.ContainsGlyph(0x0456).Should().BeTrue();
    }
}