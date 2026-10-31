using FuelFlow.Features.Vouchers.SharedModels;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Database;
using FuelFlow.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Vouchers.Redemptions.ImportStatement;

public sealed record ImportRedemptionStatementLine(
    string ExternalId,
    string VoucherNumber,
    decimal Liters,
    DateTime? RedeemedAtUtc,
    string? Note);

public sealed record ImportRedemptionStatementCommand(
    Guid UserId,
    IReadOnlyList<ImportRedemptionStatementLine> Lines);

public sealed record ImportedStatementRow(
    string ExternalId,
    RedemptionStatementStatus? Status,
    Guid? VoucherId,
    Guid? RedemptionId,
    string? Message);

public sealed record ImportRedemptionStatementResponse(
    bool Success,
    string? Error,
    int Imported,
    int Duplicates,
    int AwaitingReview,
    int Exceptions,
    IReadOnlyList<ImportedStatementRow> Rows,
    string? ErrorCode = null);

public sealed class ImportRedemptionStatementCommandHandler
{
    private const decimal LitersTolerance = 0.01m;

    private readonly ApplicationDbContext _context;
    private readonly VoucherRedemptionService _redemptions;

    public ImportRedemptionStatementCommandHandler(
        ApplicationDbContext context,
        VoucherRedemptionService redemptions)
    {
        _context = context;
        _redemptions = redemptions;
    }

