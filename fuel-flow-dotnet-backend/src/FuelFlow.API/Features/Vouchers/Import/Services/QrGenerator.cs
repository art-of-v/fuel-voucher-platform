using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using ZXing;
using ZXing.ImageSharp;
using ZXing.QrCode;
using ZXing.QrCode.Internal;

namespace FuelFlow.Features.Vouchers.Import;

public sealed class QrGenerator : IQrGenerator
{
    public string GenerateQrCode(
        string payload,
        int width = 300,
        int height = 300,
        string? eccLevel = null,
        int? version = null,
        string? encodingMode = null,
        int? maskPattern = null)
    {
        if (string.IsNullOrWhiteSpace(payload))
            return string.Empty;

        var options = new QrCodeEncodingOptions
        {
            Width = width,
            Height = height,
            Margin = 4
        };

        var ecc = ParseEccLevel(eccLevel) ?? ErrorCorrectionLevel.L;
        options.Hints[EncodeHintType.ERROR_CORRECTION] = ecc;

        if (version is >= 1 and <= 40)
            options.Hints[EncodeHintType.QR_VERSION] = version.Value;

        // When the original QR used byte encoding, force byte mode so ZXing does not
        // silently upgrade to a more compact mode and change the QR structure.
        // DISABLE_ECI prevents ZXing from prepending an ECI designator, which would
        // alter the payload byte stream visible to the scanner.
        if (string.Equals(encodingMode, "BYTE", StringComparison.OrdinalIgnoreCase))
        {
            options.Hints[EncodeHintType.CHARACTER_SET] = "ISO-8859-1";
            options.Hints[EncodeHintType.DISABLE_ECI] = true;
        }

        var writer = new BarcodeWriterPixelData
        {
            Format = BarcodeFormat.QR_CODE,
            Options = options
        };

        var pixelData = writer.Write(payload);
        using var image = Image.LoadPixelData<Bgra32>(pixelData.Pixels, pixelData.Width, pixelData.Height);

        using var ms = new MemoryStream();
        image.SaveAsPng(ms);
        return Convert.ToBase64String(ms.ToArray());
    }

    private static ErrorCorrectionLevel? ParseEccLevel(string? level) => level?.ToUpperInvariant() switch
    {
        "L" => ErrorCorrectionLevel.L,
        "M" => ErrorCorrectionLevel.M,
        "Q" => ErrorCorrectionLevel.Q,
        "H" => ErrorCorrectionLevel.H,
        _ => null
    };
}

public interface IQrGenerator
{
    string GenerateQrCode(
        string payload,
        int width = 300,
        int height = 300,
        string? eccLevel = null,
        int? version = null,
        string? encodingMode = null,
        int? maskPattern = null);
}
