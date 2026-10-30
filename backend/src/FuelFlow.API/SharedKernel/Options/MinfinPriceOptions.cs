namespace FuelFlow.SharedKernel.Options;

/// <summary>
/// Settings for the recurring Minfin price read.
/// </summary>
/// <remarks>
/// <para>
/// Minfin publishes the average retail price of every fuel network. It is the price source for two
/// of ours: KLO, which publishes none of its own, and WOG, whose page answers this host with an
/// empty <c>204</c> (2026-10-29). The grades it lists are the generic ones ("А 95+", "А 95", "А 92",
/// "ДП", "Газ"), which is how an operator names a KLO fuel too, so a KLO grade read from here
/// matches a fuel in the catalogue without a second table of correspondences. WOG names its grades
/// its own way, and <c>MinfinNetwork</c> is where those five columns are written down against the
/// fuels they price.
/// </para>
/// <para>
/// Off by default, like the other fetches: a fresh deployment must not start writing prices before
/// anyone has decided it should.
/// </para>
/// </remarks>
public sealed class MinfinPriceOptions
{
    public const string SectionName = "MinfinPrice";

    /// <summary>
    /// Whether the recurring sync job is allowed to fetch and write prices.
    /// </summary>
    public bool Enabled { get; set; } = false;

    /// <summary>Minfin's fuel price table.</summary>
    public string EndpointUrl { get; set; } = "https://index.minfin.com.ua/ua/markets/fuel/tm/";

    /// <summary>Abort the fetch after this long so a hung connection cannot occupy the job slot.</summary>
    public const int DefaultTimeoutSeconds = 30;

    public int TimeoutSeconds { get; set; } = DefaultTimeoutSeconds;

    /// <summary>Identifies us to the publisher. Set to a contactable address so a human can see who is polling.</summary>
    public string UserAgent { get; set; } = "FuelFlow price sync (https://github.com/art-of-v/fuel-voucher-platform)";
}