using FuelFlow.Features.Ledger;
using FuelFlow.Features.Ledger.SharedModels;
using FuelFlow.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace FuelFlow.UnitTests;

/// <summary>
/// Builds the journal dependency for a payment-handler test.
/// </summary>
/// <remarks>
/// The handler now posts to the ledger on every settled payment, so a test that drives one has to
/// have a chart - not because the journal is part of what it asserts, but because an unmapped event
/// is refused loudly and a silently absent one would be the exact bug this wiring exists to prevent.
/// Seeding the real chart here keeps that refusal intact and stops each fixture inventing its own.
/// </remarks>
public static class TestJournal
{
    public static JournalPostingService For(ApplicationDbContext context)
        => new(context, NullLogger<JournalPostingService>.Instance);

    /// <summary>
    /// Seeds the minimum the seeded mapping needs: both sides of <c>payment.received</c> and of
    /// <c>payment.received.vat</c>. Synchronous on purpose - the test fixtures build the handler in
    /// their constructors, which cannot await.
    /// </summary>
    public static void SeedChart(ApplicationDbContext context)
    {
        if (context.LedgerMappingRules.Any(r => r.SourceEvent == "payment.received"))
            return;

        var cash = NewAccount(context, "311", "Рахунки в банках", "Asset", "Debit");
        var advance = NewAccount(context, "681", "Рахунки за авансами одержаними", "Liability", "Credit");
        var contra = NewAccount(context, "704", "Вирахування з доходу", "Revenue", "Debit");
        var vat = NewAccount(context, "643", "Податкові зобов'язання", "Liability", "Credit");

        context.LedgerMappingRules.AddRange(
            Map("payment.received", cash, "Debit"),
            Map("payment.received", advance, "Credit"),
            Map("payment.received.vat", contra, "Debit"),
            Map("payment.received.vat", vat, "Credit"));

        context.SaveChanges();
    }

    private static LedgerMappingRule Map(string sourceEvent, Guid accountId, string side) => new()
    {
        Id = Guid.NewGuid(),
        SourceType = JournalSourceType.Payment,
        SourceEvent = sourceEvent,
        AccountId = accountId,
        Side = side == "Debit" ? NormalBalance.Debit : NormalBalance.Credit,
    };

    private static Guid NewAccount(ApplicationDbContext context, string code, string name, string type, string balance)
    {
        var id = Guid.NewGuid();
        context.ChartOfAccounts.Add(new ChartOfAccount
        {
            Id = id,
            Code = code,
            NameUkr = name,
            AccountType = Enum.Parse<AccountType>(type),
            NormalBalance = Enum.Parse<NormalBalance>(balance),
            IsPostable = true,
            IsActive = true,
        });
        return id;
    }
}