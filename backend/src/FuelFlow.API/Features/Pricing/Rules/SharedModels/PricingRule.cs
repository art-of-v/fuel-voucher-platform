using FuelFlow.Features.Contracts.SharedModels;
using FuelFlow.SharedKernel.Domain;

namespace FuelFlow.Features.Pricing.Rules.SharedModels;

/// <summary>What a rule controls. The set is closed; a CHECK pins it.</summary>
public static class PricingRuleKinds
{
    /// <summary>Profit per litre, in UAH. The margin the operator wants above cost.</summary>
    public const string ProfitPerLiter = "profit_per_liter";

    /// <summary>Floor on the discount per litre. A price below it is refused.</summary>
    public const string MinDiscountPerLiter = "min_discount_per_liter";

    /// <summary>Discount as a percentage of the list price.</summary>
    public const string DiscountPercent = "discount_percent";

    /// <summary>An absolute price per litre, overriding the computed one.</summary>
    public const string FixedPricePerLiter = "fixed_price_per_liter";

    /// <summary>Discount per litre for a voucher term, indexed by the contract or offer.</summary>
    public const string TermDiscountPerLiter = "term_discount_per_liter";

    public static readonly string[] All =
        [ProfitPerLiter, MinDiscountPerLiter, DiscountPercent, FixedPricePerLiter, TermDiscountPerLiter];

    public static bool IsKnown(string? value) => All.Contains(value);
}

/// <summary>What a rule applies to. Exactly one target, and which one is stated by the scope itself.</summary>
public static class PricingRuleScopes
{
    /// <summary>One offer: a (brand, fuel) pair and the stock sold against it.</summary>
    public const string Offer = "offer";

    /// <summary>One of our own legal entities. The operator's company, not a supplier.</summary>
    public const string LegalEntity = "legal_entity";

    /// <summary>One signed customer contract.</summary>
    public const string Contract = "contract";

    /// <summary>Everything. The fallback, and the only scope with no target column set.</summary>
    public const string Global = "global";

    public static readonly string[] All = [Offer, LegalEntity, Contract, Global];

    public static bool IsKnown(string? value) => All.Contains(value);
}

/// <summary>
/// Where a rule is in its life.
/// <para>
/// Only <see cref="Approved"/> affects a price. That is the whole point: today an operator edit
/// re-prices the catalogue instantly, with no review and no record of who did it. Here a change is
/// proposed, sits in draft, and only changes anything once a second person has approved it.
/// </para>
/// </summary>
public static class PricingRuleStatuses
{
    public const string Draft = "draft";
    public const string PendingApproval = "pending_approval";

    /// <summary>The only status that changes a price.</summary>
    public const string Approved = "approved";

    public const string Rejected = "rejected";

    /// <summary>Replaced by a later rule covering the same scope and kind.</summary>
    public const string Superseded = "superseded";

    public static readonly string[] All = [Draft, PendingApproval, Approved, Rejected, Superseded];

    public static bool IsKnown(string? value) => All.Contains(value);
}

/// <summary>
/// One pricing rule: what it controls, where it applies, from when, and whether anybody approved it.
/// </summary>
/// <remarks>
/// <para>
/// <b>This exists because pricing was in three places at once.</b> <c>app_settings</c> held
/// <c>VoucherTerm:Tier:{code}:DiscountPerLiter</c> as unversioned strings, <c>fuel_offers</c> held
/// <c>margin_percent</c> as a column, and the checkout code had a fallback ladder. Two consequences:
/// "which discount applied on 13 March" was unanswerable, and an operator mistake was silent —
/// changing one key re-priced the whole catalogue with no review and no record.
/// </para>
/// <para>
/// <b>Effective-dated, so history is answerable.</b> <see cref="ValidFrom"/> and
/// <see cref="ValidTo"/> are the whole reason this is a table and not a settings row: a rule that
/// started on 1 March and ended on 1 April is a fact the database keeps, rather than something the
/// audit reconstructs.
/// </para>
/// <para>
/// <b>Approval is the second person.</b> <see cref="ApprovedByUserId"/> exists so that "approved"
/// cannot mean one person clicked twice — a CHECK ties the status to the approver being present.
/// </para>
/// </remarks>
public sealed class PricingRule
{
    public Guid Id { get; set; }

    /// <summary>What this rule controls. One of <see cref="PricingRuleKinds"/>.</summary>
    public string RuleKind { get; set; } = null!;

    /// <summary>What it applies to. One of <see cref="PricingRuleScopes"/>, and it decides which target column may be set.</summary>
    public string Scope { get; set; } = null!;

    /// <summary>
    /// The offer this rule prices.
    /// <para>
    /// A <c>string</c>, not a <c>Guid</c>: <c>fuel_offers.id</c> is still a text key today, because
    /// D-21 (catalogue keys become UUID surrogates) has not landed. Typed to match the column rather
    /// than the target schema, because an FK that does not resolve is not a forward-compatible one —
    /// it is a table that cannot be created. Widening this to a Guid is part of D-21.
    /// </para>
    /// </summary>
    public string? OfferId { get; set; }

    /// <summary>
    /// One of our legal entities.
    /// <para>
    /// The design note for this table says <c>legal_entity_party_id</c>, referencing
    /// <c>legal_entities.party_id</c>. That column does not exist — D-02 step 3 has not landed — so this
    /// references <c>legal_entities(id)</c> instead. Same shape, and repointing it when the party
    /// column arrives is a one-line migration rather than a rewrite of every rule.
    /// </para>
    /// </summary>
    public Guid? LegalEntityId { get; set; }

    public Guid? ContractId { get; set; }

    /// <summary>The rule's number. Always in UAH per litre, whatever the kind — a percentage is stored as its percent.</summary>
    public decimal Value { get; set; }

    /// <summary>When this rule starts applying. A rule with no end is open-ended.</summary>
    public DateTime ValidFrom { get; set; }

    /// <summary>When it stops. NULL means still open.</summary>
    public DateTime? ValidTo { get; set; }

    /// <summary>One of <see cref="PricingRuleStatuses"/>. Only <c>approved</c> changes a price.</summary>
    public string Status { get; set; } = PricingRuleStatuses.Draft;

    /// <summary>Who asked for this. NOT NULL: a rule nobody asked for has no origin.</summary>
    public Guid RequestedByUserId { get; set; }

    public DateTime RequestedAtUtc { get; set; }

    /// <summary>Who approved it. NULL unless approved, and never the requester in practice.</summary>
    public Guid? ApprovedByUserId { get; set; }

    public DateTime? ApprovedAtUtc { get; set; }

    /// <summary>Why it was turned down. Only meaningful when rejected.</summary>
    public string? RejectionReason { get; set; }

    public FuelOffer? Offer { get; set; }
    public LegalEntity? LegalEntity { get; set; }
    public Contract? Contract { get; set; }

    /// <summary>Who asked for this rule.</summary>
    public User? RequestedBy { get; set; }

    /// <summary>Who approved it. Null unless approved.</summary>
    public User? ApprovedBy { get; set; }
}