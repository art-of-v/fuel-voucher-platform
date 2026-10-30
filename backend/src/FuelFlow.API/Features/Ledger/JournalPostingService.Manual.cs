using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using FuelFlow.Features.Ledger.ManualEntries;
using FuelFlow.Features.Ledger.SharedModels;
using FuelFlow.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Ledger;

/// <summary>
/// The manual half of the posting engine: entries a person decides, not events that happen.
/// </summary>
/// <remarks>
/// <para>
/// A reclassification, an opening balance, a month-end accrual, a correction that is not the reversal
/// of one specific event - none of them is caused by a row in the operational tables, and until #795
/// none of them could be recorded at all. The only alternative was hand-written SQL against the
/// journal, which skips the entry numbering, the period-open check, the tamper hash and the balance
/// triggers: every invariant the journal exists to hold (planning #795).
/// </para>
/// <para>
/// The shape is the journal's own, not the mapping's: the caller names the accounts and the sides,
/// because a human made the decision and the accounts are the decision. That is the one path in this
/// engine where an account code is allowed to arrive from outside, and it is why the path is a
/// separate method rather than a flag on <see cref="PostAsync"/> - the event path must never be able
/// to bypass <c>ledger_mapping_rules</c>.
/// </para>
/// <para>
/// Idempotent on the caller's <c>reference</c>: the same reference posted twice returns the entry the
/// first call wrote. The reference is hashed into <c>source_id</c>, so the unique index on
/// <c>(source_event, source_type, source_id)</c> is the same one every other posting hangs on - a
/// retried HTTP call, a re-run script and a duplicate request from a second operator all land on one
/// entry rather than two identical ones the books cannot tell apart.
/// </para>
/// </remarks>
public sealed partial class JournalPostingService
{
    /// <summary>
    /// The event name every manual entry carries. Constant and short: <c>source_event</c> is capped
    /// at 40 characters, and this is the key an accountant queries when they want to see every
    /// adjustment a human made.
    /// </summary>
    public const string ManualAdjustmentEvent = "manual.adjustment";

    /// <summary>The most <c>numeric(14,2)</c> can hold.</summary>
    private const decimal MaxManualAmountUah = 999_999_999_999.99m;

