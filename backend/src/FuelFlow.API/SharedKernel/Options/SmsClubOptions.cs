namespace FuelFlow.SharedKernel.Options;

public sealed class SmsClubOptions
{
    public const string SectionName = "SmsClub";

    /// <summary>
    /// The deadline the SMS brand is given. The OTP path waits on this, and a login that waits
    /// a hundred seconds for a code is a login that never happens.
    /// </summary>
    public const int DefaultTimeoutSeconds = 10;

    public string Token { get; set; } = string.Empty;
    public string SenderName { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = "https://im.smsclub.mobi";

    /// <summary>
    /// How long the brand is given to accept the message. Zero or less means the default.
    /// </summary>
    public int TimeoutSeconds { get; set; } = DefaultTimeoutSeconds;
}