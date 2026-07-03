using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using ZXing;
using ZXing.Common;
using ZXing.ImageSharp;
using ZXing.QrCode.Internal;

namespace FuelFlow.Features.Vouchers.Import;

public sealed class QrDecoder : IQrDecoder
{
    // QR format-information mask constant (XOR applied to the 15 raw format bits).
    private const int QrFormatInfoMask = 0x5412;

    public QrDecodeResult Decode(Image voucherImage)
    {
        if (voucherImage == null) return new QrDecodeResult();

        using var imageRgba32 = voucherImage.CloneAs<Rgba32>();

        var reader = new ZXing.ImageSharp.BarcodeReader<Rgba32>
        {
            AutoRotate = true,
            Options = new DecodingOptions
            {
                TryHarder = true,
                TryInverted = true,
                PossibleFormats = new[] { BarcodeFormat.QR_CODE }
            }
        };

        var result = reader.Decode(imageRgba32);
        if (result == null) return new QrDecodeResult();

        string? eccLevel = null;
        if (result.ResultMetadata.TryGetValue(ResultMetadataType.ERROR_CORRECTION_LEVEL, out var eccObj))
            eccLevel = eccObj?.ToString();

        // ── Precise version + mask pattern via BitMatrix ─────────────────────────
        // Run the low-level Detector on the same image to obtain the perspective-
        // corrected bit matrix.  This gives us:
        //   • Version  – exact: (matrixWidth − 17) / 4, no floating-point rounding.
        //   • Mask pattern – the 3-bit value from the QR format-information strip.
        int? version = null;
        int? maskPattern = null;

        try
        {
            var luminance = new ImageSharpLuminanceSource<Rgba32>(imageRgba32);
            var binaryBitmap = new BinaryBitmap(new HybridBinarizer(luminance));
            var matrix = binaryBitmap.BlackMatrix;
            if (matrix != null)
            {
                var detectorResult = new Detector(matrix).detect(null);
                var bits = detectorResult?.Bits;
                if (bits != null)
                {
                    int dim = bits.Width; // 21 + (v-1)*4  →  v = (dim-17)/4
                    int v = (dim - 17) / 4;
                    if (v >= 1 && v <= 40)
                        version = v;

                    maskPattern = TryReadMaskPattern(bits);
                }
            }
        }
        catch
        {
            // BitMatrix path failed – fall back to geometry-based version below.
        }

        // Geometry-based fallback for version when BitMatrix path fails.
        if (version == null)
            version = TryEstimateVersionFromPoints(result.ResultPoints);

        // ── Encoding mode ─────────────────────────────────────────────────────────
        // BYTE_SEGMENTS is only populated by ZXing when the QR contains byte-encoded
        // segments.  Its absence means Numeric or Alphanumeric mode was used.
        string? encodingMode = DetermineEncodingMode(result);

        return new QrDecodeResult
        {
            Text = result.Text,
            EccLevel = eccLevel,
            Version = version,
            MaskPattern = maskPattern,
            EncodingMode = encodingMode
        };
    }

    /// <summary>
    /// Reads both copies of the 15-bit QR format-information strip, XORs each with
    /// the QR format mask, and returns the mask pattern from whichever copy has fewer
    /// bit errors according to the BCH(15,5) error-correction check — exactly as
    /// ZXing's BitMatrixParser.readFormatInformation does internally.
    ///
    /// Copy 1: top-left finder pattern   (row 8 / col 8 area)
    /// Copy 2: bottom-left + top-right   (row dim-8..dim-1 col 8 / row 8 col dim-8..dim-1)
    ///
    /// Reading both copies makes the decoder resilient to compression artefacts or
    /// print damage that may corrupt one copy but not the other.
    /// </summary>
    private static int? TryReadMaskPattern(BitMatrix bits)
    {
        try
        {
            int dim = bits.Width;

            int raw1 = ReadFormatCopy1(bits);
            int raw2 = ReadFormatCopy2(bits, dim);

            int d1 = raw1 ^ QrFormatInfoMask;
            int d2 = raw2 ^ QrFormatInfoMask;

            // Pick the copy with fewer BCH errors (lower Hamming weight of remainder).
            int errors1 = BchErrors(d1);
            int errors2 = BchErrors(d2);

            int demasked = errors1 <= errors2 ? d1 : d2;
            return (demasked >> 10) & 0x7;
        }
        catch
        {
            return null;
        }
    }

