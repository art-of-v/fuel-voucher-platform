using System.Diagnostics;
using FuelFlow.Features.Vouchers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FuelFlow.Features.Vouchers.Import;

public sealed record ImportVouchersCommand(Stream PdfStream, string FileName);

public sealed record ImportVouchersResponse(
    Guid ImportId,
    int Imported,
    int Duplicates,
    int Failed,
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

    public ImportVouchersCommandHandler(
        IImportVouchersDbContext context,
        IPdfRenderer pdfRenderer,
        IVoucherDetector voucherDetector,
        IQrDecoder qrDecoder,
        IEnumerable<IVoucherProviderParser> parsers,
        ILogger<ImportVouchersCommandHandler> logger)
    {
        _context = context;
        _pdfRenderer = pdfRenderer;
        _voucherDetector = voucherDetector;
        _qrDecoder = qrDecoder;
        _parsers = parsers;
        _logger = logger;
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
        // Key: "eccLevel|version".
        var qrParamsCache = new Dictionary<string, QrParameters>(StringComparer.OrdinalIgnoreCase);

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

                var importError = new VoucherImportError
                {
                    Id = Guid.NewGuid(),
                    ImportId = import.Id,
                    PageNumber = page.PageNumber,
                    ErrorMessage = errMsg,
                    CreatedAtUtc = DateTime.UtcNow
                };
                _context.VoucherImportErrors.Add(importError);
                import.FailedCount += regions.Count;
                await _context.SaveChangesAsync(cancellationToken);
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

                var importError = new VoucherImportError
                {
                    Id = Guid.NewGuid(),
                    ImportId = import.Id,
                    PageNumber = page.PageNumber,
                    ErrorMessage = errMsg,
                    CreatedAtUtc = DateTime.UtcNow
                };
                _context.VoucherImportErrors.Add(importError);
                import.FailedCount += regions.Count;
                await _context.SaveChangesAsync(cancellationToken);
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
                    var errMsg = $"Voucher failed validation. Confidence: {parsed.Confidence}. FuelTypeId: {parsed.FuelTypeId}, Liters: {parsed.Liters}, Expiry: {parsed.ExpirationDate}, Number: {parsed.VoucherNumber}, QR Payload: {parsed.QrPayload}.";
                    _logger.LogWarning("Page {PageNumber}: {ErrorMessage}", page.PageNumber, errMsg);

                    var importError = new VoucherImportError
                    {
                        Id = Guid.NewGuid(),
                        ImportId = import.Id,
                        PageNumber = page.PageNumber,
                        VoucherNumber = string.IsNullOrEmpty(parsed.VoucherNumber) ? null : parsed.VoucherNumber,
                        ErrorMessage = errMsg,
                        RawText = parsed.RawText,
                        CreatedAtUtc = DateTime.UtcNow
                    };
                    _context.VoucherImportErrors.Add(importError);
                    import.FailedCount++;
                    continue;
                }

                bool integrityPassed = parsed.QrPayload.Contains(parsed.VoucherNumber);
                if (!integrityPassed)
                {
                    _logger.LogWarning("Import Warning: Voucher Number {VoucherNumber} was not found in QrPayload {QrPayload}.", parsed.VoucherNumber, parsed.QrPayload);
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
                // Key: "eccLevel|version|maskPattern|encodingMode" — all four must match
                // to reuse the same row, since different masks produce different QR visuals.
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
                    Status = SharedModels.VoucherStatus.Imported,
                    ImportJobId = import.Id,
                    QrParametersId = qrParams.Id,
                    UpdatedAtUtc = DateTime.UtcNow
                };

                _context.FuelVouchers.Add(voucher);
                addedVouchersInBatch.Add(voucher);
                import.ImportedCount++;

                _logger.LogInformation("QR Decode Success: Decoded QR code for Voucher {VoucherNumber}.", parsed.VoucherNumber);
            }

            await _context.SaveChangesAsync(cancellationToken);
        }

        import.Status = "Completed";
        import.CompletedAtUtc = DateTime.UtcNow;
        await _context.SaveChangesAsync(cancellationToken);

        stopwatch.Stop();
        _logger.LogInformation("Import Finished for file {FileName}. Duration: {DurationMs}ms. Imported: {Imported}, Duplicates: {Duplicates}, Failed: {Failed}",
            request.FileName, stopwatch.ElapsedMilliseconds, import.ImportedCount, import.DuplicateCount, import.FailedCount);

        return new ImportVouchersResponse(
            import.Id,
            import.ImportedCount,
            import.DuplicateCount,
            import.FailedCount,
            stopwatch.Elapsed.TotalSeconds
        );
    }
}
