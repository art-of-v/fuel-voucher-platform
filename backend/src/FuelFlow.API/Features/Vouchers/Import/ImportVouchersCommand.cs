namespace FuelFlow.Features.Vouchers.Import;

/// <summary>
/// Import one supplier PDF. <paramref name="SupplierId"/> is required: a PDF is one supplier's delivery,
/// and the vouchers it creates have to be settled with that supplier — the brand printed on them cannot
/// tell us who issued them.
/// </summary>
/// <param name="ActingUserId">
/// Who ran the import. It was not passed before D-13, and the omission was invisible while the only
/// record of an import was the file-level <c>voucher_imports</c> row - which is not the question the
/// ledger answers. A deposit that cannot say who took delivery of the paper cannot be the start of a
/// voucher's life story, and an import is the start of most of them.
/// </param>
/// <param name="ActingUserName">
/// Their name, for the audit row that says what the import brought in. Nullable: a machine-driven
/// import has no name to give.
/// </param>
public sealed record ImportVouchersCommand(
    Stream PdfStream, string FileName, Guid SupplierId, Guid ActingUserId, string? ActingUserName = null);

// One rejected row, surfaced inline in the import result so staff see WHY a row failed
// (not just a count). Reason is the same human-readable message persisted to
// voucher_import_errors; a blank reason is rendered by the admin as "unknown, escalate".
public sealed record ImportErrorLine(
    int PageNumber,
    string? VoucherNumber,
    string Reason
);

public sealed record ImportVouchersResponse(
    Guid ImportId,
    int Imported,
    int Duplicates,
    int Failed,
    int DecodeIntegrityFailed,
    int DecodeIntegrityWarning,
    double DurationSeconds,
    IReadOnlyList<ImportErrorLine>? Errors = null
);
