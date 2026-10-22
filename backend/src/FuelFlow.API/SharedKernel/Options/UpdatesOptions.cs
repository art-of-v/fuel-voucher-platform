namespace FuelFlow.SharedKernel.Options;

/// <summary>
/// Configuration for the self-hosted Expo Updates (OTA) manifest endpoint. See
/// <see cref="FuelFlow.Features.Updates.UpdatesController"/> and planning issue #42.
/// </summary>
public sealed class UpdatesOptions
{
    public const string SectionName = "Updates";

    /// <summary>
    /// Public, CDN-fronted base URL of the R2 bucket that holds the pre-signed update descriptors and
    /// their assets (for example "https://ota.palne.shop"); a trailing slash is tolerated. Left blank —
    /// the default — the manifest endpoint answers 204 (no update), so the whole feature stays inert
    /// until the bucket exists and the deploy supplies OTA_PUBLIC_BASE_URL. This is the fail-safe
    /// default: an unconfigured server never breaks an installed app, it just offers no update.
    /// </summary>
    public string PublicBaseUrl { get; set; } = string.Empty;

    /// <summary>
    /// The deadline the CDN is given. An installed app asking "is there an update?" waits on
    /// this, so a CDN that never answers is a 502 on the update check.
    /// </summary>
    public const int DefaultTimeoutSeconds = 15;

    /// <summary>
    /// How long the CDN is given to answer. Zero or less means the default.
    /// </summary>
    public int TimeoutSeconds { get; set; } = DefaultTimeoutSeconds;
}
