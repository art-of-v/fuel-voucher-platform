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
    /// in production without burning Twilio credit.
    /// <para>
    /// Two ways to configure: individual env vars like
    /// <c>Auth__TestPhones__+380991234567=123456</c> (bound here directly), or the
    /// comma-separated <see cref="TestPhoneRaw"/> convenience string. The raw form
    /// exists because a docker-compose <c>environment</c> key cannot contain the
    /// "+" of a phone number, and this deployment supplies the list through one
    /// interpolated variable in deploy/.env (AUTH_TEST_PHONES).
    /// </para>
    /// </summary>
    public Dictionary<string, string> TestPhones { get; set; } = new();

    /// <summary>
    /// Raw allowlist string, e.g. <c>+380991234567=427135,+380931112222=900817</c>.
    /// Parsed into <see cref="TestPhones"/> after binding; see
    /// <see cref="ParseTestPhonePairs"/> for the exact format.
    /// </summary>
    public string TestPhoneRaw { get; set; } = string.Empty;

    /// <summary>
    /// Parses the raw allowlist: comma-separated <c>number=code</c> pairs. Whitespace
    /// around entries is tolerated; entries without a separator are skipped rather
    /// than throwing - a typo in one optional env var must not stop the API from
    /// starting. A malformed CODE is kept here and rejected later, where ResolveCode
    /// logs it with the offending number.
    /// </summary>
    public static IReadOnlyDictionary<string, string> ParseTestPhonePairs(string? raw)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (string.IsNullOrWhiteSpace(raw))
            return result;

        foreach (var entry in raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var separator = entry.IndexOf('=');
            if (separator <= 0)
                continue;

            var number = entry[..separator].Trim();
            var code = entry[(separator + 1)..].Trim();

            if (number.Length > 0 && code.Length > 0)
                result[number] = code;
        }

        return result;
    }
}
