namespace FuelFlow.Features.Vouchers.GetVoucherDecodeIntegrity;

/// <summary>
/// What the importer can prove about a voucher's QR code - and what it cannot.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="DecodeIntegrity"/> is the result of re-encoding the stored payload and comparing the module
/// grid with the image read from the supplier's PDF. A match proves the two agree; it does not prove the
/// voucher was issued by the supplier. <see cref="SupplierProvenance"/> is always <c>Unverified</c>: the
/// supplier is the operator's declaration at upload, and the payload carries no signature, serial or
/// registry entry to check it against.
/// </para>
/// <para>
/// Treat a mismatch or a warning as import QA - the printed matrix and the decoded payload diverged, so the
/// batch needs reconciling with the supplier's delivery note and invoice - never as proof of fraud, and a
/// match never as proof of authenticity.
/// </para>
/// </remarks>
public sealed record GetVoucherDecodeIntegrityResponse(
    Guid VoucherId,
    string VoucherNumber,
    string Status,
    string DecodeIntegrity,
    decimal? MatrixMismatchPercent,
    int? MatrixMismatchedModules,
    int? MatrixTotalModules,
    Guid? SupplierId,
    string? SupplierName,
    string SupplierProvenance,
    string QrCodeBase64);
