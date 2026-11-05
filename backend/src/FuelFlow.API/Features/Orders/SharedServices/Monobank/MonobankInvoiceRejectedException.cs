using System.Net;
using System.Text.Json;

namespace FuelFlow.API.Features.Orders.SharedServices.Monobank;

/// <summary>
/// The acquirer refused to open a payment at all.
/// <para>
/// It carries the bank's own machine code and sentence so the operator reads what the bank said
/// instead of a stack trace, and so the app can answer with a sentence the customer can act on.
/// It stays an <see cref="HttpRequestException"/> because callers that already treat any failure
/// of the HTTP call as "the acquirer did not answer" must keep treating this one the same way -
/// reconciliation and the statement importer both do.
/// </para>
/// </summary>
public sealed class MonobankInvoiceRejectedException : HttpRequestException
{
    /// <summary>
    /// The machine code the customer's app is told. Deliberately one code for every refusal: the
    /// customer cannot act on "the merchant has no active terminal", only on "try again".
    /// </summary>
    public const string Code = "payment_provider_unavailable";

    public MonobankInvoiceRejectedException(HttpStatusCode statusCode, string errorBody)
        : base($"Monobank API returned {statusCode}: {errorBody}", null, statusCode)
    {
        (ErrCode, ErrText) = ReadErrorBody(errorBody);
    }

    /// <summary>The bank's machine code (<c>FORBIDDEN</c>, <c>INVALID_PARAM</c>), null if the body was not its JSON.</summary>
    public string? ErrCode { get; }

    /// <summary>The bank's own words, e.g. <c>you don't have active terminal for internet acquiring</c>.</summary>
    public string? ErrText { get; }

    private static (string? Code, string? Text) ReadErrorBody(string errorBody)
    {
        // A proxy error page arrives where the bank's JSON should be, and a checkout that cannot
        // be paid must not fail twice on the way to saying so.
        try
        {
            using var document = JsonDocument.Parse(errorBody);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                return (null, null);

            return (ReadString(document.RootElement, "errCode"), ReadString(document.RootElement, "errText"));
        }
        catch (JsonException)
        {
            return (null, null);
        }
    }

    private static string? ReadString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