    public async Task<ImportRedemptionStatementResponse> HandleAsync(
        ImportRedemptionStatementCommand command,
        CancellationToken cancellationToken = default)
    {
        if (command.Lines.Count == 0)
        {
            return Failure("The statement has no lines.", RedemptionRefusalCodes.StatementEmpty);
        }

        var validationError = Validate(command.Lines);
        if (validationError is not null)
        {
            return Failure(validationError.Value.Error, validationError.Value.Code);
        }

        var externalIds = command.Lines
            .Select(l => l.ExternalId.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        var response = await _context.ExecuteReplaySafeAsync<ImportRedemptionStatementResponse>(
            async ct => await ImportAsync(command, ct),
            cancellationToken,
            verifySucceeded: async ct =>
            {
                var stored = await _context.RedemptionStatementRows
                    .AsNoTracking()
                    .CountAsync(r => externalIds.Contains(r.ExternalId), ct);

                return stored == externalIds.Length;
            });

        return response;
    }

    private async Task<ImportRedemptionStatementResponse> ImportAsync(
        ImportRedemptionStatementCommand command,
        CancellationToken cancellationToken)
    {
        var importedAt = SystemClock.UtcNow;
        var seenExternalIds = new HashSet<string>(StringComparer.Ordinal);
        var rows = new List<ImportedStatementRow>(command.Lines.Count);
        var inserted = new List<RedemptionStatementRow>(command.Lines.Count);
        var duplicates = 0;

        foreach (var line in command.Lines)
        {
            var externalId = line.ExternalId.Trim();
            if (!seenExternalIds.Add(externalId))
            {
                duplicates++;
                rows.Add(new ImportedStatementRow(
                    externalId, null, null, null,
                    "Duplicate line id in this statement; the first one was imported."));
                continue;
            }

            if (await _context.RedemptionStatementRows
                    .AsNoTracking()
                    .AnyAsync(r => r.ExternalId == externalId, cancellationToken))
            {
                duplicates++;
                rows.Add(new ImportedStatementRow(
                    externalId, null, null, null,
                    "This line was imported before; nothing changed."));
                continue;
            }

            var voucherNumber = line.VoucherNumber.Trim();
            var voucher = await _context.FuelVouchers
                .AsNoTracking()
                .Where(v => v.VoucherNumber == voucherNumber)
                .Select(v => new { v.Id, v.Status, v.Liters })
                .FirstOrDefaultAsync(cancellationToken);

            var row = new RedemptionStatementRow
            {
                Id = Guid.NewGuid(),
                ExternalId = externalId,
                VoucherNumber = voucherNumber,
                Liters = line.Liters,
                RedeemedAtUtc = NormalizeUtc(line.RedeemedAtUtc),
                VoucherId = voucher?.Id,
                Note = Truncate(line.Note, 500),
                ImportedAtUtc = importedAt,
                ImportedByUserId = command.UserId,
            };

            if (voucher is null)
            {
                row.Status = RedemptionStatementStatus.UnknownVoucher;
                rows.Add(Result(row, "No live voucher carries this number."));
                inserted.Add(row);
                continue;
            }

            switch (voucher.Status)
            {
                case VoucherStatus.Used:
                    var confirmedId = await FindLatestRedemptionIdAsync(
                        voucher.Id, VoucherRedemptionStatus.Confirmed, cancellationToken);
                    row.VoucherRedemptionId = confirmedId;
                    row.Status = RedemptionStatementStatus.AlreadyConfirmed;
                    rows.Add(Result(row, "The voucher is already used; check the litre count by hand."));
                    break;

                case VoucherStatus.PendingReconciliation:
                    var active = await _context.VoucherRedemptions
                        .AsNoTracking()
                        .Where(r => r.VoucherId == voucher.Id && r.Status == VoucherRedemptionStatus.Reported)
                        .Select(r => new { r.Id, r.ReportedLiters })
                        .FirstOrDefaultAsync(cancellationToken);

                    if (active is null)
                    {
                        row.Status = RedemptionStatementStatus.NotAssigned;
                        rows.Add(Result(row, "The voucher is pending but has no live claim."));
                    }
                    else
                    {
                        row.VoucherRedemptionId = active.Id;
                        row.Status = Math.Abs(active.ReportedLiters - line.Liters) <= LitersTolerance
                            ? RedemptionStatementStatus.Matched
                            : RedemptionStatementStatus.Mismatch;
                        rows.Add(Result(
                            row,
                            row.Status == RedemptionStatementStatus.Matched
                                ? "Matched the pending claim."
                                : "The stated litres do not match the pending claim."));
                    }

                    break;

                case VoucherStatus.Assigned:
                    var report = await _redemptions.ReportAsync(
                        voucher.Id,
                        reportedByUserId: null,
                        actorUserId: command.UserId,
                        reportedLiters: line.Liters,
                        redeemedAtUtc: NormalizeUtc(line.RedeemedAtUtc) ?? importedAt,
                        supplierReference: externalId,
                        reason: $"Supplier statement line {externalId}",
                        cancellationToken);

                    switch (report.Outcome)
                    {
                        case RedemptionReportOutcome.Reported:
                            row.Status = Math.Abs(voucher.Liters - line.Liters) <= LitersTolerance
                                ? RedemptionStatementStatus.Matched
                                : RedemptionStatementStatus.Mismatch;
                            row.VoucherRedemptionId = report.RedemptionId;
                            rows.Add(Result(
                                row,
                                row.Status == RedemptionStatementStatus.Matched
                                    ? "Opened a claim from the statement; it awaits review."
                                    : "The statement's litres differ from the voucher; check by hand."));
                            break;

                        case RedemptionReportOutcome.AlreadyPending:
                            var raced = await _context.VoucherRedemptions
                                .AsNoTracking()
                                .Where(r => r.VoucherId == voucher.Id && r.Status == VoucherRedemptionStatus.Reported)
                                .Select(r => (decimal?)r.ReportedLiters)
                                .FirstOrDefaultAsync(cancellationToken);
                            row.VoucherRedemptionId = report.RedemptionId;
                            row.Status = raced is not null
                                && Math.Abs(raced.Value - line.Liters) <= LitersTolerance
                                ? RedemptionStatementStatus.Matched
                                : RedemptionStatementStatus.Mismatch;
                            rows.Add(Result(
                                row,
                                row.Status == RedemptionStatementStatus.Matched
                                    ? "Another request opened an identical claim first."
                                    : "Another request opened a claim with different litres; check by hand."));
                            break;

                        case RedemptionReportOutcome.AlreadyUsed:
                            row.Status = RedemptionStatementStatus.AlreadyConfirmed;
                            row.VoucherRedemptionId = report.RedemptionId;
                            rows.Add(Result(row, "The voucher was confirmed by another request."));
                            break;

                        case RedemptionReportOutcome.InvalidState:
                            row.Status = RedemptionStatementStatus.NotAssigned;
                            rows.Add(Result(row, "The voucher changed state during the import."));
                            break;

                        default:
                            row.Status = RedemptionStatementStatus.UnknownVoucher;
                            rows.Add(Result(row, "The voucher disappeared during the import."));
                            break;
                    }

                    break;

                default:
                    row.Status = RedemptionStatementStatus.NotAssigned;
                    rows.Add(Result(
                        row,
                        $"The voucher is {voucher.Status}; it cannot wait for evidence."));
                    break;
            }

            inserted.Add(row);
        }

        if (inserted.Count > 0)
        {
            _context.RedemptionStatementRows.AddRange(inserted);
            await _context.SaveChangesAsync(cancellationToken);
        }

        var awaitingReview = rows.Count(r => r.Status is { } status && RedemptionStatementStatuses.AwaitingOperator(status));
        var exceptions = rows.Count(r => r.Status is not null and not RedemptionStatementStatus.Matched);

        return new ImportRedemptionStatementResponse(
            true,
            null,
            inserted.Count,
            duplicates,
            awaitingReview,
            exceptions,
            rows);
    }

    private static ImportedStatementRow Result(RedemptionStatementRow row, string message) =>
        new(row.ExternalId, row.Status, row.VoucherId, row.VoucherRedemptionId, message);

    private async Task<Guid?> FindLatestRedemptionIdAsync(
        Guid voucherId,
        VoucherRedemptionStatus status,
        CancellationToken cancellationToken) =>
        await _context.VoucherRedemptions
            .AsNoTracking()
            .Where(r => r.VoucherId == voucherId && r.Status == status)
            .OrderByDescending(r => r.Id)
            .Select(r => (Guid?)r.Id)
            .FirstOrDefaultAsync(cancellationToken);

    private static (string Code, string Error)? Validate(IReadOnlyList<ImportRedemptionStatementLine> lines)
    {
        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line.ExternalId))
            {
                return (
                    RedemptionRefusalCodes.LineIdRequired,
                    "Every statement line needs an external id.");
            }

