using FuelFlow.SharedKernel.Time;
using Microsoft.Extensions.Logging;

namespace FuelFlow.Features.Vouchers.Import;

public sealed partial class ImportVouchersCommandHandler
{
    private async Task ProcessPdfPagesAsync(ImportRunState state)
    {
        var import = state.Import;
        var cancellationToken = state.CancellationToken;

        int pagesProcessed = 0;
        const int batchSize = 10;

        try
        {
            await foreach (var page in _pdfRenderer.RenderPagesAsync(state.Request.PdfStream, cancellationToken))
            {
                using (page.Image)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    pagesProcessed++;

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
                            Id = Guid.CreateVersion7(),
                            ImportId = import.Id,
                            PageNumber = page.PageNumber,
                            ErrorMessage = errMsg,
                            CreatedAtUtc = SystemClock.UtcNow
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
                            Id = Guid.CreateVersion7(),
                            ImportId = import.Id,
                            PageNumber = page.PageNumber,
                            ErrorMessage = errMsg,
                            CreatedAtUtc = SystemClock.UtcNow
                        });
                        import.FailedCount += regions.Count;
                        continue;
                    }

                    foreach (var parsed in parsedVouchers)
                    {
                        await ProcessParsedVoucherAsync(parsed, page, state);
                    }
                }

                if (pagesProcessed % batchSize == 0)
                {
                    await _context.SaveChangesAsync(cancellationToken);
                    _logger.LogDebug("Batch checkpoint saved after {PagesProcessed} pages.", pagesProcessed);
                }
            }

            import.PageCount = pagesProcessed;
            _logger.LogInformation("Page Count: {PageCount}", pagesProcessed);
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to render or process PDF pages");
            import.PageCount = pagesProcessed;
            import.Status = "Failed";
            import.CompletedAtUtc = SystemClock.UtcNow;
            await _context.SaveChangesAsync(cancellationToken);
            throw;
        }
    }
}
