using SixLabors.ImageSharp;
using UglyToad.PdfPig.Core;

namespace FuelFlow.Features.Vouchers.Import;

public sealed class VoucherRegion
{
    public Rectangle Bounds { get; init; }
    public PdfRectangle PdfBounds { get; init; }

    public bool Contains(PdfRectangle box)
    {
        var centroid = box.Centroid;
        return centroid.X >= PdfBounds.Left &&
               centroid.X <= PdfBounds.Right &&
               centroid.Y >= PdfBounds.Bottom &&
               centroid.Y <= PdfBounds.Top;
    }
}
