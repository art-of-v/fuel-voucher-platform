using FuelFlow.Features.Vouchers.SharedModels;

namespace FuelFlow.Features.Vouchers.GetUserVouchers;

public sealed record VoucherDto(
    Guid Id,
    string Provider,
    string FuelType,
    decimal Liters,
    decimal Amount,
    DateOnly ExpirationDate,
    string VoucherNumber,
    string ExternalId,
    string QrPayload,
    string QrCodeData,
    string Status,
    string? FuelSubtype,
    string? RedemptionRules,
    string? ImageUrl,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc
);

public sealed record GetUserVouchersResponse(List<VoucherDto> Vouchers);
