using Net.Codecrete.QrCodeGenerator;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using System.Text;

namespace FuelFlow.Features.Vouchers.Import;

/// <summary>
/// QR code generator backed by Net.Codecrete.QrCodeGenerator (v2.1.0+).
/// Unlike the ZXing-based <see cref="QrGenerator"/>, this implementation honours the
/// stored <c>maskPattern</c> (0–7), allowing exact visual reproduction of the original
/// QR code read from the PDF — a requirement for gas-station proprietary scanners.
/// PNG output is produced by the library's built-in <c>ToPngBitmap</c> method; no
/// additional image-processing dependency is required.
/// </summary>
public sealed class QrGeneratorV2 : IQrGenerator
{
    // Quiet zone: 4 modules of white border on every side (ISO/IEC 18004 recommendation).
    private const int Margin = 4;

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

        var ecc = ParseEcc(eccLevel);
        var segments = BuildSegments(payload, encodingMode);

        int minVer = version is >= 1 and <= 40 ? version.Value : QrCode.MinVersion;
        int maxVer = version is >= 1 and <= 40 ? version.Value : QrCode.MaxVersion;
        int mask = maskPattern is >= 0 and <= 7 ? maskPattern.Value : -1;

        // boostEcl: false — we must not upgrade ECC because it would change the visual structure.
        var qr = QrCode.EncodeSegments(segments, ecc, minVer, maxVer, mask, boostEcl: false);

        return RenderToPngBase64(qr, width, height);
    }

    private static List<QrSegment> BuildSegments(string payload, string? encodingMode)
    {
        if (string.Equals(encodingMode, "BYTE", StringComparison.OrdinalIgnoreCase))
        {
            // ISO-8859-1 (Latin-1) matches the original BYTE-mode encoding used by OKKO/WOG.
            var bytes = Encoding.Latin1.GetBytes(payload);
            return new List<QrSegment> { QrSegment.MakeBytes(bytes) };
        }

        if (string.Equals(encodingMode, "NUMERIC", StringComparison.OrdinalIgnoreCase))
            return new List<QrSegment> { QrSegment.MakeNumeric(payload) };

        if (string.Equals(encodingMode, "ALPHANUMERIC", StringComparison.OrdinalIgnoreCase))
            return new List<QrSegment> { QrSegment.MakeAlphanumeric(payload) };

        // No mode stored — let the library auto-select the most compact representation.
        return QrSegment.MakeSegments(payload);
    }

    private static string RenderToPngBase64(QrCode qr, int width, int height)
    {
        // Compute scale so the full QR symbol (data + quiet zone) fits in the canvas.
        int totalModules = qr.Size + Margin * 2;
        int scale = Math.Max(1, Math.Min(width, height) / totalModules);

        // Pixel coordinate of module (0,0) top-left corner.
        // The remaining space after fitting the scaled QR is split evenly on both sides,
        // so the quiet zone is centered rather than pushed to bottom-right.
        int moduleStartX = (width  - totalModules * scale) / 2 + Margin * scale;
        int moduleStartY = (height - totalModules * scale) / 2 + Margin * scale;

        var black = new Rgba32(0, 0, 0, 255);
        var white = new Rgba32(255, 255, 255, 255);

        // Write pixels directly — no intermediate PNG decode and no DrawImage compositing,
        // both of which can introduce 1-pixel off-by-ones.
        using var canvas = new Image<Rgba32>(width, height);
        canvas.ProcessPixelRows(accessor =>
        {
            for (int py = 0; py < height; py++)
            {
                var row = accessor.GetRowSpan(py);
                int moduleY = (py - moduleStartY) / scale;
                bool rowInQr = py >= moduleStartY && moduleY < qr.Size;

                for (int px = 0; px < width; px++)
                {
                    bool dark = false;
                    if (rowInQr && px >= moduleStartX)
                    {
                        int moduleX = (px - moduleStartX) / scale;
                        if (moduleX < qr.Size)
                            dark = qr.GetModule(moduleX, moduleY);
                    }
                    row[px] = dark ? black : white;
                }
            }
        });

        using var ms = new MemoryStream();
        canvas.SaveAsPng(ms);
        return Convert.ToBase64String(ms.ToArray());
    }

    private static QrCode.Ecc ParseEcc(string? level) => level?.ToUpperInvariant() switch
    {
        "M" => QrCode.Ecc.Medium,
        "Q" => QrCode.Ecc.Quartile,
        "H" => QrCode.Ecc.High,
        _ => QrCode.Ecc.Low
    };
}
