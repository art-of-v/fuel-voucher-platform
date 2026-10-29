using FuelFlow.Features.Acquiring;
using FuelFlow.Features.Acquiring.UnappliedReceipts;
using FuelFlow.Features.Ledger.SharedModels;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Options;
using FuelFlow.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Ledger.GetTrialBalance;

/// <summary>
/// The repeatable check closing a month needs: posted balances per account, against the acquirer's
/// statement for the same period.
/// </summary>
/// <remarks>
/// <para>
/// The accounts are aggregated in the database. A trial balance is a report over every journal line
/// that ever existed up to the period end, and loading that into memory to add it up would make the
/// endpoint's cost grow with the business.
/// </para>
/// <para>
/// Reversed entries are included, not filtered out. A reversal is a second posting that counters the
/// first; both are facts an auditor reads, and dropping either side would leave the account showing
/// half of a correction.
/// </para>
/// </remarks>
public sealed class GetTrialBalanceQueryHandler
{
    /// <summary>The bank account. Named once, here, so the check does not hunt for it by name.</summary>
    internal const string BankAccountCode = "311";

    private readonly ApplicationDbContext _context;

    public GetTrialBalanceQueryHandler(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<TrialBalanceResponse> HandleAsync(
        TrialBalanceQuery query,
        CancellationToken cancellationToken = default)
    {
        var (from, to) = ResolvePeriod(query);
        var toExclusive = to.AddDays(1);

        var accounts = await AggregateAsync(
            [JournalEntryStatus.Posted, JournalEntryStatus.Reversed], from, toExclusive, cancellationToken);
        var drafts = await AggregateAsync(
            [JournalEntryStatus.Draft], from, toExclusive, cancellationToken);

        var bank = accounts.FirstOrDefault(a => a.Code == BankAccountCode);

        var fromUtc = DateTime.SpecifyKind(from.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);
        var toUtc = DateTime.SpecifyKind(toExclusive.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);

        // The acquirer's side: only the Live merchant's rows are the company's money. A sandbox row
        // is a simulation and comparing it to a bank account would report a difference nobody can act on.
        var statementRows = await _context.MonobankStatementEntries
            .AsNoTracking()
            .Where(e => e.Merchant == MonobankMerchant.Live
                     && e.AmountKopecks > 0
                     && e.AcquirerCreatedAtUtc >= fromUtc
                     && e.AcquirerCreatedAtUtc < toUtc)
            .Select(e => new { e.Status, e.AmountKopecks })
            .ToListAsync(cancellationToken);

        var receivedRows = statementRows
            .Where(r => MonobankStatementEntry.IsReceivedStatus(r.Status))
            .ToList();

        var statementTotal = receivedRows.Sum(r => r.AmountKopecks ?? 0m) / 100m;
        var ledgerDebit = bank?.Debit ?? 0m;
        var difference = statementTotal - ledgerDebit;

        var unbooked = await UnappliedReceipts.FindAsync(
            _context, MonobankMerchant.Live, fromUtc, toUtc, cancellationToken);

        return new TrialBalanceResponse(
            from,
            to,
            accounts,
            new TrialBalanceTotals(
                accounts.Sum(a => a.Debit),
                accounts.Sum(a => a.Credit),
                accounts.Sum(a => a.Debit) == accounts.Sum(a => a.Credit)),
            drafts,
            new BankCheck(
                MonobankMerchant.Live.ToString(),
                statementTotal,
                receivedRows.Count,
                ledgerDebit,
                bank?.Credit ?? 0m,
                bank?.ClosingBalance ?? 0m,
                difference,
                difference == 0m && unbooked.Count == 0),
            unbooked);
    }

    /// <summary>
    /// Reads one account's whole history in one query: everything before the period is its opening
    /// balance, everything inside it is its movement.
    /// </summary>
    /// <remarks>
    /// The statuses are a list rather than a flag because the two sets are different facts: the books
    /// are everything that is not a draft - posted and reversed - while the draft list is exactly one
    /// status. A condition that switched between the two would not translate.
    /// </remarks>
    private async Task<IReadOnlyList<TrialBalanceAccount>> AggregateAsync(
        JournalEntryStatus[] statuses,
        DateOnly periodStart,
        DateOnly periodEndExclusive,
        CancellationToken cancellationToken)
    {
        // "from"/"to" are not usable as local names inside a query comprehension - the parser reads
        // them as query keywords and the ternary that follows fails with a syntax error, which is why
        // these are periodStart/periodEndExclusive.
        var rows = await (
            from line in _context.JournalLines.AsNoTracking()
            join entry in _context.JournalEntries.AsNoTracking() on line.EntryId equals entry.Id
            join account in _context.ChartOfAccounts.AsNoTracking() on line.AccountId equals account.Id
            where statuses.Contains(entry.Status)
            group new { line, entry, account } by new
            {
                account.Code,
                account.NameUkr,
                account.AccountType,
                account.NormalBalance
            }
            into g
            select new
            {
                g.Key.Code,
                g.Key.NameUkr,
                g.Key.AccountType,
                g.Key.NormalBalance,
                OpeningDebit = g.Sum(x => (x.entry.EntryDate < periodStart ? x.line.DebitUah : 0m)),
                OpeningCredit = g.Sum(x => (x.entry.EntryDate < periodStart ? x.line.CreditUah : 0m)),
                Debit = g.Sum(x => (x.entry.EntryDate >= periodStart && x.entry.EntryDate < periodEndExclusive ? x.line.DebitUah : 0m)),
                Credit = g.Sum(x => (x.entry.EntryDate >= periodStart && x.entry.EntryDate < periodEndExclusive ? x.line.CreditUah : 0m)),
            })
            .ToListAsync(cancellationToken);

        return rows
            .Select(r =>
            {
                var opening = r.OpeningDebit - r.OpeningCredit;
                var closing = opening + r.Debit - r.Credit;

                return new TrialBalanceAccount(
                    r.Code,
                    r.NameUkr,
                    r.AccountType,
                    r.NormalBalance,
                    Math.Abs(opening),
                    Side(opening),
                    r.Debit,
                    r.Credit,
                    Math.Abs(closing),
                    Side(closing));
            })
            .OrderBy(a => a.Code, StringComparer.Ordinal)
            .ToList();
    }

    private static NormalBalance? Side(decimal signed) => signed switch
    {
        > 0m => NormalBalance.Debit,
        < 0m => NormalBalance.Credit,
        _ => null
    };

    /// <summary>
    /// The calendar month containing today, unless both ends are given. The same resolution as the
    /// reconciliation act, so "October" means one period everywhere a document is produced.
    /// </summary>
    internal static (DateOnly From, DateOnly To) ResolvePeriod(TrialBalanceQuery query, DateOnly? today = null)
    {
        if (query.FromDate is { } from && query.ToDate is { } to)
        {
            return (from, to);
        }

        var reference = today ?? DateOnly.FromDateTime(SystemClock.UtcNow);
        var start = new DateOnly(reference.Year, reference.Month, 1);
        return (start, start.AddMonths(1).AddDays(-1));
    }
}

public sealed record TrialBalanceQuery
{
    public DateOnly? FromDate { get; init; }
    public DateOnly? ToDate { get; init; }
}
