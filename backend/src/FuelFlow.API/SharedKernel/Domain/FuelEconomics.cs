namespace FuelFlow.SharedKernel.Domain;

/// <summary>
/// What a fuel's economics state is: whether the two numbers its price is made of are stated.
/// </summary>
/// <param name="HasCost">Whether the supplier cost per litre is stated and greater than zero.</param>
/// <param name="HasMargin">Whether the margin per litre is stated.</param>
public readonly record struct FuelEconomicsState(bool HasCost, bool HasMargin)
{
    /// <summary>
    /// Whether the fuel has a price of its own.
    /// </summary>
    /// <remarks>
    /// A margin of zero is a stated margin: the fuel is sold at cost, which is a decision and not a gap.
    /// A cost of zero is not a stated cost - see <see cref="FuelEconomics.State"/>.
    /// </remarks>
    public bool IsPriced => HasCost && HasMargin;

    /// <summary>
    /// What is missing, as a stable machine token, or <c>null</c> when nothing is.
    /// </summary>
    /// <remarks>
    /// <see cref="Missing"/> is the English words for a log line; a client cannot build a translated
    /// sentence out of them, because the words are the thing each locale has its own of. So the gap
    /// travels as this token and the admin says it in the operator's language.
    /// </remarks>
    public string? Gap => (HasCost, HasMargin) switch
    {
        (false, false) => "cost_and_margin",
        (false, true) => "cost",
        (true, false) => "margin",
        _ => null
    };
}

/// <summary>
/// A fuel that has no price of its own: its name, and which half of the price is missing.
/// </summary>
/// <param name="Name">The fuel's own name, as the operator reads it on the fuel card.</param>
/// <param name="Gap">What is missing — see <see cref="FuelEconomicsState.Gap"/>.</param>
public sealed record UnpricedFuel(string Name, string Gap);

/// <summary>
/// The economics of a fuel: the supplier cost we pay per litre and the margin we add to it.
/// </summary>
/// <remarks>
/// <para>
/// The price of a litre is what we paid plus our margin (<see cref="FuelPricing.FinalPerLiter"/>),
/// so a fuel without both has no price to sell at. <see cref="FuelPricing.FinalPerLiterOrNull"/>
/// already answers null for that case and checkout refuses the sale
/// (<c>UnknownCostSaleBlockedException</c>) - but a refusal at checkout is a refusal the customer
/// waited for, on a shelf that said the fuel was there.
/// </para>
/// <para>
/// So the same question is asked earlier, where the answer can still be acted on: before stock goes
/// on the shelf, and before an operator writes the economics of a fuel that live stock already sits
/// on. One rule, asked in both places, so the two cannot drift.
/// </para>
/// </remarks>
public static class FuelEconomics
{
    /// <summary>
    /// What the pair states.
    /// </summary>
    /// <remarks>
    /// A cost of zero is "not stated", not "free": a litre we paid nothing for has no cost to guard a
    /// sale against, and the below-cost guard reads a zero cost as unknown cost too. The margin is
    /// different - zero there is a real decision, and the database agrees
    /// (<c>ck_fuel_types_margin_signs</c> pins it at zero or more, not at more than zero).
    /// </remarks>
    public static FuelEconomicsState State(decimal? costPerLiter, decimal? marginPerLiter)
        => new(
            HasCost: costPerLiter is { } cost && cost > 0m,
            HasMargin: marginPerLiter is not null);

    /// <summary>
    /// What is missing, in the words an English reader of the logs expects.
    /// </summary>
    /// <remarks>
    /// Derived from <see cref="FuelEconomicsState.Gap"/>, so the sentence and the token cannot
    /// disagree about which half of the price is absent.
    /// </remarks>
    public static string Missing(FuelEconomicsState state)
        => state.Gap switch
        {
            "cost_and_margin" => "no supplier cost and no margin",
            "cost" => "no supplier cost",
            "margin" => "no margin",
            _ => "nothing"
        };

    /// <summary>
    /// The fuels of these that have no price of their own, as the machine-readable pair the
    /// refusal is built from: the name and the gap.
    /// </summary>
    public static List<UnpricedFuel> Unpriced(
        IEnumerable<(string Name, FuelEconomicsState State)> fuels)
        => fuels.Where(f => !f.State.IsPriced)
            .Select(f => new UnpricedFuel(f.Name, f.State.Gap!))
            .ToList();

    /// <summary>
    /// What is wrong with the numbers an operator sent, or null when they are sound.
    /// </summary>
    /// <remarks>
    /// The field name is the request's own property name, so the admin screen can point at the box
    /// that has to change instead of showing a sentence about a column the operator cannot see.
    /// </remarks>
    public static (string Field, string Error)? Fault(
        decimal? costPerLiter,
        decimal? marginPerLiter,
        bool allowBelowCost)
    {
        if (costPerLiter is { } cost && cost <= 0m)
        {
            return (
                "SupplierPricePerLiter",
                "The supplier cost must be greater than zero - a cost of zero is not a cost we can guard a sale against.");
        }

        if (marginPerLiter is { } margin && margin < 0m)
        {
            return (
                "MarginUahPerLiter",
                "The margin cannot be negative - the fuel would be sold for less than we paid.");
        }

        if (allowBelowCost && costPerLiter is null)
        {
            return (
                "AllowBelowCost",
                "allowBelowCost needs a cost: the opt-in says a sale may go below what we paid, so there has to be a what-we-paid to go below.");
        }

        return null;
    }

    /// <summary>
    /// The refusal an operator reads when stock cannot go on the shelf.
    /// </summary>
    /// <remarks>
    /// The refusal names the fuels and what each of them is missing, because the operator's next two
    /// actions are different for each gap: the cost arrives with the batch, the margin is set on the
    /// fuel's card. A message that said only "cannot activate" would leave the operator to guess.
    /// </remarks>
    public static string ActivationRefusal(IEnumerable<(string Name, FuelEconomicsState State)> fuels)
    {
        var gaps = fuels.Where(f => !f.State.IsPriced).ToList();
        if (gaps.Count == 0)
        {
            return string.Empty;
        }

        var named = string.Join(", ", gaps.Take(5).Select(f => $"{f.Name} ({Missing(f.State)})"));
        var rest = gaps.Count > 5 ? $", and {gaps.Count - 5} more" : string.Empty;

        return $"Cannot activate - a fuel without a price of its own cannot go on sale: {named}{rest}. "
            + "The price of a litre is what we pay plus our margin: set the cost on the import batch "
            + "(the rate the supplier charges) and the margin on the fuel's card, then activate.";
    }
}
