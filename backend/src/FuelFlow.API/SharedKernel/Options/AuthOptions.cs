namespace FuelFlow.SharedKernel.Options;

public sealed class AuthOptions
{
    public const string SectionName = "Auth";

    /// <summary>The Role.Name that identifies a dashboard administrator. Kept here
    /// so OTP channel selection matches the value [Authorize(Roles = ...)] expects.</summary>
    public const string AdminRoleName = "Admin";

    public bool DevBypass { get; set; } = false;

    /// <summary>
    /// When true (default), admin-role users receive their sign-in OTP by email over the
    /// configured support SMTP account instead of a paid SMS. Delivery falls back to SMS
    /// whenever email is unconfigured, the account has no email, or the send throws, so an
    /// admin can never be locked out by the email path. Set Auth__AdminOtpViaEmail=false to
    /// force SMS for everyone.
    /// </summary>
    public bool AdminOtpViaEmail { get; set; } = true;

    /// <summary>
    /// Canonical public origin (scheme + host, e.g. <c>https://api.palne.shop</c>) that
    /// email-change confirmation links are built from. The confirmation landing
    /// <c>GET /api/auth/email/confirm</c> is served only on the public API origin; the admin
    /// dashboard origin deliberately proxies just an allow-list of <c>/api/*</c> paths and 404s
    /// everything else (see <c>deploy/Caddyfile</c> and <c>admin/nginx.conf</c>). Building the link
    /// from the incoming request host therefore 404s for admin-initiated changes, whose requests
    /// arrive on the admin origin. When set, both the admin and self-service paths use this value;
    /// when blank (the default, and dev/local where one origin serves everything) they fall back to
    /// the request's own scheme+host. Not a secret; committed per-environment.
    /// </summary>
    public string EmailConfirmBaseUrl { get; set; } = "";

    /// <summary>
    /// Phone number (E.164, e.g. +380671234567) of the user to bootstrap as ProductOwner
    /// on application startup. If set and the user exists, their role is set to ProductOwner.
    /// Useful for first deploy — avoids manual DB edit.
    /// </summary>
    public string? BootstrapProductOwnerPhone { get; set; }

    /// <summary>
    /// The E.164 phone number of the single, seeded QA test account used for App Store review
    /// sign-in. This is an <b>identifier</b>, not a secret: the redesign assumes it can become
    /// public. The actual control is the server-side <c>QaTestAccess:Enabled</c> runtime setting
    /// plus <see cref="QaTestAccessCodeHash"/>; the number alone grants nothing. Committed in
    /// source on purpose so the QA identity can be seeded deterministically.
    /// </summary>
    public const string QaTestAccountPhoneNumber = "+380110010203";

    /// <summary>
    /// Fixed deterministic id of the seeded QA test <c>User</c> row (role <c>User</c>, least
    /// privilege). Matches the value inserted by the QA-access migration.
    /// </summary>
    public static readonly Guid QaTestAccountUserId = Guid.Parse("a10c1de5-4a11-4b00-8000-000000000001");

    /// <summary>
    /// The QA verification code, supplied in plaintext through configuration/secret
    /// (<c>Auth__QaTestAccessCode</c>, wired from the deploy secret store). It is <b>never</b>
    /// used from here directly: <see cref="FuelFlow.API.Extensions.ServiceSetup"/> hashes it once
    /// at boot into <see cref="QaTestAccessCodeHash"/> and then clears this property, so the
    /// plaintext does not linger in the bound options for the process lifetime. Absent/blank =
    /// QA access is unconfigured and therefore permanently disabled regardless of the runtime
    /// switch (fail-safe). Treat as a credential: never log it, never return it from an API.
    /// </summary>
    public string? QaTestAccessCode { get; set; }

    /// <summary>
    /// SHA-256 hash (hex) of <see cref="QaTestAccessCode"/>, computed once at startup. Compared
    /// against the hash of the code a QA tester submits, exactly like a stored OTP hash. Null/empty
    /// when no QA code is configured, which forces QA access off. This is the only representation
    /// of the QA code retained in memory.
    /// </summary>
    public string? QaTestAccessCodeHash { get; set; }

    /// <summary>True only when a QA code has been configured and hashed. A false value forces the
    /// QA mechanism off no matter what the runtime <c>QaTestAccess:Enabled</c> flag says.</summary>
    public bool IsQaTestAccessConfigured => !string.IsNullOrEmpty(QaTestAccessCodeHash);
}
