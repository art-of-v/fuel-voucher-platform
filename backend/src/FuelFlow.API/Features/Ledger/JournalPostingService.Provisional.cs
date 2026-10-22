using FuelFlow.Features.Ledger.SharedModels;
using FuelFlow.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Ledger;

/// <summary>
/// The provisional half of the posting engine: entries that have been written but are not yet in
/// the books.
/// </summary>
/// <remarks>
/// A self-reported voucher redemption is a fact the business has not confirmed with the supplier,
/// and the books should say so (planning #684). The entry is real from the moment it is written -
/// it has a number, its lines and a source an auditor can trace - but it is a <b>draft</b>: the
/// posting is the claim that someone stood behind it, and that claim belongs to the confirmation,
/// not to the report. Until then it is invisible to every report that reads posted entries, which
/// is exactly the property a provisional state is for.
/// <para>
/// The other direction matters as much. A draft is discarded when the report is withdrawn, and a
/// posted entry is never edited when it turns out to be wrong: it is reversed by its opposite, in
/// an open period, and the original stays on the books for the auditor.
/// </para>
/// <para>
/// These methods run inside the caller's transaction, like every other write in this service.
/// </para>
/// </remarks>
public sealed partial class JournalPostingService
{
    /// <summary>
    /// Writes a balanced entry as a draft: composed, numbered, and deliberately not posted.
    /// </summary>
    /// <remarks>
    /// Idempotent on the same key as <see cref="PostAsync"/>: a replay returns the entry that is
    /// already there, whether it is still a draft or has since been confirmed.
    /// </remarks>
    public async Task<JournalEntry> PostProvisionalAsync(
        PostJournalEntryRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.AmountUah <= 0)
            throw new LedgerPostingException(
                $"Amount for '{request.SourceEvent}' must be greater than zero; got {request.AmountUah}.");
        if (string.IsNullOrWhiteSpace(request.Description))
            throw new LedgerPostingException($"Description for '{request.SourceEvent}' must not be blank.");

        var existing = await FindExistingAsync(request, cancellationToken);
        if (existing is not null)
        {
            _logger.LogInformation(
                "Provisional journal entry for {SourceEvent} source {SourceId} already exists as {EntryId}; not composing twice.",
                request.SourceEvent, request.SourceId, existing.Id);
            return existing;
        }

        var entry = await ComposeDraftAsync(request, SystemClock.UtcNow, cancellationToken);

        _logger.LogInformation(
            "Composed provisional journal entry {EntryNo} for {SourceEvent} ({Amount} UAH); it stays out of the books until it is confirmed.",
            entry.EntryNo, request.SourceEvent, request.AmountUah);

