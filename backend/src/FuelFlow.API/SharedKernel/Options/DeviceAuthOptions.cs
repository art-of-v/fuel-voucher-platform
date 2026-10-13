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

// Operator FLOOR for the device-signature nonce lifetime, not the value the middleware uses.
// The middleware computes max(this, 2 x TimestampToleranceMs/1000) and the factor is load-bearing:
// the tolerance check is symmetric, so a device whose clock runs up to a full tolerance fast is
// accepted, which anchors its timestamp as late as (arrival + tolerance) while the nonce's TTL
// starts on arrival. One tolerance therefore expired the nonce while the signed timestamp was
// still acceptable - a replay window of up to TimestampToleranceMs on /api/purchases{,/bulk,/renew}.
// See DeviceSignatureMiddleware and DeviceSignatureReplayIntegrationTests.
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
