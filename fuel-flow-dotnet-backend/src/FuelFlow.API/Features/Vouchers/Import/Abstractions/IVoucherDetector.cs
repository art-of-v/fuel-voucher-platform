namespace FuelFlow.Features.Vouchers.Import;

public interface IVoucherDetector
{
    IReadOnlyCollection<VoucherRegion> Detect(PageRender page);
}
