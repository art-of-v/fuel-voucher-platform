namespace FuelFlow.Features.Vouchers.GetVoucherVerification;

public sealed record GetVoucherVerificationResponse(
    Guid VoucherId,
    string VoucherNumber,
    string Status,
    double? MismatchPercent,
    int? MismatchedModules,
    int? TotalModules,
    string QrCodeBase64);
