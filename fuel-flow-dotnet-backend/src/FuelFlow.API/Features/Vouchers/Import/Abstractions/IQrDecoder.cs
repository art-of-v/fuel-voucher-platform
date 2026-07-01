using SixLabors.ImageSharp;

namespace FuelFlow.Features.Vouchers.Import;

public interface IQrDecoder
{
    string? Decode(Image voucherImage);
}