        return entry;
    }

    /// <summary>
    /// Posts every draft the source row left behind, in place where its period is still open and
    /// reposted into the open period where it is not.
    /// </summary>
    /// <remarks>
    /// <para>
    /// One source row may carry several drafts - a redemption posts revenue, VAT and cost of sales -
    /// and a confirmation is about the redemption, not about one of its entries, so all of them are
    /// posted together.
    /// </para>
    /// <para>
    /// A confirmation that arrives after the month closed cannot post into it:
    /// <c>trg_journal_entry_posted_is_coherent</c> refuses exactly that, and rightly so - a closed
    /// month does not move. The draft is then discarded and its lines are posted again, dated
    /// today, in the open period. The original period keeps the number it no longer uses; the fact
    /// belongs to the period it arrived in, not to the one that was already signed off.
    /// </para>
    /// <para>
    /// Idempotent: a second confirmation finds no drafts and returns the entries the first one
    /// posted.
    /// </para>
    /// </remarks>
    public async Task<IReadOnlyList<JournalEntry>> ConfirmProvisionalAsync(
        Guid sourceId,
        Guid postedByUserId,
        CancellationToken cancellationToken = default)
    {
        var drafts = await _db.JournalEntries
            .Include(e => e.Lines)
            .Where(e => e.SourceId == sourceId && e.Status == JournalEntryStatus.Draft)
            .ToListAsync(cancellationToken);

        if (drafts.Count == 0)
        {
            return await _db.JournalEntries
                .Include(e => e.Lines)
                .Where(e => e.SourceId == sourceId && e.Status != JournalEntryStatus.Draft)
                .ToListAsync(cancellationToken);
        }

        var now = SystemClock.UtcNow;
        var periodIds = drafts.Select(e => e.PeriodId).Distinct().ToArray();

        var closedPeriodIds = await _db.AccountingPeriods
            .Where(p => periodIds.Contains(p.Id) && p.Status == PeriodStatus.Closed)
            .Select(p => p.Id)
            .ToListAsync(cancellationToken);

        var stranded = drafts.Where(e => closedPeriodIds.Contains(e.PeriodId)).ToList();
        var inPlace = drafts.Where(e => !closedPeriodIds.Contains(e.PeriodId)).ToList();

        var reposted = new List<JournalEntry>(stranded.Count);
        if (stranded.Count > 0)
        {
            // Free the idempotency key and the entry numbers first: the reposted entry carries the
            // same (source_event, source_type, source_id), and the unique index refuses it while the
            // discarded draft still holds the triple.
            _db.JournalEntries.RemoveRange(stranded);
            await _db.SaveChangesAsync(cancellationToken);

            var today = DateOnly.FromDateTime(now);
            var period = await ResolvePeriodAsync(today, cancellationToken);
            var firstNo = await NextEntryNosAsync(period.Id, stranded.Count, cancellationToken);

            for (var index = 0; index < stranded.Count; index++)
            {
                reposted.Add(CloneAsRepost(stranded[index], today, period.Id, firstNo + index, now));
            }

            _db.JournalEntries.AddRange(reposted);
            await _db.SaveChangesAsync(cancellationToken);
        }

        var toPost = inPlace.Concat(reposted).ToList();
        foreach (var entry in toPost)
        {
            MarkPosted(entry, postedByUserId, now);
        }

        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Confirmed {Count} provisional journal entries for source {SourceId}; {Reposted} were reposted into the open period.",
            toPost.Count, sourceId, reposted.Count);

        return toPost;
    }

    /// <summary>
    /// Removes the drafts the source row left behind, posting nothing.
    /// </summary>
    /// <remarks>
    /// This is the withdrawal: a report the supplier's statement never confirms, or a voucher the
    /// customer hands back before anyone confirmed it. Only drafts are touched - a posted entry is
    /// a fact and is reversed, never deleted - and the removal is idempotent, so withdrawing
    /// something already withdrawn is a no-op rather than an error.
    /// </remarks>
    public async Task<int> DiscardProvisionalAsync(
        Guid sourceId,
        CancellationToken cancellationToken = default)
    {
        var drafts = await _db.JournalEntries
            .Include(e => e.Lines)
            .Where(e => e.SourceId == sourceId && e.Status == JournalEntryStatus.Draft)
            .ToListAsync(cancellationToken);

        if (drafts.Count == 0)
        {
            return 0;
        }

        _db.JournalEntries.RemoveRange(drafts);
        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Discarded {Count} provisional journal entries for source {SourceId}.", drafts.Count, sourceId);

        return drafts.Count;
    }

    /// <summary>
    /// Posts the opposite of a posted entry and marks the original as countered by it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The original is never edited or deleted: a posted entry is what an auditor reads, and a
    /// correction is a second fact that undoes it. The reversal is dated today - in the open period,
    /// the only period the books will take it in - even when the original sits in a closed month.
    /// The original keeps its date, its number and its lines; <c>reversed_by_entry_id</c> is the
    /// link between the two.
    /// </para>
    /// <para>
    /// Only a posted entry can be reversed. A draft is discarded instead, and the reason must say
    /// why - a reversal without one is an entry nobody can explain later.
    /// </para>
    /// </remarks>
    public async Task<JournalEntry> ReverseAsync(
        Guid entryId,
        Guid postedByUserId,
        string reason,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw new LedgerPostingException("A reversal must say why; the reason is blank.");

        var original = await _db.JournalEntries
            .Include(e => e.Lines)
            .FirstOrDefaultAsync(e => e.Id == entryId, cancellationToken)
            ?? throw new LedgerPostingException($"Journal entry {entryId} does not exist.");

        if (original.ReversedByEntryId is not null)
            throw new LedgerPostingException(
                $"Journal entry {entryId} was already reversed by {original.ReversedByEntryId}.");
        if (original.Status != JournalEntryStatus.Posted)
            throw new LedgerPostingException(
                $"Only a posted entry can be reversed; entry {entryId} is {original.Status}.");

        var now = SystemClock.UtcNow;
        var today = DateOnly.FromDateTime(now);
        var period = await ResolvePeriodAsync(today, cancellationToken);
        var entryNo = await NextEntryNoAsync(period.Id, cancellationToken);

        var reversal = new JournalEntry
        {
            Id = Guid.CreateVersion7(),
            EntryNo = entryNo,
            EntryDate = today,
            PeriodId = period.Id,
            SourceType = JournalSourceType.Reversal,
            // The target is source_id below. The event name stays short and constant because
            // source_event is capped at 40 characters, and appending a suffix to an already long
            // event would overflow the column rather than fail loudly at a useful place.
            SourceEvent = "entry.reversed",
            SourceId = original.Id,
            Description = ReversalDescription(original, reason),
            Status = JournalEntryStatus.Draft,
            CreatedAtUtc = now,
            CreatedByUserId = postedByUserId,
            // Opposite sides, same accounts and the same subledger fields: undoing a posting must not
            // invent a counterparty or a voucher the posting did not name.
            Lines = original.Lines
                .OrderBy(l => l.LineNo)
                .Select(l => new JournalLine
                {
                    Id = Guid.CreateVersion7(),
                    LineNo = l.LineNo,
                    AccountId = l.AccountId,
                    PartyId = l.PartyId,
                    LegalEntityId = l.LegalEntityId,
                    VoucherId = l.VoucherId,
                    QuantityLiters = l.QuantityLiters,
                    DebitUah = l.CreditUah,
                    CreditUah = l.DebitUah,
                })
                .ToList(),
        };

        _db.JournalEntries.Add(reversal);
        await _db.SaveChangesAsync(cancellationToken);

        try
        {
            MarkPosted(reversal, postedByUserId, now);
            original.Status = JournalEntryStatus.Reversed;
            original.ReversedByEntryId = reversal.Id;

            await _db.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            // Same rule as PostAsync: do not leave a half-written reversal behind. Put the
            // original's in-memory change back too, or the cleanup below would retry the very save
            // that just failed.
            original.Status = JournalEntryStatus.Posted;
            original.ReversedByEntryId = null;
            _db.Entry(reversal).State = EntityState.Deleted;
            await _db.SaveChangesAsync(cancellationToken);
            throw;
        }

        _logger.LogInformation(
            "Reversed journal entry {EntryNo} with entry {ReversalNo} ({Reason}).",
            original.EntryNo, reversal.EntryNo, reason);

        return reversal;
    }

    /// <summary>
    /// Copies a stranded draft into the open period, dated today, keeping its lines and its source.
    /// New keys: a repost is a new posting, and the old one is about to be deleted - reusing its id
    /// would make the two indistinguishable in an audit.
    /// </summary>
    private static JournalEntry CloneAsRepost(
        JournalEntry draft,
        DateOnly date,
        Guid periodId,
        long entryNo,
        DateTime now)
        => new()
        {
            Id = Guid.CreateVersion7(),
            EntryNo = entryNo,
            EntryDate = date,
            PeriodId = periodId,
            SourceType = draft.SourceType,
            SourceEvent = draft.SourceEvent,
            SourceId = draft.SourceId,
            Description = draft.Description,
            Status = JournalEntryStatus.Draft,
            CreatedAtUtc = now,
            CreatedByUserId = draft.CreatedByUserId,
            Lines = draft.Lines
                .OrderBy(l => l.LineNo)
                .Select(l => new JournalLine
                {
                    Id = Guid.CreateVersion7(),
                    LineNo = l.LineNo,
                    AccountId = l.AccountId,
                    PartyId = l.PartyId,
                    LegalEntityId = l.LegalEntityId,
                    VoucherId = l.VoucherId,
                    QuantityLiters = l.QuantityLiters,
                    DebitUah = l.DebitUah,
                    CreditUah = l.CreditUah,
                })
                .ToList(),
        };

    /// <summary>The reversal's description names the entry it counters and repeats the reason.</summary>
    private static string ReversalDescription(JournalEntry original, string reason)
    {
        var description = $"Reversal of entry {original.EntryNo} ({original.SourceEvent}): {reason.Trim()}";
        return description.Length <= 500 ? description : description[..500];
    }
}
