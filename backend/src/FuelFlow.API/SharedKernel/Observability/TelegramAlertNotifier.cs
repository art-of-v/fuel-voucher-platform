using System.Net.Http.Json;
using System.Text;
using FuelFlow.SharedKernel.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FuelFlow.SharedKernel.Observability;

/// <summary>
/// Sends alerts to a Telegram group via the Bot API.
/// <para>
/// Every message is prefixed with the environment and service so a single group can
/// safely receive traffic from Development and Production without ambiguity.
/// </para>
/// </summary>
public sealed class TelegramAlertNotifier : IAlertNotifier
{
    private readonly HttpClient _httpClient;
    private readonly TelegramOptions _telegram;
    private readonly ObservabilityOptions _observability;
    private readonly ILogger<TelegramAlertNotifier> _logger;
    private readonly AlertSeverity _minimumSeverity;

    public TelegramAlertNotifier(
        HttpClient httpClient,
        IOptions<TelegramOptions> telegram,
        IOptions<ObservabilityOptions> observability,
        ILogger<TelegramAlertNotifier> logger)
    {
        _httpClient = httpClient;
        _telegram = telegram.Value;
        _observability = observability.Value;
        _logger = logger;

        _minimumSeverity = Enum.TryParse<AlertSeverity>(_telegram.MinimumSeverity, ignoreCase: true, out var parsed)
            ? parsed
            : AlertSeverity.Warning;
    }

    public async Task SendAsync(
        AlertSeverity severity,
        string title,
        string message,
        IReadOnlyDictionary<string, string>? context = null,
        bool respectMinimumSeverity = true,
        CancellationToken cancellationToken = default)
    {
        if (!_telegram.IsConfigured || (respectMinimumSeverity && severity < _minimumSeverity))
        {
            return;
        }

        var text = BuildMessage(severity, title, message, context);

        foreach (var chatId in _telegram.ChatIds.Where(id => !string.IsNullOrWhiteSpace(id)))
        {
            try
            {
                using var response = await _httpClient.PostAsJsonAsync(
                    $"/bot{_telegram.BotToken}/sendMessage",
                    new
                    {
                        chat_id = chatId,
                        text,
                        parse_mode = "HTML",
                        disable_web_page_preview = true
                    },
                    cancellationToken);

                if (!response.IsSuccessStatusCode)
                {
                    var body = await response.Content.ReadAsStringAsync(cancellationToken);
                    _logger.LogWarning(
                        "Telegram alert to chat {ChatId} failed with {StatusCode}: {Body}",
                        chatId, (int)response.StatusCode, body);
                }
            }
            catch (Exception ex)
            {
                // Alerting must never surface as a failure in the caller's operation.
                _logger.LogWarning(ex, "Telegram alert to chat {ChatId} could not be delivered", chatId);
            }
        }
    }

    private string BuildMessage(
        AlertSeverity severity,
        string title,
        string message,
        IReadOnlyDictionary<string, string>? context)
    {
        var icon = severity switch
        {
            AlertSeverity.Critical => "\U0001F534",
            AlertSeverity.Warning => "\U0001F7E1",
            _ => "\U0001F535"
        };

        var sb = new StringBuilder();
        sb.Append(icon)
          .Append(" <b>[")
          .Append(Escape(_observability.Environment))
          .Append('/')
          .Append(Escape(_observability.ServiceName))
          .Append("] ")
          .Append(Escape(title))
          .AppendLine("</b>");

        sb.AppendLine(Escape(message));

        if (context is { Count: > 0 })
        {
            sb.AppendLine();
            foreach (var (key, value) in context)
            {
                sb.Append("<b>").Append(Escape(key)).Append("</b>: ")
                  .AppendLine(Escape(value));
            }
        }

        sb.AppendLine();
        sb.Append("<i>").Append(DateTimeOffset.UtcNow.ToString("yyyy-MM-dd HH:mm:ss")).Append(" UTC</i>");

        var text = sb.ToString();

        // Telegram rejects messages longer than 4096 characters.
        return text.Length > 4000 ? string.Concat(text.AsSpan(0, 4000), "\n…") : text;
    }

    private static string Escape(string value) =>
        value.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
}
