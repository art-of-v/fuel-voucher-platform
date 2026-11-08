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

    /// <summary>
    /// The settle core around the same journal: the webhook handler no longer posts on its own, it
    /// calls this, so a test that drives a paid webhook needs the core rather than the ledger.
    /// </summary>
    public static FuelFlow.Features.Orders.SettlePaidOrder.SettlePaidOrderCommandHandler SettleFor(
        ApplicationDbContext context)
        => new(
            context,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<
                FuelFlow.Features.Orders.SettlePaidOrder.SettlePaidOrderCommandHandler>.Instance,
            For(context));

    /// <summary>
    /// The settle behind the money check: the paths that hold money and an order - the settlement
    /// channel and the panel's place screen - call this, so a test that drives either needs it rather
    /// than the core alone.
    /// </summary>
    public static FuelFlow.Features.Orders.SettlePaidOrder.TrySettlePaidOrderCommandHandler TrySettleFor(
        ApplicationDbContext context)
        => new(
            context,
            SettleFor(context),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<
                FuelFlow.Features.Orders.SettlePaidOrder.TrySettlePaidOrderCommandHandler>.Instance);

    /// <summary>
    /// The held-money finder on top of the try-settle: the checkout asks it whether money declared
    /// before the order existed pays for it, so a test that drives a checkout needs it in the
    /// handler's constructor rather than a stub that says no.
    /// </summary>
    public static FuelFlow.Features.Orders.SettlePaidOrder.SettleFromHeldMoneyCommandHandler HeldMoneyFor(
        ApplicationDbContext context)
        => new(
            context,
            TrySettleFor(context),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<
                FuelFlow.Features.Orders.SettlePaidOrder.SettleFromHeldMoneyCommandHandler>.Instance);

    /// <summary>Seeds what the seeded mapping needs: both sides of the payment events.</summary>
    public static void SeedChart(ApplicationDbContext context)
    {
        if (context.LedgerMappingRules.Any(r => r.SourceEvent == "payment.received"))
            return;

        var cash = NewAccount(context, "311", "Рахунки в банках", AccountType.Asset, NormalBalance.Debit);
        var advance = NewAccount(context, "681", "Рахунки за авансами одержаними", AccountType.Liability, NormalBalance.Credit);
        var contra = NewAccount(context, "704", "Вирахування з доходу", AccountType.Revenue, NormalBalance.Debit);
        var vat = NewAccount(context, "643", "Податкові зобов'язання", AccountType.Liability, NormalBalance.Credit);

        context.LedgerMappingRules.AddRange(
            Map("payment.received", cash, NormalBalance.Debit),
            Map("payment.received", advance, NormalBalance.Credit),
            Map("payment.received.vat", contra, NormalBalance.Debit),
            Map("payment.received.vat", vat, NormalBalance.Credit),
            // The release of that same carve, which a delivery outside the tax perimeter writes.
            Map("payment.received.vat.out_of_perimeter", vat, NormalBalance.Debit),
            Map("payment.received.vat.out_of_perimeter", contra, NormalBalance.Credit));

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