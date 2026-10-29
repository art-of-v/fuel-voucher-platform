using FuelFlow.Features.Acquiring.UnappliedReceipts;
using FuelFlow.Features.Ledger.SharedModels;

namespace FuelFlow.Features.Ledger.GetTrialBalance;

/// <summary>
/// The close check (#780): what the books say for a period, and whether the bank agrees with them.
/// </summary>
/// <remarks>
/// Two ledgers side by side, and the point is that one of them is not ours. <see cref="Accounts"/> is
/// the posted journal; <see cref="Bank"/> is the acquirer's statement and the account it settles
/// into. A trial balance that balances proves the postings are internally consistent - it does not
/// prove the money is real, which is why the bank leg is part of the same document.
/// <para>
/// Drafts are listed separately and deliberately not mixed in: a draft is a claim nobody has stood
/// behind yet, and a trial balance that folded drafts into the books would report revenue the
/// operator has not confirmed.
/// </para>
/// </remarks>
/// <param name="Accounts">Posted and reversed entries, per account: opening, movement, closing.</param>
/// <param name="Drafts">The same shape over provisional entries, so the operator can see what is
/// still unposted and decide it.</param>
/// <param name="UnbookedCaptures">Acquirer money with no payment row - the receipts the books are
/// short of until someone books them.</param>
public sealed record TrialBalanceResponse(
    DateOnly FromDate,
    DateOnly ToDate,
    IReadOnlyList<TrialBalanceAccount> Accounts,
    TrialBalanceTotals Totals,
    IReadOnlyList<TrialBalanceAccount> Drafts,
    BankCheck Bank,
    IReadOnlyList<UnbookedCapture> UnbookedCaptures)
{
    /// <summary>
    /// Everything the close asks of the books, in one flag.
    /// </summary>
    /// <remarks>
    /// <see cref="TrialBalanceTotals.Balanced"/> alone is not enough: a book can be balanced and
    /// still be missing the bank's money, which is exactly the state #780 was opened in. A draft is
    /// the third way to be unfinished - revenue composed but not confirmed is revenue the month's
    /// result cannot count, so a month with drafts in it is not closed either.
    /// </remarks>
    public bool ReadyToClose
        => Totals.Balanced && Bank.Matches && UnbookedCaptures.Count == 0 && Drafts.Count == 0;
}

/// <summary>
/// One account's period, in the form an accountant reads: a balance brought forward, the movement,
/// and a balance carried forward with the side it sits on.
/// </summary>
/// <remarks>
/// <c>Closing</c> is always positive and <see cref="ClosingSide"/> says which column it belongs in.
/// A negative closing balance with an implied side is how a report ends up read backwards.
/// </remarks>
public sealed record TrialBalanceAccount(
    string Code,
    string NameUkr,
    AccountType AccountType,
    NormalBalance NormalBalance,
    decimal OpeningBalance,
    NormalBalance? OpeningSide,
    decimal Debit,
    decimal Credit,
    decimal ClosingBalance,
    NormalBalance? ClosingSide);

public sealed record TrialBalanceTotals(decimal Debit, decimal Credit, bool Balanced);

/// <summary>
/// The acquirer's received money for the period against what account 311 recorded.
/// </summary>
/// <param name="StatementReceivedUah">Gross received, from the Live merchant's statement rows.</param>
/// <param name="Ledger311DebitUah">Money in, per the bank account in the books.</param>
/// <param name="Ledger311ClosingUah">The bank account's closing balance, all time to the period end.</param>
/// <param name="DifferenceUah">Statement minus ledger. Positive means the bank holds money the
/// books do not.</param>
/// <param name="Matches">The difference is zero. The check assumes the Monobank merchant is the only
/// payer into 311; a different receipt would show here as a difference and needs its own
/// reconciliation, not a correction to this one.</param>
public sealed record BankCheck(
    string Merchant,
    decimal StatementReceivedUah,
    int StatementRowCount,
    decimal Ledger311DebitUah,
    decimal Ledger311CreditUah,
    decimal Ledger311ClosingUah,
    decimal DifferenceUah,
    bool Matches);
