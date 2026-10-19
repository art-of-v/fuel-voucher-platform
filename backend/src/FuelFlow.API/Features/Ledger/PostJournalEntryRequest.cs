using FuelFlow.Features.Ledger.SharedModels;

namespace FuelFlow.Features.Ledger;

/// <summary>
/// One business fact that has to appear in the books. The caller names the event and the amount; it
/// does not name an account, and it cannot - which is the point. Accounts come from
/// <see cref="LedgerMappingRule"/>, so the day the accountant decides that surcharges belong on a
/// different account, that is a data edit and not a deploy.
/// </summary>
public sealed record PostJournalEntryRequest
{
    /// <summary>The domain event, matching <c>ledger_mapping_rules.source_event</c>, e.g. <c>payment.captured</c>.</summary>
    public required string SourceEvent { get; init; }

    public required JournalSourceType SourceType { get; init; }

    /// <summary>The operational row that caused this entry. Required: an entry with no source is an
    /// entry nobody can trace back, and idempotency hangs off it.</summary>
    public required Guid SourceId { get; init; }

    public required DateOnly EntryDate { get; init; }

    public required decimal AmountUah { get; init; }

    public required string Description { get; init; }

    public Guid? PartyId { get; init; }
    public Guid? LegalEntityId { get; init; }
    public Guid? VoucherId { get; init; }
    public string? OfferId { get; init; }
    public decimal? QuantityLiters { get; init; }

    /// <summary>Who stands behind the posting. Required by ck_journal_entries_posted_shape.</summary>
    public required Guid PostedByUserId { get; init; }
}

public sealed class LedgerPostingException : Exception
{
    public const string Code = "ledger_posting_failed";

    public LedgerPostingException(string message) : base(message)
    {
    }
}

/// <summary>
/// The event has no mapping, or only half of one. Deliberately loud: an event nobody mapped is a
/// hole in the books, and quietly dropping it is how a trial balance ends up short by an amount
/// nobody can explain.
/// </summary>
public sealed class LedgerUnmappedEventException : Exception
{
    public const string Code = "ledger_event_unmapped";

    public LedgerUnmappedEventException(string sourceEvent, string missingSide)
        : base($"No {missingSide} account is mapped for '{sourceEvent}'. The event cannot be posted.")
    {
        SourceEvent = sourceEvent;
    }

    public string SourceEvent { get; }
}

/// <summary>
/// The period holding <c>EntryDate</c> is closed. Closed means closed: the fix is a reversing entry
/// in an open period, not an edit to this one.
/// </summary>
public sealed class LedgerPeriodClosedException : Exception
{
    public const string Code = "ledger_period_closed";

    public LedgerPeriodClosedException(DateOnly entryDate)
        : base($"The accounting period containing {entryDate:yyyy-MM-dd} is closed.")
    {
    }
}