namespace FuelFlow.SharedKernel.Options;

public sealed class DeviceAuthOptions
{
    public const string SectionName = "DeviceAuth";
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// Explicit acknowledgement that Production is running without device binding on checkout.
    /// Checked only by the startup guard in Program.cs, which refuses to boot in Production when
    /// <see cref="Enabled"/> is false and this is not set. Exists because enabling signature
    /// enforcement is coupled to the released mobile build, so the safe value cannot simply be
    /// forced on - but shipping without it must be a recorded decision rather than a default.
    /// </summary>
    public bool AcknowledgeDisabledInProduction { get; set; } = false;

    public int ChallengeExpirySeconds { get; set; } = 30;

    // Nonce TTL must be >= TimestampToleranceMs/1000, otherwise a consumed
    // signed request can be replayed once its nonce expires from cache but
    // its timestamp is still inside the tolerance window.
    public int SignatureNonceTtlSeconds { get; set; } = 300;

    public int TimestampToleranceMs { get; set; } = 300000;

    public bool AllowDevelopmentBypass { get; set; } = true;

    // The real mobile checkout routes (PurchaseController). Keep in sync with
    // the mobile app's SIGNATURE_REQUIRED_ENDPOINTS list.
    public List<string> RequireSignatureForEndpoints { get; set; } = new()
    {
        "/api/purchases",
        "/api/purchases/bulk"
    };
}
