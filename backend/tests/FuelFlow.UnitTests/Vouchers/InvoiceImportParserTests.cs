using ClosedXML.Excel;
using FluentAssertions;
using FuelFlow.Features.Vouchers.ParseInvoice;

namespace FuelFlow.UnitTests.Vouchers;

public sealed class InvoiceImportParserTests
{
    private static Stream BuildWorkbook(Action<IXLWorksheet> fill)
    {
        using var wb = new XLWorkbook();
        var ws = wb.AddWorksheet("Накладна");
        fill(ws);
        var ms = new MemoryStream();
        wb.SaveAs(ms);
        ms.Position = 0;
        return ms;
    }

    [Fact]
    public void Parse_HeaderWithPriceColumn_ReadsLines()
    {
        using var stream = BuildWorkbook(ws =>
        {
            ws.Cell("A1").Value = "Код";
            ws.Cell("B1").Value = "Паливо";
            ws.Cell("C1").Value = "Кількість, л";
            ws.Cell("D1").Value = "Ціна за л, грн";
            ws.Cell("E1").Value = "Сума, грн";

            ws.Cell("A2").Value = "okko-dp";
            ws.Cell("B2").Value = "Дизельне паливо";
            ws.Cell("C2").Value = 5000;
            ws.Cell("D2").Value = 48.5;
            ws.Cell("E2").Value = 242500;

            ws.Cell("A3").Value = "okko-a95";
            ws.Cell("B3").Value = "Бензин А-95";
            ws.Cell("C3").Value = 3000;
            ws.Cell("D3").Value = 52.3;
            ws.Cell("E3").Value = 156900;
        });

        var result = InvoiceImportParser.Parse(stream);

        result.Errors.Should().BeEmpty();
        result.Lines.Should().HaveCount(2);

        var dp = result.Lines[0];
        dp.Code.Should().Be("okko-dp");
        dp.Label.Should().Be("Дизельне паливо");
        dp.Liters.Should().Be(5000m);
        dp.CostPerLiter.Should().Be(48.5m);
        dp.Total.Should().Be(242500m);

        result.Lines[1].Code.Should().Be("okko-a95");
        result.Lines[1].CostPerLiter.Should().Be(52.3m);
    }

    [Fact]
    public void Parse_HandlesUkrainianDecimalComma_AndThousandsSpaces()
    {
        using var stream = BuildWorkbook(ws =>
        {
            ws.Cell("A1").Value = "Паливо";
            ws.Cell("B1").Value = "Обсяг, л";
            ws.Cell("C1").Value = "Ціна, грн";
            ws.Cell("D1").Value = "Сума, грн";

            // String cells with comma decimals and space thousands, as a CSV-ish export would carry.
            ws.Cell("A2").Value = "Дизель";
            ws.Cell("B2").Value = "5 000";
            ws.Cell("C2").Value = "48,50";
            ws.Cell("D2").Value = "242 500,00";
        });

        var result = InvoiceImportParser.Parse(stream);

        result.Errors.Should().BeEmpty();
        var line = result.Lines.Should().ContainSingle().Subject;
        line.Liters.Should().Be(5000m);
        line.CostPerLiter.Should().Be(48.50m);
        line.Total.Should().Be(242500.00m);
    }

    [Fact]
    public void Parse_NoPriceColumn_KeepsTotalAndQuantityForDerivation()
    {
        using var stream = BuildWorkbook(ws =>
        {
            ws.Cell("A1").Value = "Номенклатура";
            ws.Cell("B1").Value = "Кількість, л";
            ws.Cell("C1").Value = "Сума, грн";

            ws.Cell("A2").Value = "Дизельне паливо";
            ws.Cell("B2").Value = 1000;
            ws.Cell("C2").Value = 50000;
        });

        var result = InvoiceImportParser.Parse(stream);

        var line = result.Lines.Should().ContainSingle().Subject;
        line.CostPerLiter.Should().BeNull();     // no price column — handler derives 50 from total ÷ qty
        line.Liters.Should().Be(1000m);
        line.Total.Should().Be(50000m);
    }

    [Fact]
    public void Parse_SkipsBlankAndTotalsRows()
    {
        using var stream = BuildWorkbook(ws =>
        {
            ws.Cell("A1").Value = "Паливо";
            ws.Cell("B1").Value = "Кількість, л";
            ws.Cell("C1").Value = "Ціна, грн";

            ws.Cell("A2").Value = "Дизель";
            ws.Cell("B2").Value = 100;
            ws.Cell("C2").Value = 48;

            // row 3 intentionally blank (spacer)
            // row 4: a totals row with no fuel label — only the sum cell
            ws.Cell("C4").Value = 4800;
        });

        var result = InvoiceImportParser.Parse(stream);

        result.Lines.Should().ContainSingle();
        result.Lines[0].Label.Should().Be("Дизель");
    }

    [Fact]
    public void Parse_FindsHeaderBelowPreambleRows()
    {
        using var stream = BuildWorkbook(ws =>
        {
            ws.Cell("A1").Value = "Видаткова накладна № 42";
            ws.Cell("A2").Value = "Постачальник: ТОВ \"ОККО\"";

            ws.Cell("A4").Value = "Паливо";
            ws.Cell("B4").Value = "Кількість, л";
            ws.Cell("C4").Value = "Ціна за літр, грн";

            ws.Cell("A5").Value = "Бензин А-95";
            ws.Cell("B5").Value = 200;
            ws.Cell("C5").Value = 52;
        });

        var result = InvoiceImportParser.Parse(stream);

        var line = result.Lines.Should().ContainSingle().Subject;
        line.Label.Should().Be("Бензин А-95");
        line.CostPerLiter.Should().Be(52m);   // "Ціна за літр" taken as price, not quantity
    }

    [Fact]
    public void Parse_GarbageStream_ReportsFileErrorNoLines()
    {
        using var stream = new MemoryStream("this is not a workbook"u8.ToArray());

        var result = InvoiceImportParser.Parse(stream);

        result.Lines.Should().BeEmpty();
        result.Errors.Should().ContainSingle();
        result.Errors[0].Line.Should().Be(0);
    }

    [Fact]
    public void Parse_NoRecognisableHeader_ReportsError()
    {
        using var stream = BuildWorkbook(ws =>
        {
            ws.Cell("A1").Value = "Foo";
            ws.Cell("B1").Value = "Bar";
            ws.Cell("A2").Value = "x";
            ws.Cell("B2").Value = 1;
        });

        var result = InvoiceImportParser.Parse(stream);

        result.Lines.Should().BeEmpty();
        result.Errors.Should().ContainSingle(e => e.Message.Contains("header"));
    }

    [Theory]
    [InlineData("48,50", 48.50)]
    [InlineData("242 500,00", 242500.00)]
    [InlineData("1 234,56 грн", 1234.56)]
    [InlineData("52.30", 52.30)]
    [InlineData("", null)]
    [InlineData("abc", null)]
    public void ParseNumberString_NormalisesUkrainianFormats(string raw, double? expected)
    {
        var parsed = InvoiceImportParser.ParseNumberString(raw);
        if (expected is null) parsed.Should().BeNull();
        else parsed.Should().Be((decimal)expected.Value);
    }
}
