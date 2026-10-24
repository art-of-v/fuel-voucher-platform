namespace FuelFlow.SharedKernel.Options;

public sealed class AuthOptions
{
    public const string SectionName = "Auth";

    /// <summary>The Role.Name that identifies a dashboard administrator. Kept here
    /// so OTP channel selection matches the value [Authorize(Roles = ...)] expects.</summary>
    public const string AdminRoleName = "Admin";

    public bool DevBypass { get; set; } = false;

    /// <summary>
    /// The pepper a six-digit verification code is hashed with before it reaches the database.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A six-digit code has a million possible values, so a bare SHA-256 digest of it is not a
    /// secret: anybody who reads the <c>verification_codes</c> table, or a backup of it, can search
    /// that space in milliseconds. The pepper is the half of the key that never reaches the
    /// database, so a leaked dump alone cannot test a candidate. It is supplied by the deployment
    /// (<c>Auth__OtpPepper</c> / <c>OTP_PEPPER</c>) and Production refuses to start without a strong
    /// one. Staging needs its own; Development falls back to
    /// <see cref="DevelopmentOtpPepper"/>, which Production refuses.
    /// </para>
    /// </remarks>
    public string OtpPepper { get; set; } = DevelopmentOtpPepper;

    /// <summary>
    /// The committed fallback pepper for the local development loop and the test suite only. It is
    /// not a secret - it is in the repository - which is why Production and Staging refuse it.
    /// </summary>
    public const string DevelopmentOtpPepper = "fuelflow-development-otp-pepper-not-a-secret";

    /// <summary>
    /// When true, staff-role users receive their sign-in OTP by email over the configured support
    /// SMTP account instead of a paid SMS. Delivery falls back to SMS whenever email is
    /// unconfigured, the account has no email, or the send throws, so a staff member is never
    /// locked out by the email path.
    /// <para>
    /// <b>The default is false.</b> A mailbox is the weakest channel the platform has: it is the
    /// one an attacker can influence (forwarding rules, spam-folder rules, a provider breach) and
    /// the one a phishing campaign reaches. Staff OTPs therefore go to the SIM, which needs the
    /// handset itself.
    /// </para>
    /// <para>
    /// Turning it on is a break-glass, not a setting: it is meant for the day the SMS gateway is
    /// down. It requires an explicit acknowledgement in Production
    /// (<see cref="AdminOtpViaEmailBreakGlassAcknowledged"/>), every use of it is recorded as a
    /// security event, and it is not offered to an account without a second factor -
    /// a weaker first factor must not become the whole sign-in for an account that lacks one.
    /// </para>
    /// </summary>
    public bool AdminOtpViaEmail { get; set; } = false;

    /// <summary>
    /// The owner's explicit acknowledgement that Production runs with the weaker email channel.
    /// </summary>
    /// <remarks>
    /// The break-glass is a decision, and a decision has to be written down rather than inferred
    /// from an environment variable that somebody set during an outage and nobody removed. The
    /// Production configuration validator refuses to start when <see cref="AdminOtpViaEmail"/> is
    /// on and this is not, so the two cannot drift apart silently.
    /// </remarks>
    public bool AdminOtpViaEmailBreakGlassAcknowledged { get; set; } = false;

    /// <summary>
    /// When true, the sign-in code is delivered over push (Expo) to the account's own devices
    /// instead of over SMS.
    /// </summary>
    /// <remarks>
    /// <para>
    /// An SMS costs money and the brand balance is finite. When it runs out the budget guard
    /// refuses every send, so nobody can sign in at all - a push costs nothing, and the platform
    /// already ships one to every device that registered its token.
    /// </para>
    /// <para>
    /// <b>The default is false</b>, because the code then rides through a third-party push
    /// service instead of the carrier, and the device that receives it is not necessarily the
    /// device that is signing in. The flag turns the cheaper channel on for a deployment that
    /// has run out of SMS credit; SMS stays the fallback for an account with no active device
    /// token, so a device that never registered is never locked out. When both this and the
    /// email break-glass are on, push is tried first: it is free and device-bound, and the
    /// mailbox is neither.
    /// </para>
    /// </remarks>
    public bool OtpDeliveryViaPush { get; set; } = false;

