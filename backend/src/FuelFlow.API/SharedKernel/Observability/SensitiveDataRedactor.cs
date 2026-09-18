namespace FuelFlow.SharedKernel.Observability;

/// <summary>
/// Redacts personal values before they leave the application through logs or alerts.
/// </summary>
internal static class SensitiveDataRedactor
{
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
