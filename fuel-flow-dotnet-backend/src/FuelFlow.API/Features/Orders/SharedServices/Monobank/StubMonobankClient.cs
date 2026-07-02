using FuelFlow.API.Features.Orders.SharedServices.Monobank.Models;
using FuelFlow.SharedKernel.Options;
using Microsoft.Extensions.Options;

namespace FuelFlow.API.Features.Orders.SharedServices.Monobank;

public sealed class MockMonobankClient : IMonobankClient
{
    private readonly MonobankOptions _options;
    private readonly ILogger<MockMonobankClient> _logger;
    private readonly Dictionary<string, MonobankInvoiceStatus> _mockInvoices = new();

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
            CreatedDate = DateTime.UtcNow,
            ModifiedDate = DateTime.UtcNow
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
                CreatedDate = DateTime.UtcNow.AddMinutes(-5),
                ModifiedDate = DateTime.UtcNow
            };
        }

        _logger.LogInformation(
            "[MOCK MODE] Returning fake status for {InvoiceId}: {Status}",
            invoiceId,
            status.Status);

        return Task.FromResult(status);
    }

    public void SimulateStatusChange(string invoiceId, string newStatus)
    {
        if (_mockInvoices.TryGetValue(invoiceId, out var invoice))
        {
            invoice.Status = newStatus;
            invoice.ModifiedDate = DateTime.UtcNow;
            _logger.LogInformation(
                "[MOCK MODE] Simulated status change for {InvoiceId} to {Status}",
                invoiceId,
                newStatus);
        }
    }
}
