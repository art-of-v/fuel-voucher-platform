using System.Diagnostics;
using FuelFlow.Features.Vouchers;
using FuelFlow.SharedKernel.Observability;
using FuelFlow.SharedKernel.Time;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FuelFlow.Features.Vouchers.Import;

public sealed partial class ImportVouchersCommandHandler
{
    private readonly IImportVouchersDbContext _context;
    private readonly IPdfRenderer _pdfRenderer;
    private readonly IVoucherDetector _voucherDetector;
    private readonly IQrDecoder _qrDecoder;
    private readonly IEnumerable<IVoucherProviderParser> _parsers;
    private readonly ILogger<ImportVouchersCommandHandler> _logger;
    private readonly IBackgroundJobClient _backgroundJobClient;
    private readonly FuelFlowMetrics _metrics;
    private readonly FuelFlow.Features.Vouchers.Security.IQrCipher _qrCipher;
    private readonly NotificationDispatcher _notifications;
    private readonly FuelFlow.Features.Brands.AuditEventService _auditEvents;

    public ImportVouchersCommandHandler(
        IImportVouchersDbContext context,
        IPdfRenderer pdfRenderer,
        IVoucherDetector voucherDetector,
        IQrDecoder qrDecoder,
        IEnumerable<IVoucherProviderParser> parsers,
        ILogger<ImportVouchersCommandHandler> logger,
        IBackgroundJobClient backgroundJobClient,
        FuelFlowMetrics metrics,
        NotificationDispatcher notifications,
        FuelFlow.Features.Vouchers.Security.IQrCipher qrCipher,
        FuelFlow.Features.Brands.AuditEventService auditEvents)
    {
        _context = context;
        _pdfRenderer = pdfRenderer;
        _voucherDetector = voucherDetector;
        _qrDecoder = qrDecoder;
        _parsers = parsers;
        _logger = logger;
        _backgroundJobClient = backgroundJobClient;
        _notifications = notifications;
        _metrics = metrics;
        _qrCipher = qrCipher;
        _auditEvents = auditEvents;
    }

    /// <summary>
    /// Prepares a rejected voucher's raw region text for storage.
    /// <para>
    /// The region text is the parsed block a voucher was rejected from, and it embeds the QR
    /// payload - the same bearer instrument <see cref="QrPayloadFingerprint"/> exists to keep out of
    /// stores. Writing it verbatim copied redeemable material into
    /// <c>voucher_import_errors</c>, which the admin UI surfaces to a wider audience than the
    /// voucher table and which no retention job prunes.
    /// </para>
    /// <para>
    /// The text is kept, bounded, because its whole purpose is letting an operator see why a row
    /// failed to parse. Only the payload is replaced, by its own fingerprint, so the operator can
    /// still tell two failing rows apart and confirm a re-import produced the same payload.
    /// </para>
    /// </summary>
    internal static string PrepareRawTextForStorage(string? rawText, string? qrPayload)
    {
        var text = rawText ?? string.Empty;

        if (!string.IsNullOrEmpty(qrPayload) && text.Contains(qrPayload, StringComparison.Ordinal))
            text = text.Replace(qrPayload, QrPayloadFingerprint.Describe(qrPayload), StringComparison.Ordinal);

        return text.Length <= MaxStoredRawTextLength
            ? text
            : text[..MaxStoredRawTextLength];
    }

    /// <summary>Bound on a column that is text; a failed parse must not be able to write a page.</summary>
    private const int MaxStoredRawTextLength = 4000;

    private sealed record ImportRunState(
        ImportVouchersCommand Request,
        VoucherImport Import,
        Guid SupplierId,
        string AcquisitionSource,
        Dictionary<string, int> ImportedVouchersByProvider,
        Dictionary<Guid, string?> BrandSlugByFuelType,
        Dictionary<Guid, Guid> BrandIdByFuelType,
        HashSet<string> AddedNumbers,
        HashSet<string> AddedPayloads,
        Dictionary<string, QrParameters> QrParamsCache,
        CancellationToken CancellationToken);

