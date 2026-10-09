namespace FuelFlow.SharedKernel.Options;

/// <summary>
/// Settings for the recurring OKKO pump-price (колонка) fetch.
/// <para>
/// Separate from the rest of the pricing configuration because it is the only pricing input that
/// comes from outside: OKKO publishes its own retail price list, and this decides whether we track it
/// automatically or only when an operator uploads a file.
/// </para>
/// </summary>
public sealed class OkkoPriceOptions
{
    public const string SectionName = "OkkoPrice";

    /// <summary>
    /// Whether the recurring sync job is allowed to fetch and write prices.
    /// <para>
    /// Off by default so a fresh deployment cannot start moving prices before anyone has decided it
    /// should: a price change here is unattended, and the prices it writes feed the customer-facing
    /// catalogue and the struck "до" price. Turn it on once the catalogue is set up.
    /// </para>
    /// </summary>
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// OKKO's public page-API endpoint. The prices live in the JSON this returns; the rendered HTML
    /// carries no prices at all outside a JavaScript state blob, which is why the scraper reads the
    /// API rather than the page. Note the locale segment is part of the path, not a query parameter.
    /// </summary>
    public string EndpointUrl { get; set; } = "https://www.okko.ua/api/uk/fuels";

    /// <summary>Abort the fetch after this long so a hung connection cannot occupy the job slot.</summary>
    public int TimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// Identifies us to OKKO. Set to a contactable address so a human can see who is polling.
    /// </summary>
    public string UserAgent { get; set; } = "FuelFlow price sync (https://github.com/art-of-v/fuel-voucher-platform)";
}