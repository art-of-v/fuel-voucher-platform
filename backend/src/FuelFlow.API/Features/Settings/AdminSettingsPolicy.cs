namespace FuelFlow.Features.Settings;

/// <summary>
/// The policy the admin settings screen reports back, so the numbers on it cannot drift from the
/// rules the server applies.
/// </summary>
public static class AdminSettingsPolicy
{
    /// <summary>
    /// The margin a term discount must leave behind, in UAH per litre.
    /// </summary>
    /// <remarks>
    /// A policy rather than a cost rule: the below-cost refusal already stops a losing sale, but a sale that
    /// earns 10 kopecks is not worth the support it generates. Expressed in UAH per litre because that is the
    /// unit the ladder is priced in.
    /// </remarks>
    public const decimal TermMarginFloorUah = 0.5m;
}
