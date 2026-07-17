using System.Diagnostics;
using FuelFlow.API.BackgroundJobs;
using FuelFlow.Features.Vouchers;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FuelFlow.Features.Vouchers.Import;

public sealed record ImportVouchersCommand(Stream PdfStream, string FileName);

public sealed record ImportVouchersResponse(
    Guid ImportId,
    int Imported,
    int Duplicates,
    int Failed,
    int VerificationFailed,
    int VerifiedWithWarnings,
    double DurationSeconds
);

public sealed class ImportVouchersCommandHandler
{
    private readonly IImportVouchersDbContext _context;
    private readonly IPdfRenderer _pdfRenderer;
    private readonly IVoucherDetector _voucherDetector;
    private readonly IQrDecoder _qrDecoder;
    private readonly IEnumerable<IVoucherProviderParser> _parsers;
    private readonly ILogger<ImportVouchersCommandHandler> _logger;
    private readonly IBackgroundJobClient _backgroundJobClient;

    public ImportVouchersCommandHandler(
        IImportVouchersDbContext context,
        IPdfRenderer pdfRenderer,
        IVoucherDetector voucherDetector,
        IQrDecoder qrDecoder,
        IEnumerable<IVoucherProviderParser> parsers,
        ILogger<ImportVouchersCommandHandler> logger,
        IBackgroundJobClient backgroundJobClient)
    {
        _context = context;
        _pdfRenderer = pdfRenderer;
        _voucherDetector = voucherDetector;
        _qrDecoder = qrDecoder;
        _parsers = parsers;
        _logger = logger;
        _backgroundJobClient = backgroundJobClient;
    }

