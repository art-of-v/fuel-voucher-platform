using System.Text.RegularExpressions;

namespace FuelFlow.SharedKernel.Observability;

/// <summary>
/// Redacts personal values before they leave the application through logs or alerts.
/// </summary>
internal static class SensitiveDataRedactor
{
    /// <summary>
    /// The Bot API takes the credential in the path - <c>/bot&lt;id&gt;:&lt;token&gt;/sendMessage</c> - so
    /// any logger that records a bot request URI records the token with it; the framework's own
    /// <c>System.Net.Http.HttpClient</c> category does exactly that at Information level. The bot
    /// id stays: it is public, and it is what tells an operator which bot failed.
    /// </summary>
    private static readonly Regex TelegramBotToken = new(
        @"bot(\d+):[A-Za-z0-9_-]+",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    internal static bool ContainsTelegramBotToken(string? text) =>
        !string.IsNullOrEmpty(text)
        && text.Contains("bot", StringComparison.OrdinalIgnoreCase)
        && TelegramBotToken.IsMatch(text);

    internal static string RedactTelegramBotToken(string text) =>
        ContainsTelegramBotToken(text)
            ? TelegramBotToken.Replace(text, "bot$1:<redacted>")
            : text;

    /// <summary>
    /// Keeps only the last four digits, which is enough to correlate a support request
    /// without storing a full personal phone number.
    /// </summary>
    internal static string MaskPhoneNumber(string? phoneNumber)
    {
        if (string.IsNullOrEmpty(phoneNumber))
            return "****";

        return phoneNumber.Length <= 4 ? "****" : $"****{phoneNumber[^4..]}";
    }

    /// <summary>
    /// Masks email keeping only first char of local part and domain for correlation.
    /// </summary>
    internal static string MaskEmail(string? email)
    {
        if (string.IsNullOrEmpty(email))
            return "****";

        var atIndex = email.IndexOf('@');
        if (atIndex <= 0)
            return "****";

        var localPart = email[..atIndex];
        var domain = email[atIndex..];

        var maskedLocal = localPart.Length > 0 ? localPart[0] + "***" : "****";

        return maskedLocal + domain;
    }
}
