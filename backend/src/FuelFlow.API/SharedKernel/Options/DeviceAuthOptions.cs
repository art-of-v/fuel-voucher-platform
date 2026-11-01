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

    /// <summary>
    /// Routes that demand a device proof only from an app at or above the given version. The key is
    /// a pattern - an optional <c>METHOD </c> prefix, then a path whose segments may contain
    /// <c>*</c> - and the value is the native app version whose bundle carries the signing.
    /// </summary>
    /// <remarks>
    /// <para>
    /// These are kept apart from <see cref="RequireSignatureForEndpoints"/> on purpose: the list above
    /// is what an already-installed bundle signs, so adding a route there refuses that call on every
    /// phone that has not taken the update yet. A failed <c>mark-used</c> is a customer standing at
    /// the station with a voucher in hand, which is worse than the window this gate leaves open.
    /// </para>
    /// <para>
    /// The version is compared the way <see cref="AttestationOptions.MinimumEnforcedAppVersion"/> is:
    /// a missing or unparseable <c>X-App-Version</c> is not enforced, so a build that predates the
    /// header keeps working. That also means a caller can dodge the gate by lying about its version -
    /// this is a rollout device, not the boundary itself. The boundary closes when the version that
    /// signs these routes is what the stores serve: raise <see cref="AppVersionOptions.MinimumVersion"/>
    /// to it and move these patterns into <see cref="RequireSignatureForEndpoints"/>.
    /// </para>
    /// </remarks>
    public Dictionary<string, string> RequireSignatureForEndpointsFrom { get; set; } = new()
    {
        ["/api/vouchers/*/mark-used"] = "1.3.0",
        ["/api/vouchers/*/restore"] = "1.3.0",
        ["DELETE /api/purchases/*"] = "1.3.0"
    };
}
