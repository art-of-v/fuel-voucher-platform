namespace FuelFlow.Features.Vouchers.Import;

public sealed class ProviderParseContext
{
    public PageRender PageRender { get; init; } = null!;
    public IReadOnlyCollection<VoucherRegion> VoucherRegions { get; init; } = null!;
    public IQrDecoder QrDecoder { get; init; } = null!;
}
