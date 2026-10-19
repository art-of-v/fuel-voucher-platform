namespace FuelFlow.Features.Ledger.SharedModels;

/// <summary>
/// One account in the chart. The chart's *content* is the accountant's decision and ships as data;
/// only this shape is the schema's.
/// <para>
/// <c>IsPostable</c> exists because a chart has headers - 1000 "Assets", with 1010 beneath it - and
/// a header is not something a journal line may name. Nothing in the schema stops a line naming one,
/// so the mapping layer is where that rule is enforced; saying so here is more honest than a
/// constraint that pretends to enforce it.
/// </para>
/// </summary>
public sealed class ChartOfAccount
{
    public Guid Id { get; set; }

    /// <summary>Account number as the accountant writes it, e.g. <c>7911</c>.</summary>
    public string Code { get; set; } = null!;

    public string NameUkr { get; set; } = null!;
    public AccountType AccountType { get; set; }
    public NormalBalance NormalBalance { get; set; }
    public Guid? ParentId { get; set; }
    public bool IsPostable { get; set; } = true;
    public bool IsActive { get; set; } = true;
}

public enum AccountType
{
    Asset,
    Liability,
    Equity,
    Revenue,
    Expense,
    Vat
}

public enum NormalBalance
{
    Debit,
    Credit
}

/// <summary>
/// A closed month. Periods are the immutability boundary: nothing in a closed period may change, and
/// that has to be true in the database, because "closed March" has to survive a hurried operator and
/// a <c>psql</c> session - not just a well-behaved handler.
/// </summary>
public sealed class AccountingPeriod
{
    public Guid Id { get; set; }
    public DateOnly PeriodStart { get; set; }
    public DateOnly PeriodEnd { get; set; }
    public PeriodStatus Status { get; set; } = PeriodStatus.Open;
    public DateTime? ClosedAtUtc { get; set; }
    public Guid? ClosedByUserId { get; set; }

    public ICollection<JournalEntry> Entries { get; set; } = [];
}

public enum PeriodStatus
{
    Open,
    Closed
}

/// <summary>What in the business caused an entry. The set is closed so the mapping can key on it.</summary>
public enum JournalSourceType
{
    Payment,
    Refund,
    Fulfillment,
    Cost,
    Movement,
    Adjustment,
    Fiscalization,
    Reversal
}

public enum JournalEntryStatus
{
    /// <summary>Being composed. May be transiently unbalanced - that is what the draft guard is for.</summary>
    Draft,

    /// <summary>Posted into a period. Immutable; reversing means posting the opposite entry.</summary>
    Posted,

    /// <summary>Countered by <see cref="ReversedByEntryId"/>. The original is kept, not deleted.</summary>
    Reversed
}

/// <summary>
/// One balanced posting. This is the artefact a bookkeeper signs off and a VAT return is built from;
/// money that is only a column on an operational table cannot appear in a trial balance and cannot be
/// carried between periods.
/// </summary>
public sealed class JournalEntry
{
    public Guid Id { get; set; }

    /// <summary>Sequence within the period. Allocated from <c>document_sequences</c>, not here.</summary>
    public long EntryNo { get; set; }

    public DateOnly EntryDate { get; set; }
    public Guid PeriodId { get; set; }
    public JournalSourceType SourceType { get; set; }

    /// <summary>
    /// The domain event that caused this entry, matching <c>ledger_mapping_rules.source_event</c>.
    /// Stored on the entry, not just inferred from the mapping, because one business fact can produce
    /// several entries - a sale posts revenue, then VAT, then cost of sales - and idempotency has to
    /// tell those apart. Keying on the source row alone would let the second entry be swallowed as a
    /// duplicate of the first.
    /// </summary>
    public string SourceEvent { get; set; } = null!;

    /// <summary>The operational row that caused this entry, where there is exactly one.</summary>
    public Guid? SourceId { get; set; }

    public string Description { get; set; } = null!;
    public JournalEntryStatus Status { get; set; } = JournalEntryStatus.Draft;
    public DateTime? PostedAtUtc { get; set; }
    public Guid? PostedByUserId { get; set; }
    public Guid? ReversedByEntryId { get; set; }

    /// <summary>SHA-256 over the entry's own content. A hash that anyone can recompute is not a signature.</summary>
    public string? TamperHash { get; set; }

    public DateTime CreatedAtUtc { get; set; }
    public Guid? CreatedByUserId { get; set; }

    public AccountingPeriod? Period { get; set; }
    public ICollection<JournalLine> Lines { get; set; } = [];
}

/// <summary>
/// One side of one posting. Every amount is UAH, per D-06; kopecks exist only on the wire.
/// </summary>
public sealed class JournalLine
{
    public Guid Id { get; set; }
    public Guid EntryId { get; set; }
    public short LineNo { get; set; }
    public Guid AccountId { get; set; }

    /// <summary>Counterparty, for the subledger. Null when the posting has no counterparty.</summary>
    public Guid? PartyId { get; set; }

    /// <summary>
    /// The offer this line is about. Text, not uuid, because <c>fuel_offers.id</c> is still a natural
    /// key; D-21 replaces both with surrogates and this becomes uuid in the same migration.
    /// </summary>
    public string? OfferId { get; set; }

    /// <summary>The company the line belongs to. Keyed on <c>id</c> until D-02 re-keys it to party_id.</summary>
    public Guid? LegalEntityId { get; set; }

    public Guid? VoucherId { get; set; }
    public decimal? QuantityLiters { get; set; }
    public decimal DebitUah { get; set; }
    public decimal CreditUah { get; set; }

    public JournalEntry? Entry { get; set; }
    public ChartOfAccount? Account { get; set; }
}

/// <summary>
/// Which account a domain event posts to. Data, not code, on purpose: the accountant will discover in
/// month three that surcharges belong somewhere else, and that should be a data edit rather than a
/// deploy.
/// </summary>
public sealed class LedgerMappingRule
{
    public Guid Id { get; set; }
    public JournalSourceType SourceType { get; set; }

    /// <summary>The domain event, e.g. <c>order.fulfilled</c>. Also the key the uniqueness hangs on.</summary>
    public string SourceEvent { get; set; } = null!;

    public Guid AccountId { get; set; }
    public NormalBalance Side { get; set; }

    public ChartOfAccount? Account { get; set; }
}