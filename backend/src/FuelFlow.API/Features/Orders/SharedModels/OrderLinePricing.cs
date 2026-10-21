using System.Text.Json;
using System.Text.Json.Serialization;
using FuelFlow.SharedKernel.Domain;

namespace FuelFlow.Features.Orders.SharedModels;

/// <summary>
/// Freezes the inputs that produced a line's price, so the line can explain its own number long
/// after the terms that priced it have changed.
/// </summary>
/// <remarks>
/// <see cref="OrderLineItem.UnitPrice"/> is the answer; this is the question behind it. Without it,
/// "why was this order 89.06?" is answerable only by whoever happened to be looking at checkout at
/// the time - and contracts change, so today's contract is not the one that priced a past order and
/// cannot be used to reconstruct it.
/// <para>
/// Written at checkout and never updated. A row that already exists with a null basis stays null:
/// backfilling from current configuration would manufacture a history that never happened, and a
/// fabricated basis is worse than an honest gap because it is indistinguishable from a real one.
/// </para>
/// </remarks>
public static class OrderLinePricing
{
    /// <summary>
    /// The JSON shape stored on <see cref="OrderLineItem.PricingBasis"/>. Property names are
    /// camelCase on purpose - it is read by people and by ad-hoc queries during a dispute, not only
    /// by this codebase.
    /// </summary>
    private sealed record Basis(
        decimal? SupplierPricePerLiter,
        decimal? MarginUahPerLiter,
        decimal? PumpPricePerLiter,
        decimal? DiscountPerLiter,
        string? TermCode);

    private static readonly JsonSerializerOptions Options = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>
    /// Freezes the package's pricing columns plus the term ladder's resolved discount.
    /// </summary>
    /// <remarks>
    /// The supplier cost is included deliberately even though it never leaves the admin screens
    /// through the API: a pricing dispute is exactly the moment someone needs to know the margin,
    /// and reconstructing it later from a changed cost column would not be reconstruction, it would
    /// be a guess.
    /// </remarks>
    public static string? For(SellablePackage package, string? termCode, decimal? discountPerLiter = null)
    {
        ArgumentNullException.ThrowIfNull(package);

        return JsonSerializer.Serialize(
            new Basis(
                package.SupplierPricePerLiter,
                package.MarginUahPerLiter,
                package.PumpPricePerLiter,
                discountPerLiter,
                termCode),
            Options);
    }
}