    /// <summary>
    /// Whether a staff sign-in has to prove a second factor.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The default is true, and Production refuses to start when it is false: a staff account
    /// that can move money must not be reachable with a single factor. The switch exists for the
    /// local development loop and for the moment a deployment has to be rolled back - not as a
    /// way to run the platform.
    /// </para>
    /// <para>
    /// <see cref="DevBypass"/> switches the second factor off as well, because it switches
    /// everything else off: a development process that demands an authenticator app cannot be
    /// developed without one.
    /// </para>
    /// </remarks>
    public bool StaffSecondFactorRequired { get; set; } = true;

    /// <summary>
    /// The issuer shown next to the entry in the authenticator app. It is the name of the
    /// product, and it is what the user checks before typing a code into it.
    /// </summary>
    public string StaffTotpIssuer { get; set; } = "FuelFlow";

    /// <summary>
    /// Canonical public origin (scheme + host, e.g. <c>https://api.palne.shop</c>) that
    /// email-change confirmation links are built from. The confirmation landing
    /// <c>GET /api/auth/email/confirm</c> is served only on the public API origin; the admin
    /// dashboard origin deliberately proxies just an allow-list of <c>/api/*</c> paths and 404s
    /// everything else (see <c>deploy/Caddyfile</c> and <c>admin/nginx.conf.template</c>). Building the link
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
    /// <remarks>
    /// Rekeyed from a hand-written literal to a v7 key by the <c>QaAccountIdentity</c> data
    /// migration, which rekeys the account only while nothing names it - see that migration for why
    /// an environment with history may legitimately still be on the old value. Nothing in the
    /// running system reads this: QA is identified at runtime by
    /// <see cref="QaTestAccountPhoneNumber"/>, and this constant exists for the test suite's
    /// convenience.
    /// </remarks>
    public static readonly Guid QaTestAccountUserId = Guid.Parse("01a136cb-e966-79bf-8cd3-99ccf51bd6b0");

    /// <summary>
    /// The QA verification code, supplied in plaintext through configuration/secret
    /// (<c>Auth__QaTestAccessCode</c>, wired from the deploy secret store). It is <b>never</b>
    /// used from here directly: <see cref="FuelFlow.Features.Auth.Security.OtpSecuritySetup.AddOtpSecurity"/>
    /// hashes it once at boot into <see cref="QaTestAccessCodeHash"/> and then clears this property, so the
    /// plaintext does not linger in the bound options for the process lifetime. Absent/blank =
    /// QA access is unconfigured and therefore permanently disabled regardless of the runtime
    /// switch (fail-safe). Treat as a credential: never log it, never return it from an API.
    /// </summary>
    public string? QaTestAccessCode { get; set; }

    /// <summary>
    /// Peppered HMAC-SHA256 hash of <see cref="QaTestAccessCode"/> (see
    /// <c>Features/Auth/Security/OtpCodeHasher</c>), computed once at startup with
    /// <see cref="OtpPepper"/>. Compared in constant time against the hash of the code a QA tester
    /// submits, exactly like a stored OTP hash. Null/empty when no QA code is configured, which
    /// forces QA access off. This is the only representation of the QA code retained in memory.
    /// </summary>
    public string? QaTestAccessCodeHash { get; set; }

    /// <summary>True only when a QA code has been configured and hashed. A false value forces the
    /// QA mechanism off no matter what the runtime <c>QaTestAccess:Enabled</c> flag says.</summary>
    public bool IsQaTestAccessConfigured => !string.IsNullOrEmpty(QaTestAccessCodeHash);
}
