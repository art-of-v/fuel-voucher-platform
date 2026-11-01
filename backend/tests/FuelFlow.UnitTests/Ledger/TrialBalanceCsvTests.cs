using FluentAssertions;
using FuelFlow.Features.Acquiring.UnappliedReceipts;
using FuelFlow.Features.Ledger.GetTrialBalance;
using FuelFlow.Features.Ledger.SharedModels;
using Xunit;

namespace FuelFlow.UnitTests.Ledger;

/// <summary>
/// The close check is opened in Excel by the same accountant who opens the reconciliation act, so it
/// goes through the same cell guard. Nothing a payer types reaches these columns today - they are our
/// chart of accounts plus the acquirer's own reference numbers - which is why this asserts the rule
/// rather than a live attack.
/// </summary>
public class TrialBalanceCsvTests
{
    [Fact]
    public void TextCellsCarryTheFormulaMarkerLikeEveryOtherCsv()
    {
        var csv = TrialBalanceCsv.Render(ReportWith(
            new TrialBalanceAccount(
                "=SUM(1,2)",
                "-2+3",
                AccountType.Asset,
                NormalBalance.Debit,
                0m,
                NormalBalance.Debit,
                10m,
                0m,
                10m,
                NormalBalance.Debit),
            new UnbookedCapture("INV1", 10m, new DateTime(2026, 10, 31, 10, 0, 0, DateTimeKind.Utc), 1, "@RRN")));

        csv.Should().Contain("\"'=SUM(1,2)\"");
        csv.Should().Contain("\"'-2+3\"");
        csv.Should().Contain("\"'@RRN\"");
    }

    [Fact]
    public void MoneyCellsStayNumbers()
    {
        var csv = TrialBalanceCsv.Render(ReportWith(
            new TrialBalanceAccount(
                "311",
                "Bank",
                AccountType.Asset,
                NormalBalance.Debit,
                -5m,
                NormalBalance.Credit,
                10.50m,
                0m,
                5.50m,
                NormalBalance.Debit),
            new UnbookedCapture("INV1", 10m, new DateTime(2026, 10, 31, 10, 0, 0, DateTimeKind.Utc), 1, "RRN1")));

        csv.Should().Contain("-5.00");
        csv.Should().Contain("10.50");
        csv.Should().NotContain("'-5.00");
    }

    private static TrialBalanceResponse ReportWith(TrialBalanceAccount account, UnbookedCapture capture) => new(
        FromDate: new DateOnly(2026, 10, 1),
        ToDate: new DateOnly(2026, 10, 31),
        Accounts: [account],
        Totals: new TrialBalanceTotals(10m, 10m, true),
        Drafts: [],
        Bank: new BankCheck("FUEL FLOW", 10m, 1, 10m, 0m, 10m, 0m, true),
        Vat: new VatCheck(0m, 0m, 0m, 0m, 0m, 0m, true),
        Stock: new StockCheck(0m, null, 0m, true),
        UnbookedCaptures: [capture]);
}
