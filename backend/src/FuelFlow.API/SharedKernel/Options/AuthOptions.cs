namespace FuelFlow.SharedKernel.Options;

public sealed class AuthOptions
{
    public const string SectionName = "Auth";

    public bool DevBypass { get; set; } = false;

    /// <summary>
    /// Allowlisted QA/test phone numbers that skip the SMS provider entirely.
    /// Key: the phone number in normalized international format ("+380..."),
    /// value: the fixed 6-digit code that number always receives.
    /// Issuance is logged and no SMS is sent, so QA costs nothing and works
    /// in production without burning Twilio credit. Configured via env vars,
    /// e.g. Auth__TestPhones__+380991234567=123456.
    /// </summary>
    public Dictionary<string, string> TestPhones { get; set; } = new();
}
