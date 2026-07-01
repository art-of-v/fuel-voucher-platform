using SixLabors.ImageSharp;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;

namespace FuelFlow.Features.Vouchers.Import;

public sealed class VoucherDetector : IVoucherDetector
{
    private const double ProximityThresholdPoints = 20.0;
    private const int PixelPadding = 30;

    public IReadOnlyCollection<VoucherRegion> Detect(PageRender page)
    {
        if (page.Words == null || page.Words.Count == 0)
            return Array.Empty<VoucherRegion>();

        var scaleX = page.Image.Width / page.WidthPoints;
        var scaleY = page.Image.Height / page.HeightPoints;

        var clusters = new List<List<Word>>();

        foreach (var word in page.Words)
        {
            var matchingClusters = new List<List<Word>>();
            foreach (var cluster in clusters)
            {
                if (cluster.Any(w => IsClose(word, w, ProximityThresholdPoints)))
                    matchingClusters.Add(cluster);
            }

            if (matchingClusters.Count == 0)
            {
                clusters.Add(new List<Word> { word });
            }
            else
            {
                var mainCluster = matchingClusters[0];
                mainCluster.Add(word);
                for (int idx = 1; idx < matchingClusters.Count; idx++)
                {
                    mainCluster.AddRange(matchingClusters[idx]);
                    clusters.Remove(matchingClusters[idx]);
                }
            }
        }

        var validClusters = clusters.Where(c => c.Count >= 5).ToList();
        var regions = new List<VoucherRegion>();

        foreach (var cluster in validClusters)
        {
            var minX = cluster.Min(w => w.BoundingBox.Left);
            var maxX = cluster.Max(w => w.BoundingBox.Right);
            var minY = cluster.Min(w => w.BoundingBox.Bottom);
            var maxY = cluster.Max(w => w.BoundingBox.Top);

            var pdfBounds = new PdfRectangle(minX, minY, maxX, maxY);

            var pixelLeft = (int)Math.Floor(minX * scaleX);
            var pixelRight = (int)Math.Ceiling(maxX * scaleX);
            var pixelTop = (int)Math.Floor((page.HeightPoints - maxY) * scaleY);
            var pixelBottom = (int)Math.Ceiling((page.HeightPoints - minY) * scaleY);

            var pixelWidth = pixelRight - pixelLeft;
            var pixelHeight = pixelBottom - pixelTop;

            var cropLeft = Math.Max(0, pixelLeft - PixelPadding);
            var cropTop = Math.Max(0, pixelTop - PixelPadding);
            var cropWidth = Math.Min(page.Image.Width - cropLeft, pixelWidth + 2 * PixelPadding);
            var cropHeight = Math.Min(page.Image.Height - cropTop, pixelHeight + 2 * PixelPadding);

            regions.Add(new VoucherRegion
            {
                Bounds = new Rectangle(cropLeft, cropTop, cropWidth, cropHeight),
                PdfBounds = pdfBounds
            });
        }

        regions.Sort((a, b) =>
        {
            var avgHeight = (a.Bounds.Height + b.Bounds.Height) / 2.0;
            if (Math.Abs(a.Bounds.Y - b.Bounds.Y) < avgHeight * 0.3)
                return a.Bounds.X.CompareTo(b.Bounds.X);
            return a.Bounds.Y.CompareTo(b.Bounds.Y);
        });

        return regions;
    }

    private static bool IsClose(Word a, Word b, double threshold)
    {
        double dx = Math.Max(0, Math.Max(a.BoundingBox.Left - b.BoundingBox.Right, b.BoundingBox.Left - a.BoundingBox.Right));
        double dy = Math.Max(0, Math.Max(a.BoundingBox.Bottom - b.BoundingBox.Top, b.BoundingBox.Bottom - a.BoundingBox.Top));
        return dx < threshold && dy < threshold;
    }
}
