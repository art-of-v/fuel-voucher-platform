using FuelFlow.Features.Vouchers;

namespace FuelFlow.Features.Vouchers.Import;

public sealed class ParsedVoucher
{
    public string Provider { get; set; } = null!;
    public string? FuelTypeId { get; set; }
    public decimal Liters { get; set; }
    public DateOnly ExpirationDate { get; set; }
    public string VoucherNumber { get; set; } = null!;
    public string QrPayload { get; set; } = null!;
    public string? QrEccLevel { get; set; }
    public int? QrVersion { get; set; }
    public int? QrMaskPattern { get; set; }
    public string? QrEncodingMode { get; set; }
    public decimal Confidence { get; set; }
    public string? RawText { get; set; }
}
