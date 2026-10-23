using System.Diagnostics;
using System.Text.Json;
using FuelFlow.API.BackgroundJobs;
using FuelFlow.SharedKernel.Time;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FuelFlow.Features.Vouchers.Import;

public sealed partial class ImportVouchersCommandHandler
{
    private async Task<ImportVouchersResponse> FinalizeAsync(ImportRunState state, Stopwatch stopwatch)
    {
        var import = state.Import;
        var request = state.Request;
        var cancellationToken = state.CancellationToken;

        import.Status = "Completed";
        import.CompletedAtUtc = SystemClock.UtcNow;

        // The summary of the import, written here rather than by the controller that asked for it:
        // the row and the "Completed" status are one fact, and this save commits them together. The
        // controller used to write it after the handler returned, which made it a second, separate
        // write that could fail on its own - an import with no row saying what it brought in
        // (planning #533).
        _auditEvents.RecordEvent(
            "Voucher",
            import.Id.ToString(),
            "VoucherImported",
            null,
            JsonSerializer.Serialize(new
            {
                FileName = request.FileName,
                Imported = import.ImportedCount,
                Duplicates = import.DuplicateCount,
                Failed = import.FailedCount,
                DecodeIntegrityFailed = import.DecodeIntegrityFailedCount,
                DecodeIntegrityWarning = import.DecodeIntegrityWarningCount
            }),
            request.ActingUserId,
            request.ActingUserName,
            $"Imported {import.ImportedCount} vouchers from {request.FileName}",
            "all");

        await _context.SaveChangesAsync(cancellationToken);

        stopwatch.Stop();
        _logger.LogInformation("Import Finished for file {FileName}. Duration: {DurationMs}ms. Imported: {Imported}, Duplicates: {Duplicates}, Failed: {Failed}",
            request.FileName, stopwatch.ElapsedMilliseconds, import.ImportedCount, import.DuplicateCount, import.FailedCount);

        if (import.ImportedCount > 0)
        {
            // Tagged by brand rather than file name: file names are unbounded and
            // would create a new Prometheus time series per upload.
            foreach (var group in state.ImportedVouchersByProvider)
            {
                _metrics.VouchersImported(group.Key, group.Value);
            }

            _backgroundJobClient.Enqueue<FulfillmentService>(
                s => s.ProcessPendingOrdersAsync(CancellationToken.None));
        }

        // Covers parse exceptions, failed validation and failed decode-integrity checks -
        // every one of those paths writes a VoucherImportError row, so reading them
        // back keeps this in step with the rejection logic above automatically.
        // Loaded once: the same rows feed both the staff Telegram sample and the
        // per-row reasons returned to the admin UI (#22). Capped so a pathological
        // all-fail upload can't bloat the response.
        var failedTotal = import.FailedCount + import.DecodeIntegrityFailedCount;
        var errorRows = await _context.VoucherImportErrors
            .Where(e => e.ImportId == import.Id)
            .OrderBy(e => e.PageNumber)
            .ThenBy(e => e.CreatedAtUtc)
            .Select(e => new ImportErrorLine(e.PageNumber, e.VoucherNumber, e.ErrorMessage))
            .Take(200)
            .ToListAsync(cancellationToken);

        if (failedTotal > 0)
        {
            await _notifications.ImportCompletedWithErrorsAsync(
                import.ImportedCount,
                failedTotal,
                errorRows.Take(5).Select(e => e.Reason).ToList(),
                cancellationToken);
        }

        return new ImportVouchersResponse(
            import.Id,
            import.ImportedCount,
            import.DuplicateCount,
            import.FailedCount,
            import.DecodeIntegrityFailedCount,
            import.DecodeIntegrityWarningCount,
            stopwatch.Elapsed.TotalSeconds,
            errorRows
        );
    }
}
