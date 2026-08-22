namespace FuelFlow.SharedKernel.Options;

public sealed class TwilioOptions
{
    public const string SectionName = "Twilio";

    /// <summary>Used when no limit is configured. Sized for expected organic signup volume,
    /// not for the largest number that would still be affordable.</summary>
    public const int DefaultDailySendLimit = 500;

    public string AccountSid { get; set; } = string.Empty;
    public string AuthToken { get; set; } = string.Empty;
    public string PhoneNumber { get; set; } = string.Empty;

    /// <summary>
    /// Maximum outbound SMS per rolling 24 hours across all recipients. Backstop against an
    /// OTP pump that cycles phone numbers to stay under the per-phone limit while draining the
    /// Twilio balance. Zero or negative falls back to <see cref="DefaultDailySendLimit"/>.
    /// </summary>
    public int DailySendLimit { get; set; } = DefaultDailySendLimit;
}
