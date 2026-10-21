namespace FuelFlow.Features.Pricing.ImportOkkoPumpPrices;

/// <summary>
/// One fuel's national pump price (колонка, UAH per liter) as published on <c>okko.ua/fuels</c>.
/// <paramref name="SiteFuelCode"/> is the site's own <c>fuel_code</c> ("A-95", "Pulls Diesel",
/// "SPBT"); it is mapped onto our catalog via
/// <see cref="Vouchers.Import.OkkoFuelClassifier.CategoryFromSiteFuelCode"/> rather than by display
/// name, because the label the site renders is localized while the code is not.
/// </summary>
/// <param name="FuelName">
/// The label the site rendered for this fuel, in the language the endpoint was called in (the
/// configured endpoint is the Ukrainian one). Nullable: pricing does not need it, so a payload without
/// it still prices. The catalogue import needs it, because a fuel it creates has to be named the way
/// the brand names it rather than the way an operator typed it once — and a code alone ("DP") is a
/// worse name for a customer than the one OKKO prints.
/// </param>
public sealed record OkkoPumpPrice(string SiteFuelCode, decimal PricePerLiter, string? FuelName = null);

/// <summary>What the import wrote (or would write, in a dry run) for one matched OKKO fuel type.</summary>
public sealed record OkkoPumpApplied(
    Guid FuelTypeId,
    string FuelName,
    string SiteFuelCode,
    decimal PumpPricePerLiter,
    decimal FinalPricePerLiter,
    decimal? PreviousPumpPricePerLiter,
    bool Changed,
    int PackagesRepriced);

/// <summary>
/// Outcome of importing a scraped OKKO price sheet into the catalog's pump field. Doubles as the
/// read-only preview an admin confirms before committing (<c>DryRun = true</c>) and the receipt
/// after it commits.
/// </summary>
public sealed record OkkoPumpApplyResult(
    int FuelsMatched,
    int FuelsChanged,
    int PackagesRepriced,
    IReadOnlyList<OkkoPumpApplied> Applied,
    IReadOnlyList<string> UnmatchedSiteFuels,
    IReadOnlyList<string> OkkoFuelsWithoutPrice,
    IReadOnlyList<OkkoPriceSheetError> Errors,
    bool DryRun);