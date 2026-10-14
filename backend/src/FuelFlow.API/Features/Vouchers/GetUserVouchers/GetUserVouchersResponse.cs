using FuelFlow.Features.Vouchers.SharedModels;

namespace FuelFlow.Features.Vouchers.GetUserVouchers;

public sealed record VoucherDto(
    Guid Id,
    string Brand,
    string FuelType,
    decimal Liters,
    decimal Amount,
    DateOnly ExpirationDate,
    string VoucherNumber,
    string ExternalId,
    string QrPayload,
    string QrCodeData,
    string Status,
    string Source,
    Guid? LegalEntityId,
    Guid? WorkerUserId,
    string? WorkerFirstName,
    string? WorkerLastName,
    string? FuelSubtype,
    string? RedemptionRules,
    string? ImageUrl,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc,
    string? FuelName = null
)
{
    /// <summary>
    /// Transitional alias for <see cref="Brand"/>, serialised under the field's old name. This is
    /// the shape behind <c>GET /api/vouchers/my</c>, which is what the published 1.0.1 wallet reads;
    /// see <see cref="FuelFlow.SharedKernel.DTOs.VoucherDto.Provider"/> for why both ship for one
    /// release and when this goes.
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("provider")]
    public string Provider => Brand;
}

public sealed record GetUserVouchersResponse(List<VoucherDto> Vouchers);
