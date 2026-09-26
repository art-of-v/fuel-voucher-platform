using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using FuelFlow.SharedKernel.Options;
using Microsoft.Extensions.Options;

namespace FuelFlow.Features.Notifications.Push;

/// <summary>
/// Typed HttpClient over the Expo Push service (https://docs.expo.dev/push-notifications/sending-notifications/).
/// One in-app notification fans out to every active token of the target user. Best-effort: any HTTP
/// or parse failure is logged and swallowed, returning no results for the affected batch so the
/// caller never wrongly reacts (e.g. deactivates a token) on an ambiguous outcome.
/// </summary>
public sealed class ExpoPushClient : IExpoPushSender
{
    // Expo caps a single /push/send request at 100 messages.
    private const int ExpoBatchSize = 100;

    private readonly HttpClient _httpClient;
    private readonly ExpoPushOptions _options;
    private readonly ILogger<ExpoPushClient> _logger;

    public ExpoPushClient(
        HttpClient httpClient,
        IOptions<ExpoPushOptions> options,
        ILogger<ExpoPushClient> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyList<ExpoPushResult>> SendAsync(
        IReadOnlyList<ExpoPushMessage> messages,
        CancellationToken cancellationToken)
    {
        if (!_options.Enabled || messages.Count == 0)
            return [];

        var results = new List<ExpoPushResult>(messages.Count);
        foreach (var batch in messages.Chunk(ExpoBatchSize))
        {
            results.AddRange(await SendBatchAsync(batch, cancellationToken));
        }

        return results;
    }

    private async Task<IReadOnlyList<ExpoPushResult>> SendBatchAsync(
        IReadOnlyList<ExpoPushMessage> batch,
        CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, _options.BaseUrl);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            if (!string.IsNullOrWhiteSpace(_options.AccessToken))
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.AccessToken);

            request.Content = JsonContent.Create(batch.Select(m => new ExpoSendItem
            {
                To = m.Token,
                Title = m.Title,
                Body = m.Body,
                Data = m.Data,
            }));

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Expo push send failed with HTTP {StatusCode}", (int)response.StatusCode);
                return [];
            }

            return ParseResponse(body, batch);
        }
        catch (Exception ex)
        {
            // Best-effort: a push failure must never break notification processing.
            _logger.LogWarning(ex, "Expo push send threw");
            return [];
        }
    }

    /// <summary>
    /// Parses an Expo /push/send response into positional results. The response's <c>data</c>
    /// array is in the same order as the messages sent, so <c>data[i]</c> maps to <c>batch[i]</c>.
    /// A malformed or unexpected body yields no results (nothing is deactivated on ambiguity).
    /// Note: this reads the immediate send ticket only; asynchronous delivery receipts (a later
    /// source of DeviceNotRegistered) are not polled — a stale token is instead cleaned up on the
    /// next send that returns an immediate error.
    /// </summary>
    internal static IReadOnlyList<ExpoPushResult> ParseResponse(string body, IReadOnlyList<ExpoPushMessage> batch)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (!doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
                return [];

            var results = new List<ExpoPushResult>(batch.Count);
            var index = 0;
            foreach (var ticket in data.EnumerateArray())
            {
                if (index >= batch.Count) break;

                var isOk = ticket.TryGetProperty("status", out var status)
                    && status.ValueKind == JsonValueKind.String
                    && string.Equals(status.GetString(), "ok", StringComparison.OrdinalIgnoreCase);

                string? errorCode = null;
                if (!isOk
                    && ticket.TryGetProperty("details", out var details)
                    && details.ValueKind == JsonValueKind.Object
                    && details.TryGetProperty("error", out var error)
                    && error.ValueKind == JsonValueKind.String)
                {
                    errorCode = error.GetString();
                }

                results.Add(new ExpoPushResult(
                    batch[index].Token,
                    isOk ? ExpoPushStatus.Ok : ExpoPushStatus.Error,
                    errorCode));
                index++;
            }

            return results;
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private sealed class ExpoSendItem
    {
        [JsonPropertyName("to")]
        public string To { get; set; } = string.Empty;

        [JsonPropertyName("title")]
        public string Title { get; set; } = string.Empty;

        [JsonPropertyName("body")]
        public string Body { get; set; } = string.Empty;

        [JsonPropertyName("sound")]
        public string Sound { get; set; } = "default";

        [JsonPropertyName("data")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public IReadOnlyDictionary<string, object>? Data { get; set; }
    }
}