    // Copy 1 — around top-left finder pattern.
    // ZXing order: row 8 cols 0-5, (7,8), (8,8), (8,7), col 8 rows 5-0.
    private static int ReadFormatCopy1(BitMatrix bits)
    {
        int raw = 0;
        for (int col = 0; col <= 5; col++)
            raw = (raw << 1) | (bits[col, 8] ? 1 : 0);
        raw = (raw << 1) | (bits[7, 8] ? 1 : 0);
        raw = (raw << 1) | (bits[8, 8] ? 1 : 0);
        raw = (raw << 1) | (bits[8, 7] ? 1 : 0);
        for (int row = 5; row >= 0; row--)
            raw = (raw << 1) | (bits[8, row] ? 1 : 0);
        return raw;
    }

    // Copy 2 — bottom-left finder pattern (col 8, rows dim-1 DOWN to dim-7) +
    //           top-right finder pattern   (row 8, cols dim-8 UP to dim-1).
    // ZXing order: 7 bits descending then 8 bits ascending (matches BitMatrixParser.java).
    private static int ReadFormatCopy2(BitMatrix bits, int dim)
    {
        int raw = 0;
        // 7 bits: col 8, rows dim-1 down to dim-7
        for (int row = dim - 1; row >= dim - 7; row--)
            raw = (raw << 1) | (bits[8, row] ? 1 : 0);
        // 8 bits: row 8, cols dim-8 up to dim-1
        for (int col = dim - 8; col < dim; col++)
            raw = (raw << 1) | (bits[col, 8] ? 1 : 0);
        return raw;
    }

    // Compute the BCH(15,5) syndrome of a demasked 15-bit format word.
    // G(x) = x^10 + x^8 + x^5 + x^4 + x^2 + x + 1 = 0x537.
    // A zero syndrome means a valid codeword (no bit errors).
    // A non-zero syndrome's popcount approximates the number of bit errors.
    // Fewer errors → more reliable copy.
    private static int BchErrors(int demasked)
    {
        const int G = 0x537;
        int rem = demasked;
        for (int i = 14; i >= 10; i--)
        {
            if ((rem & (1 << i)) != 0)
                rem ^= G << (i - 10);
        }
        rem &= 0x3FF;
        int count = 0;
        while (rem != 0) { count += rem & 1; rem >>= 1; }
        return count;
    }

    /// <summary>
    /// Determines the QR encoding mode from ZXing result metadata and payload character set.
    /// </summary>
    private static string? DetermineEncodingMode(Result result)
    {
        if (string.IsNullOrEmpty(result.Text)) return null;

        // BYTE_SEGMENTS is only present when at least one segment used byte encoding.
        if (result.ResultMetadata?.ContainsKey(ResultMetadataType.BYTE_SEGMENTS) == true)
            return "BYTE";

        // Numeric: only digits 0-9.
        if (result.Text.All(c => c >= '0' && c <= '9'))
            return "NUMERIC";

        // Alphanumeric: the 45-character QR alphanumeric set.
        var alphanumSet = new HashSet<char>("0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ $%*+-./:");
        if (result.Text.All(c => alphanumSet.Contains(c)))
            return "ALPHANUMERIC";

        return "BYTE";
    }

    /// <summary>
    /// Geometry-based version fallback: estimates QR version from the three
    /// finder-pattern result points using their EstimatedModuleSize.
    /// Used only when the BitMatrix path is unavailable.
    /// </summary>
    private static int? TryEstimateVersionFromPoints(ResultPoint[]? points)
    {
        if (points == null || points.Length < 3) return null;

        double d01 = Distance(points[0], points[1]);
        double d02 = Distance(points[0], points[2]);
        double d12 = Distance(points[1], points[2]);

        double max = Math.Max(d01, Math.Max(d02, d12));
        double side;
        if (max == d01) side = (d02 + d12) / 2.0;
        else if (max == d02) side = (d01 + d12) / 2.0;
        else side = (d01 + d02) / 2.0;

        if (points[0] is FinderPattern fp0 && points[1] is FinderPattern fp1 && points[2] is FinderPattern fp2)
        {
            double moduleSize = (fp0.EstimatedModuleSize + fp1.EstimatedModuleSize + fp2.EstimatedModuleSize) / 3.0;
            if (moduleSize > 0)
            {
                double v = (side / moduleSize - 10.0) / 4.0;
                int rounded = (int)Math.Round(v);
                if (rounded >= 1 && rounded <= 40)
                    return rounded;
            }
        }

        return null;
    }

    private static double Distance(ResultPoint a, ResultPoint b)
    {
        double dx = a.X - b.X;
        double dy = a.Y - b.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }
}
