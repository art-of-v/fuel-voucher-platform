namespace FuelFlow.SharedKernel.Options;

/// <summary>
/// How strictly the API treats a missing or invalid app-attestation proof.
/// </summary>
public enum AttestationMode
{
    /// <summary>The middleware and the controller do nothing. Same effect as <c>Enabled=false</c>.</summary>
    Off = 0,

    /// <summary>Validate every proof that is present and log the outcome, but never refuse a request.</summary>
    ReportOnly = 1,

    /// <summary>Refuse a request to a protected endpoint when it carries no valid proof.</summary>
    Enforce = 2,
}

/// <summary>
/// Mobile app attestation (Apple App Attest / Google Play Integrity) and the rollout controls that
/// make it safe to turn on for a fleet that still contains older builds.
/// </summary>
/// <remarks>
/// <para>
/// The feature is off by default and is intended to be deployed in <see cref="AttestationMode.ReportOnly"/>
/// first. The server can only verify a proof that the client sends; an older mobile build sends
/// nothing, so enforcing before that build is in users' hands would lock those users out of the
/// protected endpoints. <see cref="MinimumEnforcedAppVersion"/> and <see cref="EnforcePlatforms"/>
/// are the staged-enforcement controls: they decide which clients are subject to enforcement while
/// the mode is <see cref="AttestationMode.Enforce"/>.
/// </para>
/// <para>
/// The app version and platform headers are supplied by the client and are therefore not a security
/// boundary. They are a rollout boundary. The proof is the security control, and the proof is what
/// the verifiers validate.
/// </para>
/// </remarks>
public sealed class AttestationOptions
{
    public const string SectionName = "Attestation";

    /// <summary>Master switch. When false the feature is completely inert.</summary>
    public bool Enabled { get; set; }

    /// <summary>Report-only by default, because no released build sends a proof yet.</summary>
    public AttestationMode Mode { get; set; } = AttestationMode.ReportOnly;

    /// <summary>
    /// Explicit acknowledgement that Production is running without attestation enforcement.
    /// Checked only by the startup guard, like the DeviceAuth equivalent: the control is coupled to
    /// the released mobile build, so it cannot simply be forced on, but shipping without it must be
    /// a recorded decision rather than a default.
    /// </summary>
    public bool AcknowledgeDisabledInProduction { get; set; }

    /// <summary>How long a one-time attestation challenge is valid.</summary>
    public int ChallengeExpirySeconds { get; set; } = 120;

    /// <summary>
    /// How far the timestamp inside a Play Integrity token may be from the server clock. Play
    /// Integrity tokens are short-lived; the tolerance is deliberately tighter than the token TTL
    /// because the challenge is what makes the proof fresh.
    /// </summary>
    public int GoogleClockSkewSeconds { get; set; } = 120;

    /// <summary>The Android package name the Play Integrity verdict must name.</summary>
    public string GooglePackageName { get; set; } = "com.artem.vashchuk.mobileappnative";

    /// <summary>
    /// Service-account JSON for the Play Integrity API. Supplied through an environment secret
    /// (<c>Attestation__GoogleCredentialsJson</c>), never committed. Empty means the Google verifier
    /// is unavailable rather than silently trusting.
    /// </summary>
    public string? GoogleCredentialsJson { get; set; }

    /// <summary>
    /// Whether a Play Integrity response that identifies itself as a test response is accepted.
    /// Off in Production by default; a test response must not be a way to exercise the money path.
    /// </summary>
    public bool GoogleAllowTestingResponses { get; set; }

    /// <summary>
    /// The device integrity verdicts accepted from Play Integrity. A token is accepted when at
    /// least one listed verdict is present; <c>MEETS_STRONG_INTEGRITY</c> is the stronger signal,
    /// but a device that only reports <c>MEETS_DEVICE_INTEGRITY</c> still passes the default policy.
    /// </summary>
    public List<string> GoogleAllowedDeviceVerdicts { get; set; } =
        ["MEETS_DEVICE_INTEGRITY", "MEETS_STRONG_INTEGRITY"];

    /// <summary>
    /// Optional app-signing certificate digests (SHA-256, base64 web-safe, as Play Integrity
    /// reports them). When the list is non-empty the token must name one of them. Empty means the
    /// <c>PLAY_RECOGNIZED</c> verdict and the package name are the app checks.
    /// </summary>
    public List<string> GoogleAllowedCertificateDigests { get; set; } = [];

    /// <summary>The App Attest app identifier: team id + bundle id.</summary>
    public string AppleAppId { get; set; } = "TB929MCW58.com.fuelflowmarketplace.www";

    /// <summary>App Attest environment the server accepts: <c>Production</c> or <c>Development</c>.</summary>
    public string AppleEnvironment { get; set; } = "Production";

    /// <summary>
    /// Development-only: log and allow when a proof is missing or invalid. Production and Staging
    /// ignore this flag; a test environment that wants it must use report-only mode instead.
    /// </summary>
    public bool AllowDevelopmentBypass { get; set; } = true;

    /// <summary>
    /// The oldest client version that enforcement applies to. Older builds are observed and
    /// allowed, which is what lets the server enforce for the new build without locking out the
    /// fleet that has not updated yet.
    /// </summary>
    public string MinimumEnforcedAppVersion { get; set; } = "0.0.0";

    /// <summary>
    /// The client platforms enforcement applies to, by the <c>X-Client-Platform</c> header value.
    /// Android is deliberately absent until the package is distributed through Play: Play Integrity
    /// cannot return <c>PLAY_RECOGNIZED</c> for a sideloaded or TestFlight-style build.
    /// </summary>
    public List<string> EnforcePlatforms { get; set; } = ["ios"];

    /// <summary>
    /// The endpoints a valid proof is required for (exact path, or a suffix pattern with a trailing
    /// <c>*</c>). Keep in sync with the mobile client's <c>ATTESTATION_REQUIRED_ENDPOINTS</c>.
    /// </summary>
    public List<string> ProtectedEndpoints { get; set; } =
    [
        "/api/auth/send-code",
        "/api/auth/verify",
        "/api/purchases",
        "/api/purchases/bulk",
        "/api/purchases/renew",
        "/api/vouchers/*/mark-used",
        "/api/vouchers/*/restore",
    ];
}