    public async Task<ImportVouchersResponse> HandleAsync(ImportVouchersCommand request, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Import Started for file: {FileName}", request.FileName);
        var stopwatch = Stopwatch.StartNew();

        var import = new VoucherImport
        {
            Id = Guid.NewGuid(),
            FileName = request.FileName,
            PageCount = 0,
            StartedAtUtc = DateTime.UtcNow,
            Status = "Started",
            ImportedCount = 0,
            DuplicateCount = 0,
            FailedCount = 0
        };

        _context.VoucherImports.Add(import);
        await _context.SaveChangesAsync(cancellationToken);

        IReadOnlyList<PageRender> pages;
        try
        {
            pages = await _pdfRenderer.RenderPagesAsync(request.PdfStream, cancellationToken);
            import.PageCount = pages.Count;
            _logger.LogInformation("Page Count: {PageCount}", pages.Count);
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to render PDF pages");
            import.Status = "Failed";
            import.CompletedAtUtc = DateTime.UtcNow;
            await _context.SaveChangesAsync(cancellationToken);
            throw;
        }

        var addedVouchersInBatch = new List<FuelVoucher>();

        // Cache QrParameters rows resolved during this import to avoid redundant DB round-trips.
        // Key: "eccLevel|version|maskPattern|encodingMode".
        var qrParamsCache = new Dictionary<string, QrParameters>(StringComparer.OrdinalIgnoreCase);

        int pagesProcessed = 0;
        const int batchSize = 10;

        foreach (var page in pages)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var regions = _voucherDetector.Detect(page);
            _logger.LogInformation("Page {PageNumber}: Voucher Regions Detected: {Count}", page.PageNumber, regions.Count);

            if (regions.Count == 0)
            {
                continue;
            }

            var detectionContext = new ProviderDetectionContext { Words = page.Words };
            var strategy = _parsers.FirstOrDefault(p => p.CanParse(detectionContext));

            if (strategy == null)
            {
                var errMsg = $"No voucher parser found for page {page.PageNumber}.";
                _logger.LogError(errMsg);

                _context.VoucherImportErrors.Add(new VoucherImportError
                {
                    Id = Guid.NewGuid(),
                    ImportId = import.Id,
                    PageNumber = page.PageNumber,
                    ErrorMessage = errMsg,
                    CreatedAtUtc = DateTime.UtcNow
                });
                import.FailedCount += regions.Count;
                continue;
            }

            var parseContext = new ProviderParseContext
            {
                PageRender = page,
                VoucherRegions = regions,
                QrDecoder = _qrDecoder
            };

            IReadOnlyCollection<ParsedVoucher> parsedVouchers;
            try
            {
                parsedVouchers = await strategy.ParseAsync(parseContext, cancellationToken);
            }
            catch (Exception ex)
            {
                var errMsg = $"Failed to parse page {page.PageNumber}: {ex.Message}";
                _logger.LogError(ex, errMsg);

                _context.VoucherImportErrors.Add(new VoucherImportError
                {
                    Id = Guid.NewGuid(),
                    ImportId = import.Id,
                    PageNumber = page.PageNumber,
                    ErrorMessage = errMsg,
                    CreatedAtUtc = DateTime.UtcNow
                });
                import.FailedCount += regions.Count;
                continue;
            }

            foreach (var parsed in parsedVouchers)
            {
                bool isValid = parsed.Confidence >= 80 &&
                               !string.IsNullOrEmpty(parsed.FuelTypeId) &&
                               parsed.Liters > 0 &&
                               parsed.ExpirationDate != default &&
                               !string.IsNullOrEmpty(parsed.VoucherNumber) &&
                               !string.IsNullOrEmpty(parsed.QrPayload);

                if (!isValid)
                {
                    var reason = string.IsNullOrEmpty(parsed.FuelTypeId)
                        ? "Fuel type could not be determined from voucher text or QR code."
                        : $"Confidence: {parsed.Confidence}. FuelTypeId: {parsed.FuelTypeId}, Liters: {parsed.Liters}, Expiry: {parsed.ExpirationDate}, Number: {parsed.VoucherNumber}, QR Payload: {parsed.QrPayload}";
                    var errMsg = $"Voucher failed validation. {reason}";
                    _logger.LogWarning("Page {PageNumber}: {ErrorMessage}", page.PageNumber, errMsg);

                    _context.VoucherImportErrors.Add(new VoucherImportError
                    {
                        Id = Guid.NewGuid(),
                        ImportId = import.Id,
                        PageNumber = page.PageNumber,
                        VoucherNumber = string.IsNullOrEmpty(parsed.VoucherNumber) ? null : parsed.VoucherNumber,
                        ErrorMessage = errMsg,
                        RawText = parsed.RawText,
                        CreatedAtUtc = DateTime.UtcNow
                    });
                    import.FailedCount++;
                    continue;
                }

                // Hard integrity check: the QR payload must contain the voucher number.
                // A mismatch means the QR was decoded from a neighbour's cell (wrong pairing).
                // Reject immediately so the operator can re-import the affected vouchers.
                bool integrityPassed = parsed.QrPayload.Contains(parsed.VoucherNumber);
                if (!integrityPassed)
                {
                    var errMsg = $"QR payload does not contain voucher number '{parsed.VoucherNumber}'. " +
                                 $"Likely decoded from wrong region (payload: '{parsed.QrPayload}'). Re-import required.";
                    _logger.LogWarning("Page {PageNumber}: {ErrorMessage}", page.PageNumber, errMsg);

                    _context.VoucherImportErrors.Add(new VoucherImportError
                    {
                        Id = Guid.NewGuid(),
                        ImportId = import.Id,
                        PageNumber = page.PageNumber,
                        VoucherNumber = parsed.VoucherNumber,
                        ErrorMessage = errMsg,
                        RawText = parsed.RawText,
                        CreatedAtUtc = DateTime.UtcNow
                    });
                    import.FailedCount++;
                    continue;
                }

                bool existsInDb = await _context.FuelVouchers.AnyAsync(
                    v => v.VoucherNumber == parsed.VoucherNumber || v.QrPayload == parsed.QrPayload,
                    cancellationToken);

                bool existsInBatch = addedVouchersInBatch.Any(
                    v => v.VoucherNumber == parsed.VoucherNumber || v.QrPayload == parsed.QrPayload);

                if (existsInDb || existsInBatch)
                {
                    _logger.LogInformation("Voucher Number {VoucherNumber} (or QR payload) already exists. Skipping duplicate.", parsed.VoucherNumber);
                    import.DuplicateCount++;
                    continue;
                }

                // ── QrParameters deduplication ──────────────────────────────────────
                var eccKey = parsed.QrEccLevel ?? string.Empty;
                var cacheKey = $"{eccKey}|{parsed.QrVersion?.ToString() ?? string.Empty}|{parsed.QrMaskPattern?.ToString() ?? string.Empty}|{parsed.QrEncodingMode ?? string.Empty}";

                if (!qrParamsCache.TryGetValue(cacheKey, out var qrParams))
                {
                    var normalizedEcc = string.IsNullOrEmpty(eccKey) ? null : eccKey;
                    qrParams = await _context.QrParameters
                        .FirstOrDefaultAsync(
                            p => p.EccLevel == normalizedEcc &&
                                 p.Version == parsed.QrVersion &&
                                 p.MaskPattern == parsed.QrMaskPattern &&
                                 p.EncodingMode == parsed.QrEncodingMode,
                            cancellationToken);

                    if (qrParams == null)
                    {
                        qrParams = new QrParameters
                        {
                            Id = Guid.NewGuid(),
                            EccLevel = normalizedEcc ?? string.Empty,
                            Version = parsed.QrVersion,
                            MaskPattern = parsed.QrMaskPattern,
                            EncodingMode = parsed.QrEncodingMode,
                            CreatedAtUtc = DateTime.UtcNow
                        };
                        _context.QrParameters.Add(qrParams);
                        _logger.LogInformation(
                            "Created new QrParameters row (ECC={EccLevel}, Version={Version}, Mask={MaskPattern}, Mode={EncodingMode}).",
                            qrParams.EccLevel, qrParams.Version, qrParams.MaskPattern, qrParams.EncodingMode);
                    }

                    qrParamsCache[cacheKey] = qrParams;
                }
                // ────────────────────────────────────────────────────────────────────

                var voucher = new FuelVoucher
                {
                    Id = Guid.NewGuid(),
                    Provider = parsed.Provider,
                    FuelTypeId = parsed.FuelTypeId,
                    Liters = parsed.Liters,
                    ExpirationDate = parsed.ExpirationDate,
                    VoucherNumber = parsed.VoucherNumber,
                    QrPayload = parsed.QrPayload,
                    CreatedAtUtc = DateTime.UtcNow,
                    ImportJobId = import.Id,
                    QrParametersId = qrParams.Id,
                    UpdatedAtUtc = DateTime.UtcNow
                };

                var verification = QrMatrixVerifier.Verify(
                    parsed.QrPayload,
                    parsed.OriginalQrMatrix,
                    parsed.QrEccLevel,
                    parsed.QrVersion,
                    parsed.QrEncodingMode,
                    parsed.QrMaskPattern);

                voucher.Status = verification.Result switch
                {
                    QrMatrixVerifier.VerificationResult.Passed  => SharedModels.VoucherStatus.Imported,
                    QrMatrixVerifier.VerificationResult.Skipped => SharedModels.VoucherStatus.Imported,
                    QrMatrixVerifier.VerificationResult.Warning => SharedModels.VoucherStatus.VerifiedWithWarnings,
                    QrMatrixVerifier.VerificationResult.Failed  => SharedModels.VoucherStatus.VerificationFailed,
                    _ => SharedModels.VoucherStatus.Imported
                };

                if (verification.Result != QrMatrixVerifier.VerificationResult.Skipped)
                {
                    voucher.VerificationMismatchPercent = verification.MismatchPercent;
                    voucher.VerificationMismatchedModules = verification.MismatchedModules;
                    voucher.VerificationTotalModules = verification.TotalModules;
                }

                if (verification.Result == QrMatrixVerifier.VerificationResult.Warning)
                {
                    import.VerifiedWithWarningsCount++;
                    _logger.LogWarning(
                        "QR verification warning for voucher {VoucherNumber}: {Mismatched}/{Total} modules mismatched ({Percent:F2}%)",
                        parsed.VoucherNumber, verification.MismatchedModules, verification.TotalModules, verification.MismatchPercent);
                }
                else if (verification.Result == QrMatrixVerifier.VerificationResult.Failed)
                {
                    import.VerificationFailedCount++;
                    _logger.LogError(
                        "QR verification failed for voucher {VoucherNumber}: {Mismatched}/{Total} modules mismatched ({Percent:F2}%). Reason: {Reason}",
                        parsed.VoucherNumber, verification.MismatchedModules, verification.TotalModules, verification.MismatchPercent, verification.SkipReason);
                }
                else if (verification.Result == QrMatrixVerifier.VerificationResult.Skipped)
                {
                    _logger.LogInformation(
                        "QR verification skipped for voucher {VoucherNumber}: {Reason}",
                        parsed.VoucherNumber, verification.SkipReason);
                }

                _context.FuelVouchers.Add(voucher);
                addedVouchersInBatch.Add(voucher);
                import.ImportedCount++;

                _logger.LogInformation("QR Decode Success: Decoded QR code for Voucher {VoucherNumber}.", parsed.VoucherNumber);
            }

            pagesProcessed++;
            if (pagesProcessed % batchSize == 0)
            {
                await _context.SaveChangesAsync(cancellationToken);
                _logger.LogDebug("Batch checkpoint saved after {PagesProcessed} pages.", pagesProcessed);
            }
        }

        import.Status = "Completed";
        import.CompletedAtUtc = DateTime.UtcNow;
        await _context.SaveChangesAsync(cancellationToken);

        stopwatch.Stop();
        _logger.LogInformation("Import Finished for file {FileName}. Duration: {DurationMs}ms. Imported: {Imported}, Duplicates: {Duplicates}, Failed: {Failed}",
            request.FileName, stopwatch.ElapsedMilliseconds, import.ImportedCount, import.DuplicateCount, import.FailedCount);

        if (import.ImportedCount > 0)
        {
            _backgroundJobClient.Enqueue<FulfillmentService>(
                s => s.ProcessPendingOrdersAsync(CancellationToken.None));
        }

        return new ImportVouchersResponse(
            import.Id,
            import.ImportedCount,
            import.DuplicateCount,
            import.FailedCount,
            import.VerificationFailedCount,
            import.VerifiedWithWarningsCount,
            stopwatch.Elapsed.TotalSeconds
        );
    }
}