    public async Task<ImportVouchersResponse> HandleAsync(ImportVouchersCommand request, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Import Started for file: {FileName}", request.FileName);
        var stopwatch = Stopwatch.StartNew();

        // The supplier must exist and be selectable. Checked before any parsing so a bad id fails fast
        // with a clear message instead of producing an import whose vouchers can never be settled.
        var supplier = await _context.Suppliers
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == request.SupplierId, cancellationToken);
        if (supplier is null)
        {
            return new ImportVouchersResponse(
                Guid.Empty, 0, 0, 0, 0, 0, stopwatch.Elapsed.TotalSeconds,
                new[] { new ImportErrorLine(0, null, "Select the supplier this PDF was bought from before importing.") });
        }
        var supplierId = supplier.Id;

        // The route the vouchers are acquired on, taken from the supplier rather than from the upload.
        // One PDF is one supplier's delivery, so the operator has already stated the supply relationship
        // by choosing the supplier; asking for the route as well would let the two disagree, and would
        // allow the same supplier to appear in both buckets. Read once here because it is fixed for the
        // whole job and every voucher in it carries the same value.
        var acquisitionSource = supplier.AcquisitionSource;

        // Per-brand tally for the imported-vouchers metric. Brand is a small,
        // bounded set, unlike the file name, so it is safe as a metric label.
        var importedVouchersByProvider = new Dictionary<string, int>();

        // Which brand each fuel type belongs to, loaded once. A PDF carries hundreds of vouchers and the
        // brand/fuel pairing is checked per voucher, so a query each time would be the dominant cost of
        // the import.
        // The catalogue's brand, by SLUG. `fuel_types.brand_id` is a uuid since planning D-21 and the
        // paper says a name, so the ids are resolved once here rather than compared as ids - see
        // BrandFuelPairing.
        var catalogBrandByFuelType = await _context.FuelTypes
            .AsNoTracking()
            .Select(f => new { f.Id, f.BrandId })
            .ToListAsync(cancellationToken);
        var slugByBrandId = (await _context.Brands.AsNoTracking()
                .Select(b => new { b.Id, b.Slug })
                .ToListAsync(cancellationToken))
            .ToDictionary(b => b.Id, b => b.Slug);
        var brandSlugByFuelType = catalogBrandByFuelType
            .Where(f => f.BrandId != Guid.Empty)
            .ToDictionary(
                f => f.Id,
                f => slugByBrandId.TryGetValue(f.BrandId, out var slug) ? slug : null);

        // The brand's ID, by fuel - the key the voucher carries beside the brand's name. The same
        // catalogue read as the slug above, kept as ids so the two cannot disagree about which
        // network a fuel belongs to.
        var brandIdByFuelType = catalogBrandByFuelType
            .Where(f => f.BrandId != Guid.Empty)
            .ToDictionary(f => f.Id, f => f.BrandId);

        var import = new VoucherImport
        {
            Id = Guid.CreateVersion7(),
            FileName = request.FileName,
            PageCount = 0,
            StartedAtUtc = SystemClock.UtcNow,
            Status = "Started",
            ImportedCount = 0,
            DuplicateCount = 0,
            FailedCount = 0
        };

        _context.VoucherImports.Add(import);
        await _context.SaveChangesAsync(cancellationToken);

        var addedNumbers = new HashSet<string>(StringComparer.Ordinal);
        var addedPayloads = new HashSet<string>(StringComparer.Ordinal);

        // Cache QrParameters rows resolved during this import to avoid redundant DB round-trips.
        // Key: "eccLevel|version|maskPattern|encodingMode".
        var qrParamsCache = new Dictionary<string, QrParameters>(StringComparer.OrdinalIgnoreCase);

        var state = new ImportRunState(
            request,
            import,
            supplierId,
            acquisitionSource,
            importedVouchersByProvider,
            brandSlugByFuelType,
            brandIdByFuelType,
            addedNumbers,
            addedPayloads,
            qrParamsCache,
            cancellationToken);

        await ProcessPdfPagesAsync(state);

        return await FinalizeAsync(state, stopwatch);
    }
}
