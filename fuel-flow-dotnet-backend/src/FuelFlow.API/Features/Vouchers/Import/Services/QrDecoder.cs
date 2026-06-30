using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using ZXing;

namespace FuelFlow.Features.Vouchers.Import;

public sealed class QrDecoder : IQrDecoder
{
    public string? Decode(Image voucherImage)
    {
        if (voucherImage == null) return null;

        using var imageRgba32 = voucherImage.CloneAs<Rgba32>();

        var reader = new ZXing.ImageSharp.BarcodeReader<Rgba32>
        {
            AutoRotate = true,
            Options = new ZXing.Common.DecodingOptions
            {
                TryHarder = true,
                TryInverted = true,
                PossibleFormats = new[] { BarcodeFormat.QR_CODE }
            }
        };

        var result = reader.Decode(imageRgba32);
        return result?.Text;
    }
}
