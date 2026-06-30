using UglyToad.PdfPig.Content;

namespace FuelFlow.Features.Vouchers.Import;

public sealed class ProviderDetectionContext
{
    public IReadOnlyList<Word> Words { get; init; } = null!;
}
