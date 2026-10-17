namespace FuelFlow.Features.Ledger.SharedModels;

/// <summary>
/// What kind of counterparty a <see cref="Party"/> is.
/// </summary>
public enum PartyType
{
    /// <summary>A person. One per user account.</summary>
    Natural,

    /// <summary>A company. A customer's, or a supplier's.</summary>
    Legal
}

/// <summary>
/// Any counterparty the business transacts with.
/// <para>
/// The reason this row exists: a journal entry needs a counterparty, and today there is no single
/// thing to point that foreign key at. A customer can be a person or a company, a supplier is always
/// a company, and <c>orders</c> carries both <c>user_id</c> and <c>legal_entity_id</c> with "which
/// one is authoritative" re-derived by every handler that reads it. A supertype makes one invoice to
/// a person and one invoice to a company the same code path instead of two.
/// </para>
/// <para>
/// <strong>This is the additive first step only.</strong> The documented sequence is insert, switch,
/// reconcile, then retire - so nothing here is read by anything yet, and the columns this will
/// eventually replace are all still in place. <c>display_name</c> is copied from the user at back-fill
/// time and is not kept in sync afterwards; treating it as a name is a later decision, because two
/// names for one person is exactly the ambiguity this refactor exists to remove.
/// </para>
/// </summary>
public sealed class Party
{
    public Guid Id { get; set; }
    public PartyType PartyType { get; set; }

    /// <summary>
    /// Printable name. NOT NULL because a journal line has to print something, and a document that
    /// can say "NULL" on a counterparty is not a document.
    /// </summary>
    public string DisplayName { get; set; } = null!;

    public DateTime CreatedAtUtc { get; set; }

    public NaturalPerson? NaturalPerson { get; set; }
}

/// <summary>
/// The natural-person subtype: the facts about a party that only make sense for a person.
/// <para>
/// Keyed by <c>party_id</c> rather than owning an identity of its own, so a party is a party
/// whichever subtype describes it - that is what lets the journal point at one table.
/// </para>
/// </summary>
public sealed class NaturalPerson
{
    public Guid PartyId { get; set; }
    public DateOnly? Birthdate { get; set; }

    /// <summary>ІПН. Nullable: most users never provide one, and a guessed tax id is worse than none.</summary>
    public string? TaxId { get; set; }

    public Party? Party { get; set; }
}