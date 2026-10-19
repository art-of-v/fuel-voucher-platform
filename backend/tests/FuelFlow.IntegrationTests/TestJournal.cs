using FuelFlow.Features.Ledger;
using FuelFlow.Features.Ledger.SharedModels;
using FuelFlow.Persistence;

namespace FuelFlow.IntegrationTests;

/// <summary>
/// The journal dependency for a payment test, plus the chart it needs.
/// </summary>
/// <remarks>
/// The webhook handler posts to the ledger on every settled payment, so a test that drives one has
/// to have a chart - not because the journal is part of what it asserts, but because an unmapped
/// event is refused loudly, and a silently absent one would be the exact bug this wiring exists to
/// prevent. Seeding the real chart keeps that refusal intact and stops each fixture inventing its own.
/// <para>
/// On a per-class cloned database the seeding is idempotent, so it is safe to call from a
/// constructor.
/// </para>
/// </remarks>
public static class TestJournal
{
    public static JournalPostingService For(ApplicationDbContext context)
        => new(context, Microsoft.Extensions.Logging.Abstractions.NullLogger<JournalPostingService>.Instance);

    /// <summary>Seeds what the seeded mapping needs: both sides of the payment events.</summary>
    public static void SeedChart(ApplicationDbContext context)
    {
        if (context.LedgerMappingRules.Any(r => r.SourceEvent == "payment.received"))
            return;

        var cash = NewAccount(context, "311", "Рахунки в банках", AccountType.Asset, NormalBalance.Debit);
        var advance = NewAccount(context, "681", "Рахунки за авансами одержаними", AccountType.Liability, NormalBalance.Credit);
        var contra = NewAccount(context, "703", "Вирахування з доходу", AccountType.Revenue, NormalBalance.Debit);
        var vat = NewAccount(context, "643", "Податкові зобов'язання", AccountType.Liability, NormalBalance.Credit);

        context.LedgerMappingRules.AddRange(
            Map("payment.received", cash, NormalBalance.Debit),
            Map("payment.received", advance, NormalBalance.Credit),
            Map("payment.received.vat", contra, NormalBalance.Debit),
            Map("payment.received.vat", vat, NormalBalance.Credit));

        context.SaveChanges();
    }

    private static LedgerMappingRule Map(string sourceEvent, Guid accountId, NormalBalance side) => new()
    {
        Id = Guid.NewGuid(),
        SourceType = JournalSourceType.Payment,
        SourceEvent = sourceEvent,
        AccountId = accountId,
        Side = side,
    };

    private static Guid NewAccount(
        ApplicationDbContext context, string code, string name, AccountType type, NormalBalance balance)
    {
        var id = Guid.NewGuid();
        context.ChartOfAccounts.Add(new ChartOfAccount
        {
            Id = id,
            Code = code,
            NameUkr = name,
            AccountType = type,
            NormalBalance = balance,
            IsPostable = true,
            IsActive = true,
        });
        return id;
    }
}