using Serilog.Core;
using Serilog.Events;

namespace FuelFlow.SharedKernel.Observability;

/// <summary>
/// Rewrites any string value in a log event that carries a Telegram bot token before a sink renders
/// it.
/// <para>
/// The token cannot be kept out of the request URL - the Bot API takes it in the path - and the
/// framework's <c>System.Net.Http.HttpClient</c> category logs that URL at Information level, both
/// as the <c>Uri</c> property and inside its "HTTP POST &lt;url&gt;" scope, so the credential
/// reached the console and Loki without any of our code writing it. Redacting the values instead
/// of the category survives a new caller that logs a bot URL the same way, and enrichment runs
/// before the sinks, so Console and Loki are covered by one rule.
/// </para>
/// <para>
/// The error-log table is written by a separate provider that is fed the rendered message rather
/// than this event, so <see cref="FuelFlow.Features.ErrorLogs.Logging.DatabaseLogger"/> redacts its
/// own columns.
/// </para>
/// </summary>
internal sealed class TelegramTokenRedactingEnricher : ILogEventEnricher
{
    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        List<LogEventProperty>? redacted = null;

        foreach (var property in logEvent.Properties)
        {
            var value = Redact(property.Value);
            if (value is not null)
                (redacted ??= []).Add(new LogEventProperty(property.Key, value));
        }

        if (redacted is null)
            return;

        // Collected first: AddOrUpdateProperty writes into the dictionary that was just enumerated.
        foreach (var property in redacted)
            logEvent.AddOrUpdateProperty(property);
    }

    /// <summary>
    /// Returns the redacted replacement, or null when nothing in the value matched. The scope is a
    /// sequence of strings, the request line a scalar - both are walked.
    /// </summary>
    private static LogEventPropertyValue? Redact(LogEventPropertyValue value) => value switch
    {
        // A request URI arrives either as a string or as a Uri wrapped in a scalar, depending on
        // how the caller passed it; both render as the full URL.
        ScalarValue { Value: string text } when SensitiveDataRedactor.ContainsTelegramBotToken(text) =>
            new ScalarValue(SensitiveDataRedactor.RedactTelegramBotToken(text)),
        ScalarValue { Value: Uri uri } when SensitiveDataRedactor.ContainsTelegramBotToken(uri.ToString()) =>
            new ScalarValue(SensitiveDataRedactor.RedactTelegramBotToken(uri.ToString())),
        SequenceValue sequence => RedactSequence(sequence),
        _ => null
    };

    private static SequenceValue? RedactSequence(SequenceValue sequence)
    {
        var elements = new List<LogEventPropertyValue>(sequence.Elements.Count);
        var changed = false;

        foreach (var element in sequence.Elements)
        {
            var redacted = Redact(element);
            changed |= redacted is not null;
            elements.Add(redacted ?? element);
        }

        return changed ? new SequenceValue(elements) : null;
    }
}
