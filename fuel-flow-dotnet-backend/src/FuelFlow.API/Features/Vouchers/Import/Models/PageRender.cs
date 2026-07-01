using SixLabors.ImageSharp;
using UglyToad.PdfPig.Content;

namespace FuelFlow.Features.Vouchers.Import;

public sealed class PageRender
{
    public int PageNumber { get; init; }
    public Image Image { get; init; } = null!;
    public double WidthPoints { get; init; }
    public double HeightPoints { get; init; }
    public IReadOnlyList<Word> Words { get; init; } = null!;
}
