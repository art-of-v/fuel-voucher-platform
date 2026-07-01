using Docnet.Core;
using Docnet.Core.Models;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using UglyToad.PdfPig;

namespace FuelFlow.Features.Vouchers.Import;

public sealed class PdfRenderer : IPdfRenderer
{
    private const double TargetDpi = 300.0;
    private const double PdfPointsPerInch = 72.0;
    private const double Scale = TargetDpi / PdfPointsPerInch;

    public async Task<IReadOnlyList<PageRender>> RenderPagesAsync(Stream pdfStream, CancellationToken cancellationToken)
    {
        var result = new List<PageRender>();

        using var ms = new MemoryStream();
        await pdfStream.CopyToAsync(ms, cancellationToken);
        var bytes = ms.ToArray();

        using var pdfPigDoc = PdfDocument.Open(bytes);
        int pageCount = pdfPigDoc.NumberOfPages;

        if (pageCount == 0) return result;

        var firstPage = pdfPigDoc.GetPage(1);
        int targetWidth = (int)Math.Round(firstPage.Width * Scale);
        int targetHeight = (int)Math.Round(firstPage.Height * Scale);
        int pageDimOne = Math.Min(targetWidth, targetHeight);
        int pageDimTwo = Math.Max(targetWidth, targetHeight);

        using var docReader = DocLib.Instance.GetDocReader(bytes, new PageDimensions(pageDimOne, pageDimTwo));

        for (int i = 0; i < pageCount; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var pigPage = pdfPigDoc.GetPage(i + 1);
            var words = pigPage.GetWords().ToList();

            using var pageReader = docReader.GetPageReader(i);
            var rawBgra = pageReader.GetImage();
            int pw = pageReader.GetPageWidth();
            int ph = pageReader.GetPageHeight();

            var image = Image.LoadPixelData<Bgra32>(rawBgra, pw, ph);

            result.Add(new PageRender
            {
                PageNumber = i + 1,
                Image = image,
                WidthPoints = pigPage.Width,
                HeightPoints = pigPage.Height,
                Words = words
            });
        }

        return result;
    }
}
