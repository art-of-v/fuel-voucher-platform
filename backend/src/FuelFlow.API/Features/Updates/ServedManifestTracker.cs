using System.Globalization;
using System.Text.Json;
using FuelFlow.Features.Settings;
using FuelFlow.SharedKernel.Observability;

namespace FuelFlow.Features.Updates;

/// <summary>
/// Remembers which published manifest each OTA channel is serving and notices when the channel
/// starts serving an older one (F-13, planning #681).
/// <para>
/// The publishing side refuses a rollback (<c>mobile/scripts/publish-ota.mjs</c>), but a refusal
/// only covers the path through that script. Anything else that writes the bucket - a console
/// session, a repaired object, the script run with <c>--allow-downgrade</c> - moves the whole
/// channel, and the client cannot tell: an older bundle carries a signature that verifies exactly
/// like a newer one. This is the half that speaks from the serving side, where every install
/// actually gets its update.
/// </para>
/// <para>
/// The per-channel record lives in <c>app_settings</c> under
/// <c>Updates:LastServed:{platform}/{runtimeVersion}</c>. Rows are written only when the manifest
/// id changes, which is once per publish per runtime - a request that finds the manifest it already
/// knows does one indexed read and stops.
/// </para>
/// </summary>
public sealed class ServedManifestTracker
{
    public const string SettingKeyPrefix = "Updates:LastServed:";

    // app_settings.key is varchar(100). Platform is ios|android in practice, but the header is
    // client-supplied and anonymous, so both segments are bounded rather than trusted: a key that
    // cannot fit is skipped instead of failing the insert. This also keeps an anonymous caller
    // from creating a row per arbitrary runtime version - only a runtime with a published manifest
    // ever reaches this code, and only one inside these bounds is recorded.
    private const int MaxPlatformLength = 16;
    private const int MaxRuntimeVersionLength = 48;

    private static readonly JsonSerializerOptions StampJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    private readonly RuntimeSettingsService _settings;
    private readonly FuelFlowMetrics _metrics;
    private readonly ILogger<ServedManifestTracker> _logger;

    public ServedManifestTracker(
        RuntimeSettingsService settings,
        FuelFlowMetrics metrics,
        ILogger<ServedManifestTracker> logger)
    {
        _settings = settings;
        _metrics = metrics;
        _logger = logger;
    }

    public static string SettingKey(string platform, string runtimeVersion) =>
        $"{SettingKeyPrefix}{platform}/{runtimeVersion}";

    /// <summary>
    /// Best-effort: an audit that fails must never cost a client its update, which is why the
    /// whole body is wrapped. Nothing here can change what the endpoint returns.
    /// </summary>
    public async Task ObserveAsync(
        string platform,
        string runtimeVersion,
        byte[] manifestBytes,
        CancellationToken cancellationToken)
    {
        if (platform.Length is 0 or > MaxPlatformLength
            || runtimeVersion.Length is 0 or > MaxRuntimeVersionLength)
        {
            _logger.LogDebug(
                "Not recording the served manifest for {Platform}/{RuntimeVersion}: outside the bounds a setting key may hold.",
                platform, runtimeVersion);
            return;
        }

        try
        {
            await ObserveCoreAsync(platform, runtimeVersion, manifestBytes, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // The update has already been fetched and is about to be relayed either way. Losing
            // the bookkeeping is a gap in the alert, not a reason to fail the download.
            _logger.LogWarning(ex,
                "Could not record the manifest served for {Platform}/{RuntimeVersion}; the update itself is unaffected.",
                platform, runtimeVersion);
        }
    }

    private async Task ObserveCoreAsync(
        string platform,
        string runtimeVersion,
        byte[] manifestBytes,
        CancellationToken cancellationToken)
    {
        if (!TryReadStamp(manifestBytes, out var current))
        {
            _logger.LogDebug(
                "Served manifest for {Platform}/{RuntimeVersion} is not a JSON object with an id; not recording it.",
                platform, runtimeVersion);
            return;
        }

        // A manifest published before the build stamp existed says nothing about which commit it
        // came from. Recording it would either invent a baseline or erase a real one.
        if (current.CommittedAt is null)
            return;

        var key = SettingKey(platform, runtimeVersion);
        var previous = Parse(await _settings.GetValueAsync(key, cancellationToken));

        // Same bundle: the common case on every app launch, and it must not write.
        if (previous is not null && previous.Id == current.Id)
            return;

        if (previous?.CommittedAt is { } previousCommittedAt && current.CommittedAt < previousCommittedAt)
        {
            _logger.LogWarning(
                "OTA manifest for {Platform}/{RuntimeVersion} went BACKWARD: now serving commit {Commit} committed {CommittedAt}, previously {PreviousCommit} committed {PreviousCommittedAt}. "
                + "A publish replaced the channel with an older tree - deliberate (--allow-downgrade) or not.",
                platform, runtimeVersion,
                current.Commit ?? "(unrecorded)", current.CommittedAt.Value,
                previous.Commit ?? "(unrecorded)", previousCommittedAt);
            _metrics.UpdateManifestRollback(platform);
        }

        await _settings.UpsertAsync(
            key,
            JsonSerializer.Serialize(current, StampJson),
            cancellationToken: cancellationToken);
    }

    private static ManifestStamp? Parse(string? stored)
    {
        if (string.IsNullOrWhiteSpace(stored))
            return null;

        try
        {
            return JsonSerializer.Deserialize<ManifestStamp>(stored, StampJson);
        }
        catch (JsonException)
        {
            // A row nothing can read is not a baseline; the next observation replaces it.
            return null;
        }
    }

    /// <summary>
    /// Reads the signed <c>metadata</c> the publisher stamps into every manifest. Tolerant on
    /// purpose: this sees whatever the bucket holds, including objects written before the stamp
    /// existed, and must never be the reason a valid manifest fails to reach a device.
    /// </summary>
    internal static bool TryReadStamp(byte[] manifestBytes, out ManifestStamp stamp)
    {
        stamp = null!;

        try
        {
            using var document = JsonDocument.Parse(manifestBytes);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return false;

            var id = root.TryGetProperty("id", out var idElement) && idElement.ValueKind == JsonValueKind.String
                ? idElement.GetString()
                : null;
            if (string.IsNullOrWhiteSpace(id))
                return false;

            string? commit = null;
            DateTimeOffset? committedAt = null;

            if (root.TryGetProperty("metadata", out var metadata) && metadata.ValueKind == JsonValueKind.Object)
            {
                if (metadata.TryGetProperty("commit", out var commitElement)
                    && commitElement.ValueKind == JsonValueKind.String)
                {
                    var value = commitElement.GetString();
                    commit = string.IsNullOrWhiteSpace(value) ? null : value;
                }

                if (metadata.TryGetProperty("committedAt", out var atElement)
                    && atElement.ValueKind == JsonValueKind.String
                    && DateTimeOffset.TryParse(
                        atElement.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
                {
                    committedAt = parsed;
                }
            }

            stamp = new ManifestStamp(id, commit, committedAt);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}

/// <summary>
/// What a served manifest says about itself: the update id, and the commit and commit time the
/// publisher signed into <c>metadata</c>. Stored as-is in <c>app_settings</c>.
/// </summary>
internal sealed record ManifestStamp(string Id, string? Commit, DateTimeOffset? CommittedAt);