            if (line.ExternalId.Trim().Length > 100)
            {
                return (
                    RedemptionRefusalCodes.LineIdTooLong,
                    $"Statement line id '{line.ExternalId.Trim()[..20]}...' is longer than 100 characters.");
            }

            if (string.IsNullOrWhiteSpace(line.VoucherNumber))
            {
                return (
                    RedemptionRefusalCodes.LineVoucherRequired,
                    $"Statement line {line.ExternalId.Trim()} has no voucher number.");
            }

            if (line.VoucherNumber.Trim().Length > 100)
            {
                return (
                    RedemptionRefusalCodes.LineVoucherTooLong,
                    $"Statement line {line.ExternalId.Trim()} has a voucher number longer than 100 characters.");
            }

            if (line.Liters <= 0m)
            {
                return (
                    RedemptionRefusalCodes.LineLitresNotPositive,
                    $"Statement line {line.ExternalId.Trim()} must state positive litres.");
            }
        }

        return null;
    }

    private static ImportRedemptionStatementResponse Failure(string error, string code) =>
        new(false, error, 0, 0, 0, 0, [], code);

    private static DateTime? NormalizeUtc(DateTime? value)
    {
        if (value is null)
        {
            return null;
        }

        return value.Value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.Value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value.Value, DateTimeKind.Utc),
        };
    }

    private static string? Truncate(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
    }
}