    /// <summary>
    /// Posts a balanced, N-leg entry a person composed: the reclassification, the accrual, the
    /// opening balance.
    /// </summary>
    /// <remarks>
    /// Runs inside the caller's transaction like every other write here. The entry is composed as a
    /// draft and posted in the same unit of work, because a manual entry has no provisional state -
    /// the operator posting it is the confirmation.
    /// </remarks>
    /// <exception cref="LedgerPostingException">
    /// The request is not a posting: unbalanced, a leg with both sides or neither, an unknown, header
    /// or inactive account, or a blank reason.
    /// </exception>
    /// <exception cref="LedgerPeriodClosedException">The period holding <c>EntryDate</c> is closed.</exception>
    public async Task<JournalEntry> PostManualAsync(
        PostManualJournalEntryRequest request,
        Guid postedByUserId,
        CancellationToken cancellationToken = default)
    {
        ValidateManualEntry(request);

        var reference = request.Reference.Trim();
        var sourceId = ManualSourceId(reference);

        var existing = await _db.JournalEntries
            .Include(e => e.Lines)
            .FirstOrDefaultAsync(
                e => e.SourceEvent == ManualAdjustmentEvent
                     && e.SourceType == JournalSourceType.Adjustment
                     && e.SourceId == sourceId,
                cancellationToken);
        if (existing is not null)
        {
            if (existing.Status != JournalEntryStatus.Draft)
            {
                _logger.LogInformation(
                    "Manual entry {Reference} was already posted as entry {EntryNo}; not posting it twice.",
                    reference, existing.EntryNo);
                return existing;
            }

            // A draft under this reference is debris: an earlier attempt wrote the draft, failed to
            // post it, and its own cleanup failed too. It is not a posting - nobody stood behind it -
            // so the operator's request is served by discarding it and composing again. Returning it
            // would report a draft as posted, which is the one lie this method must not tell.
            _logger.LogWarning(
                "A stranded draft (entry {EntryNo}) held reference {Reference}; discarding it and posting afresh.",
                existing.EntryNo, reference);

            _db.JournalLines.RemoveRange(existing.Lines);
            _db.JournalEntries.Remove(existing);
            await _db.SaveChangesAsync(cancellationToken);
        }

        var accounts = await ResolveManualAccountsAsync(request, cancellationToken);
        var period = await ResolvePeriodAsync(request.EntryDate, cancellationToken);
        var entryNo = await NextEntryNoAsync(period.Id, cancellationToken);
        var now = SystemClock.UtcNow;

        var entry = new JournalEntry
        {
            Id = Guid.CreateVersion7(),
            EntryNo = entryNo,
            EntryDate = request.EntryDate,
            PeriodId = period.Id,
            SourceType = JournalSourceType.Adjustment,
            SourceEvent = ManualAdjustmentEvent,
            SourceId = sourceId,
            Description = request.Description.Trim(),
            Status = JournalEntryStatus.Draft,
            CreatedAtUtc = now,
            CreatedByUserId = postedByUserId,
            // Line numbers follow the order the caller wrote the legs in: the journal prints an entry
            // the way its author composed it, so the debit leg an accountant explains is the one they
            // typed first.
            Lines = request.Lines
                .Select((line, index) => NewManualLine((short)(index + 1), accounts[line.AccountCode.Trim()], line))
                .ToList(),
        };

        _db.JournalEntries.Add(entry);
        await _db.SaveChangesAsync(cancellationToken);

        try
        {
            MarkPosted(entry, postedByUserId, now);
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            // Same rule as PostAsync: a failed posting must not leave a half-written draft behind.
            _db.Entry(entry).State = EntityState.Deleted;
            await _db.SaveChangesAsync(cancellationToken);
            throw;
        }

        _logger.LogWarning(
            "Posted manual journal entry {EntryNo} ({Reference}) dated {EntryDate}: {Total} UAH over {Legs} legs, by user {UserId}.",
            entry.EntryNo,
            reference,
            request.EntryDate,
            request.Lines.Sum(l => l.DebitUah).ToString("0.00", CultureInfo.InvariantCulture),
            entry.Lines.Count,
            postedByUserId);

        return entry;
    }

    /// <summary>
    /// The idempotency key: the reference, hashed into the Guid <c>source_id</c> holds. Deterministic
    /// on purpose - the same reference must name the same entry in every process and every retry -
    /// and not time-ordered like the row keys, because it is not a row key: it is a pointer at the
    /// decision, and the decision is named, not stamped.
    /// </summary>
    private static Guid ManualSourceId(string reference)
    {
        var payload = $"{ManualAdjustmentEvent}|{reference}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(payload));

        return new Guid(hash.AsSpan(0, 16));
    }

