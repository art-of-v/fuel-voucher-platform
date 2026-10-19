using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using FuelFlow.Features.Ledger.SharedModels;
using FuelFlow.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace FuelFlow.Features.Ledger;

/// <summary>
/// Turns a business fact into a balanced, posted journal entry.
/// <para>
/// The single rule that shapes this class: <b>no account code appears here.</b> Accounts come from
/// <see cref="LedgerMappingRule"/>, so replacing the chart of accounts - which is the accountant's
/// decision, and one the business has not made yet - is a data change that leaves this file
/// untouched. That is what makes the engine buildable before the chart exists: the chart is an
/// input, not a dependency of the code.
/// </para>
/// <para>
/// Posting is idempotent on <c>(source_event, source_type, source_id)</c>. A Monobank webhook that
/// retries, or an outbox handler that replays, gets the original entry back rather than a second
/// one. That uniqueness is a database index rather than a read-then-write check, so two concurrent
/// replays cannot both decide they are first.
/// </para>
/// </summary>
public sealed class JournalPostingService
{
    private readonly ApplicationDbContext _db;
    private readonly ILogger<JournalPostingService> _logger;

    public JournalPostingService(ApplicationDbContext db, ILogger<JournalPostingService> logger)
    {
        _db = db;
        _logger = logger;
    }

