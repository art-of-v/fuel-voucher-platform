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
    string? ImageUrl,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc,
    string? FuelName = null
);

/// <summary>
/// The caller's wallet, one page of it.
/// </summary>
/// <param name="Vouchers">
/// The page. The list stays a list and not an envelope, because the mobile client reads
/// an array and has for as long as the endpoint has existed - a change of shape would be
/// a client that silently shows nothing.
/// </param>
/// <param name="TotalCount">
/// How many vouchers the caller has in total, across every page. The count is what tells
/// the client there is another page, and it is cheap: it is the same query without the
/// includes and without the QR rendering.
/// </param>
/// <param name="HasNextPage">Whether another page follows this one.</param>
public sealed record GetUserVouchersResponse(
    List<VoucherDto> Vouchers,
    int TotalCount,
    bool HasNextPage);
