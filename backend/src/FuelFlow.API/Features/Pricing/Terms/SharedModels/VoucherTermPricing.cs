using FuelFlow.Features.Vouchers.Renewal;

namespace FuelFlow.Features.Pricing.Terms.SharedModels;

/// <summary>Which ladder a row belongs to. The set is closed; a CHECK pins it.</summary>
/// <remarks>
/// The two ladders are not two shapes of the same thing, which is why they live in one table with a
/// discriminator rather than being forced into one meaning. A renewal tier is an **absolute price per
/// litre** the customer pays to extend a voucher; a purchase-term tier is a **discount subtracted
/// from** the computed cost-plus-margin price. Collapsing them into one "rate" column would make the
/// sign and the direction of every number a guess, and a wrong guess here is a wrong charge.
/// </remarks>
public static class VoucherTermPricingSchemes
{
    /// <summary>Renewal: what a litre costs to extend. An absolute price, not a discount.</summary>
    public const string RenewalRate = "renewal_rate";

    /// <summary>Purchase term: what a litre is taken off for committing to a longer term.</summary>
    public const string PurchaseTermDiscount = "purchase_term_discount";

    public static readonly string[] All = [RenewalRate, PurchaseTermDiscount];

    public static bool IsKnown(string? value) => All.Contains(value);
}

/// <summary>
/// One step of a term ladder, on a date range.
/// </summary>
/// <remarks>
/// <para>
/// <b>This is a lookup, not a rule.</b> Every mainstream pricing system - Stripe and Adyen price
/// books, Salesforce CPQ condition records, AWS tier tables - keeps its base ladder in a catalogue of
/// tiers and reserves "rules" for adjustments on top. A tier is an attribute of what is being sold;
/// a rule is a claim about how to change it. Mixing the two is what D-09 originally proposed, and it
/// does not fit: a ladder needs a term and an on/off flag, neither of which is a rule target or a
/// single number.
/// </para>
/// <para>
/// <b>Effective-dated, which is the actual gain.</b> The rows this replaces were key/value strings:
/// changing a rate overwrote the only record of what it had been, so "what did a renewal cost on
/// 13 March" was unanswerable and an operator mistake was invisible. Here a change closes the open
/// row and opens a new one, so the history is kept rather than reconstructed.
/// </para>
/// <para>
/// <b>One open row per (scheme, term).</b> A partial unique index enforces it, so two current prices
/// for the same tier cannot both exist and silently disagree.
/// </para>
/// </remarks>
public sealed class VoucherTermPricing
{
    public Guid Id { get; set; }

    /// <summary>Which ladder. One of <see cref="VoucherTermPricingSchemes"/>.</summary>
    public string Scheme { get; set; } = null!;

    /// <summary>
    /// The purchase term this row prices, from <see cref="VoucherRenewalTerms.Code"/> - "1w", "2m"
    /// and so on. A code rather than a duration, so the ladder stays aligned with the term the
    /// customer actually chose and cannot drift from <see cref="VoucherRenewalTerm.ApplyTo"/>.
    /// </summary>
    public string TermCode { get; set; } = null!;

    /// <summary>
    /// Whether the operator put this tier on sale. Never acts alone: both tiers compute
    /// <c>IsOfferable</c> as enabled AND a positive rate, so a row left on at zero sells nothing.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// The number, in UAH per litre, and what it means depends on <see cref="Scheme"/>: an absolute
    /// price for a renewal, a subtraction for a purchase term. Four decimals to match the other
    /// per-litre rate columns.
    /// </summary>
    public decimal RatePerLiter { get; set; }

    /// <summary>When this row started applying.</summary>
    public DateTime ValidFrom { get; set; }

    /// <summary>When it stopped. NULL means this is the current row - and only one may be open.</summary>
    public DateTime? ValidTo { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    /// <summary>
    /// Who closed the previous row and opened this one. Null on the rows the migration creates,
    /// because nobody opened them - an operator opened the corresponding settings row instead. That
    /// is the honest value; a made-up system user would be a lie in the audit column.
    /// </summary>
    public Guid? UpdatedByUserId { get; set; }

    public string? UpdatedByUserName { get; set; }

    public DateTime? UpdatedAtUtc { get; set; }
}