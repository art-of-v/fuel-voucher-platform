namespace FuelFlow.SharedKernel.Options;

/// <summary>
/// Settings for the recurring UPG and WOG pump-price fetch.
/// </summary>
/// <remarks>
/// <para>
/// Separate from the OKKO settings because the two vendors are separate questions: OKKO publishes a
/// JSON API and is fetched every fifteen minutes; UPG and WOG publish a page that has to be read,
/// and each of them has changed shape while the importer was being written.
/// </para>
/// <para>
/// Off by default, like the OKKO fetch: a fresh deployment must not start moving prices before
/// anyone has decided it should. Turn it on once the catalogue is set up.
/// </para>
/// </remarks>
public sealed class UpgWogPriceOptions
{
    public const string SectionName = "UpgWogPrice";

    /// <summary>
    /// Whether the recurring sync job is allowed to fetch and write prices.
    /// </summary>
    public bool Enabled { get; set; } = false;

    /// <summary>UPG's price page.</summary>
    public string UpgEndpointUrl { get; set; } = "https://upg.ua/";

    /// <summary>
    /// WOG's price page, on the host the site calls canonical.
    /// </summary>
    /// <remarks>
    /// Not <c>www.wog.ua</c>: that host only answers <c>301</c> to the apex, so every fetch paid for
    /// a redirect before it could read anything - and on 2026-10-29 that hop was where the sync
    /// stopped, with the redirect target answering <c>204</c> and an empty body to the production
    /// host while the same page answered <c>200</c> everywhere else. The canonical URL is the one
    /// the site itself publishes, so it is the one that is least likely to change next.
    /// </remarks>
    public string WogEndpointUrl { get; set; } = "https://wog.ua/ua/fuels/";

    /// <summary>Abort the fetch after this long so a hung connection cannot occupy the job slot.</summary>
    public const int DefaultTimeoutSeconds = 30;

    public int TimeoutSeconds { get; set; } = DefaultTimeoutSeconds;

    /// <summary>Identifies us to the vendor. Set to a contactable address so a human can see who is polling.</summary>
    public string UserAgent { get; set; } = "FuelFlow price sync (https://github.com/art-of-v/fuel-voucher-platform)";

    /// <summary>
    /// The page to read for a brand's slug, or <c>null</c> when that brand is not configured.
    /// </summary>
    public string? EndpointFor(string brand) => brand.Trim().ToLowerInvariant() switch
    {
        "upg" => UpgEndpointUrl,
        "wog" => WogEndpointUrl,
        _ => null,
    };
}