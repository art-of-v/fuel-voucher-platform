using FuelFlow.API.Features.Orders.SharedServices.Monobank.Models;
using FuelFlow.SharedKernel.Options;
using FuelFlow.SharedKernel.Time;
using Microsoft.Extensions.Options;

namespace FuelFlow.API.Features.Orders.SharedServices.Monobank;

public sealed class MockMonobankClient : IMonobankClient
{
    private readonly MonobankOptions _options;
    private readonly ILogger<MockMonobankClient> _logger;
    private readonly Dictionary<string, MonobankInvoiceStatus> _mockInvoices = new();
    private readonly Dictionary<string, List<MonobankCancelListItem>> _mockCancels = new();

    public MockMonobankClient(IOptions<MonobankOptions> options, ILogger<MockMonobankClient> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public Task<MonobankInvoiceResponse> CreateInvoiceAsync(
        MonobankInvoiceRequest request,
        CancellationToken cancellationToken = default)
    {
        _logger.LogWarning(
            "[MOCK MODE] Creating fake Monobank invoice for {MerchantPaymentId}, amount {Amount} kopecks",
            request.MerchantPaymentInfo,
            request.Amount);

        var invoiceId = $"MOCK_{Guid.NewGuid():N}";
        var pageUrl = $"https://mock-monobank.local/invoice/{invoiceId}";

        _mockInvoices[invoiceId] = new MonobankInvoiceStatus
        {
            InvoiceId = invoiceId,
            Status = "created",
            Amount = request.Amount,
            CreatedDate = SystemClock.UtcNow,
            ModifiedDate = SystemClock.UtcNow
        };

        _logger.LogInformation(
            "[MOCK MODE] Fake invoice created: {InvoiceId}, PageUrl: {PageUrl}",
            invoiceId,
            pageUrl);

        return Task.FromResult(new MonobankInvoiceResponse
        {
            InvoiceId = invoiceId,
            PageUrl = pageUrl
        });
    }

    public Task<MonobankInvoiceStatus> GetInvoiceStatusAsync(
        string invoiceId,
        CancellationToken cancellationToken = default)
    {
        _logger.LogWarning("[MOCK MODE] Getting fake invoice status for {InvoiceId}", invoiceId);

        if (!_mockInvoices.TryGetValue(invoiceId, out var status))
        {
            _logger.LogInformation("[MOCK MODE] Invoice {InvoiceId} not found in mock storage, creating default", invoiceId);
            status = new MonobankInvoiceStatus
            {
                InvoiceId = invoiceId,
                Status = "success",
                Amount = 10000,
                CreatedDate = SystemClock.UtcNow.AddMinutes(-5),
                ModifiedDate = SystemClock.UtcNow
            };
        }

        if (_mockCancels.TryGetValue(invoiceId, out var cancels))
        {
            status.CancelList = cancels
                .OrderByDescending(c => c.ModifiedDate)
                .ToList();
        }

        _logger.LogInformation(
            "[MOCK MODE] Returning fake status for {InvoiceId}: {Status}",
            invoiceId,
            status.Status);

        return Task.FromResult(status);
    }

    public Task<MonobankCancelResponse> CancelInvoiceAsync(
        string invoiceId,
        long amountKopecks,
        string extRef,
        IReadOnlyList<MonobankBasketItem>? items = null,
        CancellationToken cancellationToken = default)
    {
        _logger.LogWarning(
            "[MOCK MODE] Cancelling fake invoice {InvoiceId} for {Amount} kopecks (extRef {ExtRef})",
            invoiceId,
            amountKopecks,
            extRef);

        if (!_mockCancels.TryGetValue(invoiceId, out var cancels))
        {
            cancels = new List<MonobankCancelListItem>();
            _mockCancels[invoiceId] = cancels;
        }

        cancels.Add(new MonobankCancelListItem
        {
            Status = "processing",
            Amount = amountKopecks,
            Ccy = 980,
            ExtRef = extRef,
            CreatedDate = SystemClock.UtcNow,
            ModifiedDate = SystemClock.UtcNow
        });

        return Task.FromResult(new MonobankCancelResponse
        {
            Status = "processing",
            CreatedDate = SystemClock.UtcNow,
            ModifiedDate = SystemClock.UtcNow
        });
    }

    public Task<IReadOnlyList<MonobankStatementItem>> GetStatementAsync(
        DateTime fromUtc,
        DateTime toUtc,
        CancellationToken cancellationToken = default)
    {
        // Synthetic, and deliberately not derived from _mockInvoices: MonobankClientFactory builds
        // a NEW MockMonobankClient per ForMerchant call, so an invoice created by an earlier
        // instance is not reachable from this one. Pretending otherwise would hand the
        // reconciliation a statement full of rows it can never match, and every period would report
        // as a total mismatch - a mock that always screams is worse than one that admits it has
        // nothing.
        //
        // One row per day in the range, so a caller gets a non-empty, period-bounded result it can
        // exercise the import and matching paths against. Rows carry no InvoiceId or Reference,
        // which is the accurate description of a sandbox acquirer's statement.
        _logger.LogWarning(
            "[MOCK MODE] Returning a SYNTHETIC Monobank statement for [{From} - {To}). " +
            "These rows match no local order; reconciliation against them will report every row as unmatched.",
            fromUtc, toUtc);

        var items = new List<MonobankStatementItem>();
        for (var day = fromUtc.Date; day < toUtc.Date; day = day.AddDays(1))
        {
            items.Add(new MonobankStatementItem
            {
                // Real statements carry no row id either - the real client composes one from
                // invoice/rrn, date, amount and currency, and this mock has the same amount on every
                // day, so the date alone identifies it here.
                StatementId = $"MOCK_STMT_{day:yyyyMMdd}",
                Amount = 0,
                Ccy = 980,
                Status = "success",
                Date = day
            });
        }

        return Task.FromResult<IReadOnlyList<MonobankStatementItem>>(items);
    }

    public void SimulateStatusChange(string invoiceId, string newStatus)
    {
        if (_mockInvoices.TryGetValue(invoiceId, out var invoice))
        {
            invoice.Status = newStatus;
            invoice.ModifiedDate = SystemClock.UtcNow;
            _logger.LogInformation(
                "[MOCK MODE] Simulated status change for {InvoiceId} to {Status}",
                invoiceId,
                newStatus);
        }
    }

    public void SimulateCancelCompletion(string invoiceId, string newStatus)
    {
        if (_mockCancels.TryGetValue(invoiceId, out var cancels) && cancels.Count > 0)
        {
            cancels[^1].Status = newStatus;
            cancels[^1].ModifiedDate = SystemClock.UtcNow;
            _logger.LogInformation(
                "[MOCK MODE] Simulated cancel completion for {InvoiceId} to {Status}",
                invoiceId,
                newStatus);
        }
    }
}
