using System.Text.Json;

namespace FuelFlow.Features.Notifications.SharedModels;

/// <summary>
/// The data a client needs to open what a notification is about, frozen at the moment the
/// notification was written.
/// </summary>
/// <remarks>
/// <para>
/// This is the same shape the push payload carries (<c>data</c> in <c>NotificationService</c>), so
/// the two are written from the same place and mean the same thing. A push that arrives and the
/// in-app row that stays behind it then route identically.
/// </para>
/// <para>
/// The ids are strings because they are ids of things that may be gone by the time the
/// notification is read: a company can be deleted, an order can be deleted. A dead id is a dead
/// button, not a crash - the client checks the target still resolves before it navigates.
/// </para>
/// </remarks>
public sealed record NotificationPayload
{
    /// <summary>The company whose worker context the notification opens.</summary>
    public string? LegalEntityId { get; init; }

    /// <summary>The order the notification is about.</summary>
    public string? OrderId { get; init; }

    /// <summary>How many vouchers the notification is about.</summary>
    public int? VoucherCount { get; init; }

    /// <summary>
    /// The payload options. camelCase, because this is the vocabulary the push payload and the
    /// client's own JSON already use.
    /// </summary>
    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Serialises the payload for the <c>payload</c> column.
    /// </summary>
    public static string Serialize(NotificationPayload payload)
        => JsonSerializer.Serialize(payload, JsonOptions);

    /// <summary>
    /// Reads one id out of a stored payload. Returns null for a missing key, a non-string value, an
    /// unparsable id or a payload that is not JSON at all - a notification is not worth failing a
    /// request over.
    /// </summary>
    public static string? ReadId(string? payload, string key)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(payload);
            if (!document.RootElement.TryGetProperty(key, out var value)
                || value.ValueKind != JsonValueKind.String)
            {
                return null;
            }

            var text = value.GetString();
            return string.IsNullOrWhiteSpace(text) ? null : text;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
