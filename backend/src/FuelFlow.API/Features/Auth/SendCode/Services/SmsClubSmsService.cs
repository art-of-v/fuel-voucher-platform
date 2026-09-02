using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using FuelFlow.Features.Auth.SendCode.Abstractions;
using FuelFlow.SharedKernel.Options;
using Microsoft.Extensions.Options;

namespace FuelFlow.Features.Auth.SendCode.Services;

public sealed class SmsClubSmsService : ISmsService
{
    private readonly SmsClubOptions _options;
    private readonly SmsBudgetGuard _budget;
    private readonly HttpClient _httpClient;
    private readonly ILogger<SmsClubSmsService> _logger;

    public SmsClubSmsService(
        IOptions<SmsClubOptions> options,
        SmsBudgetGuard budget,
        HttpClient httpClient,
        ILogger<SmsClubSmsService> logger)
    {
        _options = options.Value;
        _budget = budget;
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task SendVerificationCodeAsync(string phoneNumber, string code, CancellationToken cancellationToken)
    {
        // Spend ceiling first (same discipline as the Twilio path): per-phone and per-IP
        // limits cannot see an attacker cycling thousands of distinct numbers, and every
        // send past this point costs real money.
        if (!_budget.TryConsume())
            throw new SmsBudgetExhaustedException();

        try
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Post, $"{_options.BaseUrl.TrimEnd('/')}/sms/send");
            request.Headers.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _options.Token);

            request.Content = JsonContent.Create(new SmsClubSendRequest
            {
                Phone = [phoneNumber],
                SrcAddr = _options.SenderName,
                Message = $"Your FuelFlow verification code is: {code}"
            });

            using var response = await _httpClient.SendAsync(request, cancellationToken);

            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError(
                    "SMS Club send failed with HTTP {StatusCode}: {Body}",
                    (int)response.StatusCode, body);
                throw new InvalidOperationException($"SMS Club returned HTTP {(int)response.StatusCode}");
            }

            if (!ParseResponse(body, out var error) || !string.IsNullOrEmpty(error))
            {
                _logger.LogError("SMS Club send rejected: {Error}", error);
                throw new InvalidOperationException($"SMS Club send rejected: {error}");
            }

            _logger.LogInformation("SMS sent successfully via SMS Club to {PhoneNumber}", phoneNumber);
        }
        catch (Exception ex) when (ex is not SmsBudgetExhaustedException)
        {
            _logger.LogError(ex, "Failed to send SMS via SMS Club to {PhoneNumber}", phoneNumber);
            throw;
        }
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
