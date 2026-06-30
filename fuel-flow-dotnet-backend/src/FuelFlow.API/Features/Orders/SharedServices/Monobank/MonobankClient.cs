using FuelFlow.API.Features.Orders.SharedServices.Monobank.Models;
using FuelFlow.Options;
using Microsoft.Extensions.Options;

namespace FuelFlow.API.Features.Orders.SharedServices.Monobank;

public sealed class MonobankClient : IMonobankClient
{
    private readonly MonobankOptions _options;
    private readonly HttpClient _httpClient;
    private readonly ILogger<MonobankClient> _logger;

    public MonobankClient(
        IOptions<MonobankOptions> options,
        HttpClient httpClient,
        ILogger<MonobankClient> logger)
    {
        _options = options.Value;
        _httpClient = httpClient;
        _httpClient.BaseAddress = new Uri(_options.BaseUrl);
        _httpClient.DefaultRequestHeaders.Add("X-Token", _options.Token);
        _logger = logger;
    }

    public async Task<MonobankInvoiceResponse> CreateInvoiceAsync(
        MonobankInvoiceRequest request,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Creating Monobank invoice for merchant payment ID {MerchantPaymentId}, amount {Amount} kopecks",
            request.MerchantPaymentInfo,
            request.Amount);

        try
        {
            var payload = new
            {
                amount = request.Amount,
                ccy = 980,
                merchantPaymInfo = request.MerchantPaymentInfo,
                redirectUrl = request.RedirectUrl ?? _options.RedirectUrl,
                webHookUrl = request.WebhookUrl ?? _options.WebhookUrl,
                validity = 3600
            };

            var response = await _httpClient.PostAsJsonAsync(
                "/api/merchant/invoice/create",
                payload,
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogError(
                    "Monobank invoice creation failed. Status: {StatusCode}, Error: {Error}",
                    response.StatusCode,
                    errorContent);

                throw new HttpRequestException(
                    $"Monobank API returned {response.StatusCode}: {errorContent}");
            }

            var result = await response.Content.ReadFromJsonAsync<MonobankInvoiceResponse>(cancellationToken);

            if (result == null)
            {
                throw new InvalidOperationException("Monobank returned null response");
            }

            _logger.LogInformation(
                "Monobank invoice created successfully. InvoiceId: {InvoiceId}, PageUrl: {PageUrl}",
                result.InvoiceId,
                result.PageUrl);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create Monobank invoice for {MerchantPaymentId}",
                request.MerchantPaymentInfo);
            throw;
        }
    }

    public async Task<MonobankInvoiceStatus> GetInvoiceStatusAsync(
        string invoiceId,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Getting Monobank invoice status for {InvoiceId}", invoiceId);

        try
        {
            var response = await _httpClient.GetAsync(
                $"/api/merchant/invoice/status?invoiceId={invoiceId}",
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogError(
                    "Monobank invoice status check failed. Status: {StatusCode}, Error: {Error}",
                    response.StatusCode,
                    errorContent);

                throw new HttpRequestException(
                    $"Monobank API returned {response.StatusCode}: {errorContent}");
            }

            var result = await response.Content.ReadFromJsonAsync<MonobankInvoiceStatus>(cancellationToken);

            if (result == null)
            {
                throw new InvalidOperationException("Monobank returned null status response");
            }

            _logger.LogInformation(
                "Monobank invoice status retrieved: {InvoiceId}, Status: {Status}",
                invoiceId,
                result.Status);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get Monobank invoice status for {InvoiceId}", invoiceId);
            throw;
        }
    }
}
