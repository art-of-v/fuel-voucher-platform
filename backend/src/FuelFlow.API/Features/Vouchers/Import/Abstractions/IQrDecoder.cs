using SixLabors.ImageSharp;

namespace FuelFlow.Features.Vouchers.Import;

public sealed class QrDecodeResult
{
    /// <summary>Decoded text payload (null if the image contained no readable QR code).</summary>
    public string? Text { get; init; }

    /// <summary>Error correction level of the original QR code: "L", "M", "Q", or "H". Null when decoding failed.</summary>
    public string? EccLevel { get; init; }

    /// <summary>
    /// QR version (1–40) read exactly from the aligned bit-matrix width: (width − 17) / 4.
    /// Falls back to finder-pattern geometry when the bit-matrix path fails.
    /// </summary>
    public int? Version { get; init; }

    /// <summary>
    /// Data-mask reference number (0–7) decoded from the QR format-information strip.
    /// Enforced by <see cref="QrGeneratorV2"/> during regeneration to produce pixel-identical QRs.
    /// </summary>
    public int? MaskPattern { get; init; }

    /// <summary>
    /// Data-encoding mode of the original QR code: "NUMERIC", "ALPHANUMERIC", or "BYTE".
    /// Used by <see cref="QrGeneratorV2"/> to build the correct segment type.
    /// </summary>
    public string? EncodingMode { get; init; }

    /// <summary>
    /// The raw module grid extracted from the original image by ZXing's Detector.
    /// [x, y] == true means a dark module. Null when the BitMatrix path failed.
    /// Used for QR parameter verification during import.
    /// </summary>
    public bool[,]? OriginalMatrix { get; init; }
}

public interface IQrDecoder
{
    QrDecodeResult Decode(Image voucherImage);
}