    /// <summary>
    /// Posts <paramref name="request"/> and returns the entry - the existing one if this business
    /// fact was already posted. Runs inside the caller's transaction, so a caller that posts several
    /// entries for one payment commits them together or not at all.
    /// </summary>
    public async Task<JournalEntry> PostAsync(
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
                "Journal entry for {SourceEvent} source {SourceId} already exists as {EntryId}; not posting twice.",
                request.SourceEvent, request.SourceId, existing.Id);
            return existing;
        }

        var (debit, credit) = await ResolveAccountsAsync(request.SourceEvent, cancellationToken);
        var period = await ResolvePeriodAsync(request.EntryDate, cancellationToken);
        var entryNo = await NextEntryNoAsync(period.Id, cancellationToken);
        var now = DateTime.UtcNow;

        var entry = new JournalEntry
        {
            Id = Guid.NewGuid(),
            EntryNo = entryNo,
            EntryDate = request.EntryDate,
            PeriodId = period.Id,
            SourceType = request.SourceType,
            SourceEvent = request.SourceEvent,
            SourceId = request.SourceId,
            Description = request.Description,
            // Draft first. trg_journal_lines_posted_is_immutable refuses a line added to an entry
            // that is already posted, so the entry and its lines cannot be written in one INSERT -
            // the entry has to exist and be composable before it is posted.
            Status = JournalEntryStatus.Draft,
            CreatedAtUtc = now,
            CreatedByUserId = request.PostedByUserId,
            Lines =
            [
                NewLine(1, debit, request),
                NewLine(2, credit, request),
            ],
        };

        _db.JournalEntries.Add(entry);
        await _db.SaveChangesAsync(cancellationToken);

        try
        {
            entry.Status = JournalEntryStatus.Posted;
            entry.PostedAtUtc = now;
            entry.PostedByUserId = request.PostedByUserId;
            entry.TamperHash = ComputeTamperHash(entry);

            await _db.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            // Do not leave a half-written draft behind: it would be a visible, unbalanced posting
            // that nobody asked for. The caller usually wraps this in a transaction and rolls the
            // whole thing back, but the service must not depend on that to avoid leaving debris.
            _db.Entry(entry).State = EntityState.Deleted;
            await _db.SaveChangesAsync(cancellationToken);
            throw;
        }

        _logger.LogInformation(
            "Posted journal entry {EntryNo} for {SourceEvent} ({Amount} UAH).",
            entry.EntryNo, request.SourceEvent, request.AmountUah);

        return entry;
    }

    private async Task<JournalEntry?> FindExistingAsync(
        PostJournalEntryRequest request,
        CancellationToken cancellationToken) =>
        await _db.JournalEntries
            .Include(e => e.Lines)
            .FirstOrDefaultAsync(
                e => e.SourceEvent == request.SourceEvent
                     && e.SourceType == request.SourceType
                     && e.SourceId == request.SourceId,
                cancellationToken);

    /// <summary>
    /// Resolves the two accounts the event posts to. The mapping is total by design: an event with
    /// one side mapped and the other missing is refused rather than posted one-sided, because a
    /// half-posting is an entry that balances against nothing.
    /// </summary>
    private async Task<(ChartOfAccount Debit, ChartOfAccount Credit)> ResolveAccountsAsync(
        string sourceEvent,
        CancellationToken cancellationToken)
    {
        var rules = await _db.LedgerMappingRules
            .Include(r => r.Account)
            .Where(r => r.SourceEvent == sourceEvent)
            .ToListAsync(cancellationToken);

        var debitRule = rules.FirstOrDefault(r => r.Side == NormalBalance.Debit);
        var creditRule = rules.FirstOrDefault(r => r.Side == NormalBalance.Credit);

        if (debitRule is null)
            throw new LedgerUnmappedEventException(sourceEvent, "debit");
        if (creditRule is null)
            throw new LedgerUnmappedEventException(sourceEvent, "credit");

        // A header account ("1000 Assets") is a grouping, not a place to post. Nothing in the schema
        // stops a line naming one, which is why ChartOfAccount.IsPostable exists - and this is
        // where it is enforced.
        foreach (var rule in new[] { debitRule, creditRule })
        {
            if (!rule.Account.IsPostable)
                throw new LedgerPostingException(
                    $"Account {rule.Account.Code} '{rule.Account.NameUkr}' is a header; " +
                    $"{sourceEvent} must map to a postable account.");
            if (!rule.Account.IsActive)
                throw new LedgerPostingException(
                    $"Account {rule.Account.Code} '{rule.Account.NameUkr}' is inactive; " +
                    $"{sourceEvent} cannot post to it.");
        }

        return (debitRule.Account, creditRule.Account);
    }

    /// <summary>
    /// Finds the period holding <paramref name="date"/>, creating it open if it does not exist yet.
    /// Auto-creation is safe because it only ever creates an <em>open</em> period: closing one is
    /// always a human decision, and the accountant's chart arrives with its own set of periods.
    /// </summary>
    private async Task<AccountingPeriod> ResolvePeriodAsync(
        DateOnly date,
        CancellationToken cancellationToken)
    {
        var period = await _db.AccountingPeriods
            .FirstOrDefaultAsync(p => p.PeriodStart <= date && p.PeriodEnd >= date, cancellationToken);

        if (period is null)
        {
            var start = new DateOnly(date.Year, date.Month, 1);
            period = new AccountingPeriod
            {
                Id = Guid.NewGuid(),
                PeriodStart = start,
                PeriodEnd = start.AddMonths(1).AddDays(-1),
                Status = PeriodStatus.Open,
            };
            _db.AccountingPeriods.Add(period);
            _logger.LogInformation("Opened accounting period {Start:yyyy-MM}..{End:yyyy-MM-dd}.", start, period.PeriodEnd);
        }

        if (period.Status == PeriodStatus.Closed)
            throw new LedgerPeriodClosedException(date);

        return period;
    }

    /// <summary>
    /// Hands out the next entry number for the period. Done as a single atomic upsert rather than
    /// "read then write", because two posts in the same period would otherwise read the same value
    /// and the unique index on <c>(period_id, entry_no)</c> would surface as an unhandled error.
    /// </summary>
    private async Task<long> NextEntryNoAsync(Guid periodId, CancellationToken cancellationToken)
    {
        var name = $"journal:{periodId}";

        // Not Database.SqlQuery: EF composes a query over anything handed to it, and an
        // INSERT ... ON CONFLICT ... RETURNING is not composable. Opening the connection through
        // the context instead means the upsert runs on the caller's connection and inside the
        // caller's transaction, so a post that is later rolled back does not burn a number.
        await _db.Database.OpenConnectionAsync(cancellationToken);

        await using var command = _db.Database.GetDbConnection().CreateCommand();
        command.CommandText = """
            INSERT INTO document_sequences (id, name, last_value)
            VALUES (gen_random_uuid(), @name, 1)
            ON CONFLICT (name) DO UPDATE
                SET last_value = document_sequences.last_value + 1
            RETURNING last_value;
            """;
        command.Parameters.Add(new NpgsqlParameter("name", NpgsqlDbType.Varchar) { Value = name });

        var result = await command.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt64(result, CultureInfo.InvariantCulture);
    }

    private static JournalLine NewLine(short lineNo, ChartOfAccount account, PostJournalEntryRequest request)
    {
        // Line 1 is the debit leg and line 2 the credit leg because ResolveAccountsAsync returned
        // them in that order - not because of the account's normal balance. Revenue is normally
        // credited but is debited when a refund reverses it, so the side must come from the leg's
        // position, never from the account.
        return new JournalLine
        {
            Id = Guid.NewGuid(),
            LineNo = lineNo,
            AccountId = account.Id,
            PartyId = request.PartyId,
            OfferId = request.OfferId,
            LegalEntityId = request.LegalEntityId,
            VoucherId = request.VoucherId,
            QuantityLiters = request.QuantityLiters,
            DebitUah = lineNo == 1 ? request.AmountUah : 0m,
            CreditUah = lineNo == 2 ? request.AmountUah : 0m,
        };
    }

    /// <summary>
    /// SHA-256 over the entry's content, so a posted entry's contents can be shown not to have been
    /// edited in place. It is a checksum, not a signature - anyone with write access can recompute
    /// it - and it is stored because it still turns a silent <c>UPDATE</c> into a detectable one.
    /// </summary>
    private static string ComputeTamperHash(JournalEntry entry)
    {
        var lines = entry.Lines
            .OrderBy(l => l.LineNo)
            .Select(l => string.Create(CultureInfo.InvariantCulture, $"{l.LineNo}:{l.AccountId}:{l.DebitUah}:{l.CreditUah}"));

        var payload = string.Join(
            "|",
            entry.EntryNo.ToString(CultureInfo.InvariantCulture),
            entry.EntryDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            entry.SourceType.ToString(),
            entry.SourceEvent,
            entry.SourceId?.ToString() ?? string.Empty,
            entry.Description,
            string.Join(";", lines));

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload))).ToLowerInvariant();
    }
}