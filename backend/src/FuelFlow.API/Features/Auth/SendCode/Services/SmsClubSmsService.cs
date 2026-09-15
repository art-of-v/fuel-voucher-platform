using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using FuelFlow.Features.Auth.SendCode.Abstractions;
using FuelFlow.SharedKernel.Observability;
using FuelFlow.SharedKernel.Options;
using Microsoft.Extensions.Options;

namespace FuelFlow.Features.Auth.SendCode.Services;

public sealed class SmsClubSmsService : ISmsService
{
    private readonly SmsClubOptions _options;
    private readonly SmsBudgetGuard _budget;
    private readonly HttpClient _httpClient;
    private readonly TwilioSmsService? _twilioFallback;
    private readonly ILogger<SmsClubSmsService> _logger;

    public SmsClubSmsService(
        IOptions<SmsClubOptions> options,
        SmsBudgetGuard budget,
        HttpClient httpClient,
        ILogger<SmsClubSmsService> logger,
        TwilioSmsService? twilioFallback = null)
    {
        _options = options.Value;
        _budget = budget;
        _httpClient = httpClient;
        _logger = logger;
        _twilioFallback = twilioFallback;
    }

    public async Task SendVerificationCodeAsync(string phoneNumber, string code, CancellationToken cancellationToken)
    {
        // Spend ceiling first: one unit per login attempt, regardless of how many providers
        // are tried. The Twilio fallback skips its own budget check (SendWithoutBudgetCheckAsync).
        if (!_budget.TryConsume())
            throw new SmsBudgetExhaustedException();

        try
        {
            await TrySendViaSmsClubAsync(phoneNumber, code, cancellationToken);
        }
        catch (Exception ex) when (ex is not SmsBudgetExhaustedException)
        {
            if (_twilioFallback is not null)
            {
                _logger.LogWarning(ex, "SMS Club failed; falling back to Twilio for {PhoneNumber}",
                    SensitiveDataRedactor.MaskPhoneNumber(phoneNumber));
                await _twilioFallback.SendWithoutBudgetCheckAsync(phoneNumber, code, cancellationToken);
                return;
            }

            _logger.LogError(ex, "Failed to send SMS via SMS Club to {PhoneNumber}",
                SensitiveDataRedactor.MaskPhoneNumber(phoneNumber));
            throw;
        }
    }

    private async Task TrySendViaSmsClubAsync(string phoneNumber, string code, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post, $"{_options.BaseUrl.TrimEnd('/')}/sms/send");
        request.Headers.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _options.Token);

        var rawPhone = phoneNumber.TrimStart('+');
        request.Content = JsonContent.Create(new SmsClubSendRequest
        {
            Phone = [rawPhone],
            SrcAddr = string.IsNullOrWhiteSpace(_options.SenderName) ? "palne.shop" : _options.SenderName,
            Message = $"Код підтвердження реєстрації в застосунку: {code}"
        });

        using var response = await _httpClient.SendAsync(request, cancellationToken);

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError(
                "SMS Club send failed with HTTP {StatusCode}",
                (int)response.StatusCode);
            throw new InvalidOperationException($"SMS Club returned HTTP {(int)response.StatusCode}");
        }

        if (!ParseResponse(body, out var error) || !string.IsNullOrEmpty(error))
        {
            _logger.LogError("SMS Club send rejected: {Error}", error);
            throw new InvalidOperationException($"SMS Club send rejected: {error}");
        }

        _logger.LogInformation("SMS sent successfully via SMS Club to {PhoneNumber}",
            SensitiveDataRedactor.MaskPhoneNumber(phoneNumber));
    }

    /// <summary>
    /// Parses an SMS Club send response. Returns true when at least one message was accepted
    /// (the response contained a non-empty success_request.info), and false otherwise,
    /// setting error to the first message found in success_request.add_info.
    /// </summary>
    internal static bool ParseResponse(string body, out string? error)
    {
        error = null;
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            if (!root.TryGetProperty("success_request", out var success))
                return false;

            if (success.TryGetProperty("info", out var info)
                && info.ValueKind == JsonValueKind.Object
                && info.EnumerateObject().Any())
            {
                return true;
            }

            if (success.TryGetProperty("add_info", out var addInfo)
                && addInfo.ValueKind == JsonValueKind.Object)
            {
                var first = addInfo.EnumerateObject().FirstOrDefault();
                if (first.Value.ValueKind == JsonValueKind.String)
                    error = first.Value.GetString();
            }

            return false;
        }
        catch (JsonException)
        {
            error = "Invalid response from SMS Club";
            return false;
        }
    }

    private sealed class SmsClubSendRequest
    {
        [JsonPropertyName("phone")]
        public List<string> Phone { get; set; } = [];

        [JsonPropertyName("src_addr")]
        public string SrcAddr { get; set; } = string.Empty;

        [JsonPropertyName("message")]
        public string Message { get; set; } = string.Empty;
    }
}
