namespace FuelFlow.Features.Vouchers;

public sealed class QrParameters
{
    public Guid Id { get; set; }

    /// <summary>QR code version (1–40) read from the aligned bit-matrix width. Controls grid density.</summary>
    public int? Version { get; set; }

    /// <summary>Error correction level extracted from the original QR code: "L", "M", "Q", or "H".</summary>
    public string EccLevel { get; set; } = null!;

    /// <summary>
    /// Data-mask reference number (0–7) read from the QR format-information strip.
    /// </summary>
    public int? MaskPattern { get; set; }

    /// <summary>
    /// Data-encoding mode of the original QR: "NUMERIC", "ALPHANUMERIC", or "BYTE".
    /// Used as a hint when regenerating the QR code.
    /// </summary>
    public string? EncodingMode { get; set; }

    public DateTime CreatedAtUtc { get; set; }
}
