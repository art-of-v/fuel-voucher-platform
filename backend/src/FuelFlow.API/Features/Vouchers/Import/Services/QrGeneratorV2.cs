using Net.Codecrete.QrCodeGenerator;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using System.Text;

namespace FuelFlow.Features.Vouchers.Import;

/// <summary>
/// QR code generator backed by Net.Codecrete.QrCodeGenerator (v2.1.0+).
/// This implementation honours the stored <c>maskPattern</c> (0–7), allowing exact
/// visual reproduction of the original QR code read from the PDF — a requirement
/// for gas-station proprietary scanners.
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

        var qr = BuildQrCode(payload, eccLevel, version, encodingMode, maskPattern);
        return RenderToPngBase64(qr, width, height);
    }

    internal static QrCode BuildQrCode(
        string payload,
        string? eccLevel = null,
        int? version = null,
        string? encodingMode = null,
        int? maskPattern = null)
    {
        var ecc = ParseEcc(eccLevel);
        var segments = BuildSegments(payload, encodingMode);

        int minVer = version is >= 1 and <= 40 ? version.Value : QrCode.MinVersion;
        int maxVer = version is >= 1 and <= 40 ? version.Value : QrCode.MaxVersion;
        int mask = maskPattern is >= 0 and <= 7 ? maskPattern.Value : -1;

        return QrCode.EncodeSegments(segments, ecc, minVer, maxVer, mask, boostEcl: false);
    }

    private static List<QrSegment> BuildSegments(string payload, string? encodingMode)
    {
        // Use the stored encoding mode - WOG vouchers use BYTE mode for all digits
        if (string.IsNullOrEmpty(encodingMode))
        {
            // No mode stored - use BYTE mode (matches the WOG PDF generator)
            var bytes = Encoding.Latin1.GetBytes(payload);
            return new List<QrSegment> { QrSegment.MakeBytes(bytes) };
        }

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

        // Unknown mode - fallback to BYTE mode (WOG PDF generator uses BYTE mode for digits)
        var fallbackBytes = Encoding.Latin1.GetBytes(payload);
        return new List<QrSegment> { QrSegment.MakeBytes(fallbackBytes) };
    }

    private static string RenderToPngBase64(QrCode qr, int width, int height)
    {
        // Compute scale so the full QR symbol (data + quiet zone) fits in the canvas.
        int totalModules = qr.Size + Margin * 2;
        
        // Use ceiling for scaling to prevent truncation off-by-one errors
        double scaleFactor = (double)Math.Max(1, Math.Min(width, height)) / totalModules;
        int scale = (int)Math.Ceiling(scaleFactor);
        
        // Calculate effective dimensions with proper scaling
        int scaledWidth = totalModules * scale;
        int scaledHeight = totalModules * scale;

        // Pixel coordinate of module (0,0) top-left corner.
        // Use integer division that's less prone to floating-point precision issues
        int moduleStartX = ((width - scaledWidth) + 1) / 2 + Margin * scale;
        int moduleStartY = ((height - scaledHeight) + 1) / 2 + Margin * scale;

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
