using FuelFlow.Features.Settings;
using FuelFlow.SharedKernel.Abstractions;
using FuelFlow.SharedKernel.Options;
using Microsoft.Extensions.Options;

namespace FuelFlow.Features.Auth.QaTestAccess;

/// <summary>
/// The single place that answers "is QA test-access live, and is this the QA phone?". All the
/// special-case QA logic funnels through here so it does not leak across the auth stack
/// (spec §9). The send-code and verify handlers consult this; the admin toggle mutates the
/// underlying runtime switch.
/// <para>
/// Security model: the QA phone number is a public identifier, not a secret. Access is gated by
/// two independent server-side facts, both of which must hold:
/// <list type="number">
///   <item>a QA code is configured (<see cref="IsConfigured"/>) — the hash was supplied at boot;</item>
///   <item>the runtime <c>QaTestAccess:Enabled</c> switch is on (<see cref="IsActiveAsync"/>).</item>
/// </list>
/// Either being false disables QA sign-in entirely. The mechanism is scoped to exactly one
/// pre-seeded, least-privilege identity, so it can never authenticate an arbitrary phone.
/// </para>
/// </summary>
public sealed class QaTestAccessService
{
    private readonly IOptions<AuthOptions> _authOptions;
    private readonly RuntimeSettingsService _settings;
    private readonly ILogger<QaTestAccessService> _logger;

    public QaTestAccessService(
        IOptions<AuthOptions> authOptions,
        RuntimeSettingsService settings,
        IPhoneNumberService phoneNumberService,
        ILogger<QaTestAccessService> logger)
    {
        _authOptions = authOptions;
        _settings = settings;
        _logger = logger;

        // Normalize the committed QA number the same way every inbound phone is normalized, so the
        // IsQaPhone comparison is exact regardless of how the tester types it.
        QaPhoneNumber = phoneNumberService.Normalize(AuthOptions.QaTestAccountPhoneNumber);
    }

    /// <summary>The seeded QA account's phone in the app's normalized (E.164) form.</summary>
    public string QaPhoneNumber { get; }

    /// <summary>True when a QA code was configured and hashed at startup. When false, QA access is
    /// permanently off no matter what the runtime switch says (fail-safe for a missing secret).</summary>
    public bool IsConfigured => _authOptions.Value.IsQaTestAccessConfigured;

    /// <summary>The stored SHA-256 hash of the QA code, or null when unconfigured. Callers store
    /// this directly as a verification-code hash; the plaintext is never held.</summary>
    public string? CodeHash => _authOptions.Value.QaTestAccessCodeHash;

    /// <summary>Exact match against the seeded QA phone. <paramref name="normalizedPhone"/> must
    /// already be normalized (the callers normalize before calling).</summary>
    public bool IsQaPhone(string normalizedPhone) =>
        string.Equals(normalizedPhone, QaPhoneNumber, StringComparison.Ordinal);

    /// <summary>
    /// Whether QA sign-in may proceed right now: a code is configured AND the runtime switch is on.
    /// Fail-safe on every axis — if the switch cannot be read (table missing, DB unreachable,
    /// malformed value) this returns false and logs a warning, so a configuration fault disables QA
    /// access rather than opening it, and normal customer auth is unaffected.
    /// </summary>
    public async Task<bool> IsActiveAsync(CancellationToken cancellationToken = default)
    {
        if (!IsConfigured)
            return false;

        try
        {
            return await _settings.IsQaTestAccessEnabledAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Do NOT fail open. An unreadable setting means "off".
            _logger.LogWarning(ex, "QA test-access switch could not be read; treating QA access as disabled");
            return false;
        }
    }
}
