namespace FuelFlow.Features.Brands;

/// <summary>
/// The stable codes a catalogue write is refused under, so the admin can say the reason in the
/// operator's language instead of printing the English sentence.
/// </summary>
/// <remarks>
/// One list for the network and the fuels under it: the brand screen edits both from one form, and
/// two lists drift.
/// </remarks>
internal static class BrandRefusalCodes
{
    public const string SlugRequired = "brand_slug_required";

    public const string SlugTaken = "brand_slug_taken";

    public const string BrandNotFound = "brand_not_found";

    public const string FuelNotFound = "fuel_not_found";

    public const string TestDataRequired = "fuel_test_data_required";

    public const string NameExists = "fuel_name_exists";

    public const string GradeExists = "fuel_grade_exists";

    public const string CodeTaken = "fuel_code_taken";

    public const string RehearsalName = "fuel_rehearsal_name";

    public const string PoolMismatch = "fuel_pool_mismatch";

    public const string LiveStockNeedsPrice = "fuel_live_stock_needs_price";

    public const string ArchiveBlocked = "fuel_archive_blocked";

    public const string NominalsInvalid = "fuel_nominals_invalid";

    public const string CostNotPositive = "fuel_cost_not_positive";

    public const string MarginNegative = "fuel_margin_negative";

    public const string BelowCostNeedsCost = "fuel_below_cost_needs_cost";

    /// <summary>
    /// The code for one of <see cref="FuelFlow.SharedKernel.Domain.FuelEconomics.Fault"/>'s
    /// refusals, keyed by the field it names. The numbers rule lives in the shared kernel; the
    /// spelling a client words it by belongs to the feature, so the mapping is here rather than
    /// beside the rule.
    /// </summary>
    public static string ForEconomicsFault(string field) => field switch
    {
        "SupplierPricePerLiter" => CostNotPositive,
        "MarginUahPerLiter" => MarginNegative,
        _ => BelowCostNeedsCost,
    };
}
