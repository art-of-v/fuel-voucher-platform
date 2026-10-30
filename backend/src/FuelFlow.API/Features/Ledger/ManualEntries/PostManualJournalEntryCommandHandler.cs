using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Database;

namespace FuelFlow.Features.Ledger.ManualEntries;

/// <summary>
/// A journal entry a person composed: a date, a reason, a reference that names it, and balanced legs
/// by account code.
/// </summary>
/// <remarks>
/// <para>
/// The accounts are named, unlike every other posting in this system, because a human made the
/// decision and the accounts are the decision. What is <em>not</em> left to the caller: the entry
/// number (from the period's sequence), the period (from the date, and a closed one is refused), the
/// balance (checked here and by the database), and the operator (from the authenticated session, not
/// from the body).
/// </para>
/// <para>
/// <see cref="Reference"/> is the idempotency key: posting the same reference twice returns the entry
/// the first call wrote. It is also the only handle a retried request has, so a client that cannot
/// remember it has no safe retry - that is why it is required rather than generated.
/// </para>
/// </remarks>
public sealed record PostManualJournalEntryRequest
{
    /// <summary>The accounting date: which period the entry lands in, not when it was typed.</summary>
    public required DateOnly EntryDate { get; init; }

    /// <summary>What names this entry for a retry, e.g. <c>close-2026-10-d3</c>. Case-sensitive.</summary>
    public required string Reference { get; init; }

    /// <summary>Why the entry exists, in the accountant's words. This is what the journal shows.</summary>
    public required string Description { get; init; }

    /// <summary>At least two legs, debits equal to credits, each leg exactly one side.</summary>
    public required IReadOnlyList<ManualJournalLineRequest> Lines { get; init; }
}

/// <summary>One side of a manual entry. Exactly one of the two amounts is non-zero.</summary>
public sealed record ManualJournalLineRequest
{
    /// <summary>The account code as the chart writes it, e.g. <c>704</c>.</summary>
    public required string AccountCode { get; init; }

    public decimal DebitUah { get; init; }
    public decimal CreditUah { get; init; }

    // Optional subledger fields, the same ones an event posting carries on both legs.
    public Guid? PartyId { get; init; }
    public Guid? LegalEntityId { get; init; }
    public Guid? VoucherId { get; init; }
    public decimal? QuantityLiters { get; init; }
}

/// <summary>
/// What the caller gets back: the entry's number, and the totals that prove it is the entry they
/// asked for - including on a replay, where <see cref="PostedAtUtc"/> is the first posting's moment.
/// </summary>
public sealed record PostManualJournalEntryResponse(
    Guid EntryId,
    long EntryNo,
    DateOnly EntryDate,
    string Reference,
    string Description,
    decimal TotalUah,
    int LineCount,
    Guid PostedByUserId,
    DateTime PostedAtUtc);

/// <summary>
/// Posts a manual adjustment in one unit of work.
/// </summary>
/// <remarks>
/// The transaction is not decoration: <see cref="JournalPostingService.PostManualAsync"/> writes the
/// draft and then posts it, and a crash between the two would leave a draft nobody asked for. It is
/// also replay-safe - the execution strategy may run the body again, and the second run finds the
/// entry by its reference rather than posting a second one.
/// </remarks>
public sealed class PostManualJournalEntryCommandHandler
{
    private readonly ApplicationDbContext _context;
    private readonly JournalPostingService _journal;

    public PostManualJournalEntryCommandHandler(
        ApplicationDbContext context,
        JournalPostingService journal)
    {
        _context = context;
        _journal = journal;
    }

    public Task<PostManualJournalEntryResponse> HandleAsync(
        PostManualJournalEntryRequest request,
        Guid postedByUserId,
        CancellationToken cancellationToken = default)
        => _context.ExecuteReplaySafeAsync(
            async ct =>
            {
                var entry = await _journal.PostManualAsync(request, postedByUserId, ct);

                return new PostManualJournalEntryResponse(
                    entry.Id,
                    entry.EntryNo,
                    entry.EntryDate,
                    request.Reference.Trim(),
                    entry.Description,
                    entry.Lines.Sum(l => l.DebitUah),
                    entry.Lines.Count,
                    entry.PostedByUserId ?? postedByUserId,
                    entry.PostedAtUtc ?? entry.CreatedAtUtc);
            },
            cancellationToken);
}
