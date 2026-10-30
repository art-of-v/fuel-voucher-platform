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
    VatCheck Vat,
    StockCheck Stock,
    IReadOnlyList<UnbookedCapture> UnbookedCaptures)
{
    /// <summary>
    /// Everything the close asks of the books, in one flag.
    /// </summary>
    /// <remarks>
    /// <see cref="TrialBalanceTotals.Balanced"/> alone is not enough: a book can be balanced and
    /// still be missing the bank's money, which is exactly the state #780 was opened in. A draft is
    /// the third way to be unfinished - revenue composed but not confirmed is revenue the month's
    /// result cannot count, so a month with drafts in it is not closed either. The last two are the
    /// accounts whose balance means something only if the other side was posted at all
    /// (planning #787): VAT carried in from an earlier period and still unsettled, and inventory
    /// that has been credited without ever being received.
    /// </remarks>
    public bool ReadyToClose
        => Totals.Balanced
           && Bank.Matches
           && UnbookedCaptures.Count == 0
           && Drafts.Count == 0
           && Vat.Settled
           && Stock.Complete;
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

/// <summary>
/// The VAT the platform charged, against the VAT an operator has settled with a filed return.
/// </summary>
/// <remarks>
/// <para>
/// 643 «Податкові зобов'язання» accumulates the VAT carved out of every payment, and only a filed
/// return moves it to 641 «Розрахунки за податками» (planning #787). Without that posting the
/// account's balance is "VAT charged since go-live", not "VAT owed to the budget", and a trial
/// balance that showed it without saying which one is a report read backwards.
/// </para>
/// <para>
/// <b>The check is on the liability carried in, not on the period's own VAT.</b> A period's VAT is
/// filed and paid in the next one - that is the calendar, not a delay - so demanding 643 be zero at
/// this period's end would demand the impossible. What must be true is that the liability the period
/// <em>started</em> with stopped being a liability inside it; anything older than one period is VAT
/// charged, collected from customers and never declared.
/// </para>
/// </remarks>
/// <param name="LiabilityCarriedInUah">643's credit balance at the period's start - the VAT that was
/// already due to the budget when the period opened.</param>
/// <param name="ChargedInPeriodUah">The VAT carved out of this period's payments.</param>
/// <param name="SettledInPeriodUah">What was moved out of 643 into 641 by a filed return.</param>
/// <param name="LiabilityClosingUah">643's credit balance at the period's end.</param>
/// <param name="PayableToBudgetUah">641's credit balance: VAT declared and not yet paid. Reported,
/// not gated - the payment date is the tax office's calendar, and a return filed on the last day is
/// not late because the money leaves the bank tomorrow.</param>
/// <param name="OlderThanOnePeriodUah">The part of <paramref name="LiabilityCarriedInUah"/> this
/// period did not settle. Non-zero means a return covering it was never filed.</param>
/// <param name="Settled">Nothing older than one period is left: the period's opening liability was
/// discharged, or there was none to discharge.</param>
public sealed record VatCheck(
    decimal LiabilityCarriedInUah,
    decimal ChargedInPeriodUah,
    decimal SettledInPeriodUah,
    decimal LiabilityClosingUah,
    decimal PayableToBudgetUah,
    decimal OlderThanOnePeriodUah,
    bool Settled);

/// <summary>
/// Inventory against the side that put it there.
/// </summary>
/// <remarks>
/// 281 «Товари на складі» is an asset: it is debited when stock is bought and credited when it is
/// sold. A <em>credit</em> closing balance therefore does not mean negative stock - it means the
/// cost of what was sold was booked without the purchase that puts it on the shelf, which is what
/// #787 found at the October close: 13 632.00 credited against nothing. The buy side is an operator
/// entry (Dr 281 / Cr 631 for a supplier invoice, or the opening stock Dr 281 / Cr 441), because what
/// a lot cost is not in any import the platform reads.
/// </remarks>
/// <param name="InventoryClosingUah">281's closing balance, positive on the side it sits on.</param>
/// <param name="ClosingSide">Which side that is; null when the account is at zero.</param>
/// <param name="MissingBuySideUah">How much of the closing balance sits on the credit side - cost of
/// sales booked against stock that never entered the books.</param>
/// <param name="Complete">The account is at zero or on its debit side.</param>
public sealed record StockCheck(
    decimal InventoryClosingUah,
    NormalBalance? ClosingSide,
    decimal MissingBuySideUah,
    bool Complete);
