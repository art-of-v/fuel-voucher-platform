using System.Runtime.CompilerServices;
using Docnet.Core;
using Docnet.Core.Models;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using UglyToad.PdfPig;

namespace FuelFlow.Features.Vouchers.Import;

public sealed class PdfRenderer : IPdfRenderer
{
    public const int MaxPages = 200;

    /// <summary>
    /// Cap on the longest rendered side, in pixels.
    /// <para>
    /// A PDF MediaBox may legally be up to 14,400 points per side. At 200 DPI that renders to
    /// ~40,000 px, and one 40,000 x 40,000 BGRA page is ~6.4 GB - so the page-count limit above
    /// is no protection at all, because a single page is enough to exhaust the host. Aspect ratio
    /// is preserved when this clamp applies, so QR decoding is unaffected. A real A4 voucher page
    /// renders to ~1,654 x 2,339 and never reaches this bound.
    /// </para>
    /// </summary>
    public const int MaxRenderedSidePixels = 4000;

    private const double TargetDpi = 200.0;
    private const double PdfPointsPerInch = 72.0;
    private const double Scale = TargetDpi / PdfPointsPerInch;

    public async IAsyncEnumerable<PageRender> RenderPagesAsync(Stream pdfStream, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        using var ms = new MemoryStream();
        await pdfStream.CopyToAsync(ms, cancellationToken);
        var bytes = ms.ToArray();

        using var pdfPigDoc = PdfDocument.Open(bytes);
        int pageCount = pdfPigDoc.NumberOfPages;

        if (pageCount == 0) yield break;

        if (pageCount > MaxPages)
        {
            throw new InvalidDataException(
                $"PDF has {pageCount} pages; the maximum allowed is {MaxPages}.");
        }

        var firstPage = pdfPigDoc.GetPage(1);
        var (pageDimOne, pageDimTwo) = ComputeRenderDimensions(firstPage.Width, firstPage.Height);

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

            // The dimensions come back from native code, so treat them as untrusted: a mismatch
            // against the buffer length should be a rejected upload, not an unhandled throw from
            // deep inside ImageSharp.
            if (pw <= 0 || ph <= 0 || (long)pw * ph * 4 != rawBgra.LongLength)
            {
                throw new InvalidDataException(
                    $"Page {i + 1} could not be rendered: unexpected image geometry.");
            }

            var image = Image.LoadPixelData<Bgra32>(rawBgra, pw, ph);

            yield return new PageRender
            {
                PageNumber = i + 1,
                Image = image,
                WidthPoints = pigPage.Width,
                HeightPoints = pigPage.Height,
                Words = words
            };
        }
    }

    /// <summary>
    /// Converts page size in points to the render target in pixels, scaled down proportionally if
    /// it would exceed <see cref="MaxRenderedSidePixels"/>. Returned shorter-side-first because
    /// that is the order <see cref="PageDimensions"/> expects.
    /// </summary>
    internal static (int Shorter, int Longer) ComputeRenderDimensions(double widthPoints, double heightPoints)
    {
        if (widthPoints <= 0 || heightPoints <= 0 || double.IsNaN(widthPoints) || double.IsNaN(heightPoints))
            throw new InvalidDataException("PDF page has unusable dimensions.");

        var targetWidth = widthPoints * Scale;
        var targetHeight = heightPoints * Scale;

        var longest = Math.Max(targetWidth, targetHeight);
        if (longest > MaxRenderedSidePixels)
        {
            var reduction = MaxRenderedSidePixels / longest;
            targetWidth *= reduction;
            targetHeight *= reduction;
        }

        var shorter = Math.Max(1, (int)Math.Round(Math.Min(targetWidth, targetHeight)));
        var longer = Math.Max(1, (int)Math.Round(Math.Max(targetWidth, targetHeight)));

        return (shorter, longer);
    }
}