    private static void ValidateManualEntry(PostManualJournalEntryRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Reference))
            throw new LedgerPostingException(
                "A manual entry must carry a reference: it is what makes a retry land on the same entry "
                + "instead of posting the same adjustment twice.");
        if (request.Reference.Trim().Length > 100)
            throw new LedgerPostingException("The reference must be at most 100 characters.");
        if (string.IsNullOrWhiteSpace(request.Description))
            throw new LedgerPostingException(
                "A manual entry must say why: the description is the only place the journal can carry the reason.");
        if (request.Description.Trim().Length > 500)
            throw new LedgerPostingException("The description must be at most 500 characters - the journal's column is 500.");
        if (request.Lines is null || request.Lines.Count < 2)
            throw new LedgerPostingException("A manual entry needs at least two legs: one debit and one credit.");

        var debit = 0m;
        var credit = 0m;

        for (var index = 0; index < request.Lines.Count; index++)
        {
            var line = request.Lines[index];
            var legNo = index + 1;

            if (line is null)
                throw new LedgerPostingException($"Leg {legNo} is empty.");
            if (string.IsNullOrWhiteSpace(line.AccountCode))
                throw new LedgerPostingException($"Leg {legNo} names no account.");
            if (line.DebitUah < 0m || line.CreditUah < 0m)
                throw new LedgerPostingException($"Leg {legNo} has a negative amount; the side says which way the money moves.");

            var hasDebit = line.DebitUah > 0m;
            var hasCredit = line.CreditUah > 0m;
            if (hasDebit == hasCredit)
                throw new LedgerPostingException(
                    $"Leg {legNo} must fill exactly one side; it fills {(hasDebit ? "both" : "neither")}. "
                    + "A line is a debit or a credit, never both and never a zero.");

            if (!IsWholeKopecks(line.DebitUah) || !IsWholeKopecks(line.CreditUah))
                throw new LedgerPostingException($"Leg {legNo} is not whole kopecks; the journal stores two decimals.");
            if (line.DebitUah > MaxManualAmountUah || line.CreditUah > MaxManualAmountUah)
                throw new LedgerPostingException($"Leg {legNo} exceeds what the journal's amount column can hold.");

            // ck_journal_lines_quantity_positive would refuse it at the database, as a 500 with the
            // trigger's words. Say it here, where it can be answered.
            if (line.QuantityLiters is <= 0m)
                throw new LedgerPostingException(
                    $"Leg {legNo} moves {line.QuantityLiters} litres; a quantity is either absent or greater than zero.");

            debit += line.DebitUah;
            credit += line.CreditUah;
        }

        // The database refuses an unbalanced posted entry too (assert_journal_entry_balanced). This
        // check exists so the operator gets the two totals and a 400, not a 500 from a deferred
        // trigger firing at COMMIT with no idea which entry it was about.
        if (debit != credit)
            throw new LedgerPostingException(string.Create(
                CultureInfo.InvariantCulture,
                $"The entry does not balance: {debit:0.00} debited, {credit:0.00} credited."));

        if (debit == 0m)
            throw new LedgerPostingException("The entry posts nothing: both sides are zero.");
    }

    private static bool IsWholeKopecks(decimal amount) => decimal.Round(amount, 2) == amount;

    /// <summary>
    /// Resolves every account the entry names, once, and refuses the ones nothing may post to. The
    /// checks are the ones <see cref="ResolveAccountsAsync"/> makes for the mapping path - a header
    /// is a grouping, not a place to post, and an inactive account is retired - applied to codes a
    /// person typed instead of ids data supplied.
    /// </summary>
    private async Task<Dictionary<string, ChartOfAccount>> ResolveManualAccountsAsync(
        PostManualJournalEntryRequest request,
        CancellationToken cancellationToken)
    {
        var codes = request.Lines.Select(l => l.AccountCode.Trim()).Distinct(StringComparer.Ordinal).ToArray();

        var accounts = await _db.ChartOfAccounts
            .Where(a => codes.Contains(a.Code))
            .ToListAsync(cancellationToken);
        var byCode = accounts.ToDictionary(a => a.Code, StringComparer.Ordinal);

        var missing = codes.Where(code => !byCode.ContainsKey(code)).ToList();
        if (missing.Count > 0)
            throw new LedgerPostingException(
                $"The chart has no account {string.Join(", ", missing.Select(code => $"'{code}'"))}; "
                + "a manual entry names accounts by the codes in the chart, not by new ones.");

        foreach (var account in byCode.Values)
        {
            if (!account.IsPostable)
                throw new LedgerPostingException(
                    $"Account {account.Code} '{account.NameUkr}' is a header; a manual entry must post to a postable account.");
            if (!account.IsActive)
                throw new LedgerPostingException(
                    $"Account {account.Code} '{account.NameUkr}' is inactive; a manual entry cannot post to it.");
        }

        return byCode;
    }

    private static JournalLine NewManualLine(short lineNo, ChartOfAccount account, ManualJournalLineRequest line) =>
        new()
        {
            Id = Guid.CreateVersion7(),
            LineNo = lineNo,
            AccountId = account.Id,
            PartyId = line.PartyId,
            LegalEntityId = line.LegalEntityId,
            VoucherId = line.VoucherId,
            QuantityLiters = line.QuantityLiters,
            // Which side a leg is on comes from the leg, never from the account's normal balance: a
            // reclassification debits an account that is normally credited, and that is the point.
            DebitUah = line.DebitUah,
            CreditUah = line.CreditUah,
        };
}
