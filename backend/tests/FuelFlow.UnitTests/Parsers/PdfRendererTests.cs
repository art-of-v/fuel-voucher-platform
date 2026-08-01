using FluentAssertions;
using FuelFlow.Features.Vouchers.Import;
using UglyToad.PdfPig.Writer;

namespace FuelFlow.UnitTests.Parsers;

public class PdfRendererTests
{
    [Fact]
    public async Task RenderPagesAsync_ShouldReturnPage_ForValidPdf()
    {
        var builder = new PdfDocumentBuilder();
        builder.AddPage(200, 200);
        var pdfBytes = builder.Build();

        var renderer = new PdfRenderer();
        var pages = new List<PageRender>();

        await foreach (var page in renderer.RenderPagesAsync(new MemoryStream(pdfBytes)))
        {
            pages.Add(page);
        }

        pages.Should().HaveCount(1);
        pages[0].PageNumber.Should().Be(1);
        pages[0].Image.Width.Should().BeGreaterThan(0);
        pages[0].Image.Height.Should().BeGreaterThan(0);
        pages[0].WidthPoints.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task RenderPagesAsync_ShouldYieldNothing_ForInvalidStream()
    {
        var renderer = new PdfRenderer();
        var invalidBytes = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };

        var pages = new List<PageRender>();
        Func<Task> act = async () =>
        {
            await foreach (var page in renderer.RenderPagesAsync(new MemoryStream(invalidBytes)))
            {
                pages.Add(page);
            }
        };

        await act.Should().ThrowAsync<Exception>();
    }
}
