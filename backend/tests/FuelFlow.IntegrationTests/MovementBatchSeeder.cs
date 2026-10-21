using FuelFlow.Features.Vouchers;
using FuelFlow.Features.Vouchers.Exchange;
using FuelFlow.Features.Vouchers.Movements;
using FuelFlow.Persistence;

namespace FuelFlow.IntegrationTests;

/// <summary>
/// Seeds the <see cref="MovementBatch"/> parent that an exchange's ledger rows point at, and the
/// pair rows themselves.
/// </summary>
/// <remarks>
/// <para>
/// Since #319 the batch reference is a real foreign key, so a fixture that writes exchange rows has to
/// write the settlement they belong to — the same thing <c>ConfirmVoucherExchangeCommandHandler</c>
/// does in one transaction. Keeping that in one helper means a fixture states <i>what the settlement
/// was</i> and not four times how a row of it is spelled.
/// </para>
/// <para>
/// The surcharge passed here is the amount the brand charged for the whole settlement, which is what
/// the handler records and what <c>movement_batches</c> stores. It deliberately does not have to equal
/// the sum of the member rows' shares: those are <c>surcharge × pair_litres / total_new_liters</c>, so
/// they only add up when every retired voucher was paired with a replacement.
/// </para>
/// </remarks>
internal static class MovementBatchSeeder
{
    public static MovementBatch ForExchange(
        Guid batchId,
        Guid supplierId,
        decimal surchargeUah,
        DateTime atUtc,
        string? operatorName = null)
    {
        var added = new MovementBatch
        {
            Id = batchId,
            Kind = MovementBatchKinds.VoucherExchange,
            SupplierId = supplierId,
            Status = MovementBatchStatuses.Confirmed,
            SurchargeUah = surchargeUah,
            OpenedByUserId = Guid.NewGuid(),
            OpenedByUserName = operatorName,
            OpenedAtUtc = atUtc,
            ConfirmedAtUtc = atUtc,
            CreatedAtUtc = atUtc
        };

        return added;
    }

    /// <summary>Adds the parent to a context, for fixtures that build rows and save them themselves.</summary>
    public static MovementBatch AddForExchange(
        ApplicationDbContext seed,
        Guid batchId,
        Guid supplierId,
        decimal surchargeUah,
        DateTime atUtc,
        string? operatorName = null)
    {
        var batch = ForExchange(batchId, supplierId, surchargeUah, atUtc, operatorName);
        seed.MovementBatches.Add(batch);
        return batch;
    }

    /// <summary>
    /// The ledger's row of a retired voucher: the paper went to the supplier, and this row carries the
    /// pair's three facts - which paper replaced it, that pair's share of the doplata, and the cost
    /// stamped on the replacement (planning #319).
    /// </summary>
    public static VoucherMovement AddExchangeOut(
        ApplicationDbContext seed,
        Guid batchId,
        Guid oldVoucherId,
        Guid? newVoucherId,
        decimal surchargeShareUah,
        decimal? appliedCostPerLiter,
        DateTime atUtc)
    {
        var movement = new VoucherMovement
        {
            Id = Guid.NewGuid(),
            BatchId = batchId,
            Kind = VoucherMovementKinds.ExchangeOut,
            VoucherId = oldVoucherId,
            FromLocation = VoucherLocations.Pool,
            ToLocation = VoucherLocations.Supplier,
            ActorKind = VoucherActorKinds.User,
            ActorUserId = Guid.NewGuid(),
            OccurredAtUtc = atUtc,
            ReplacedByVoucherId = newVoucherId,
            SurchargeShareUah = surchargeShareUah,
            AppliedCostPerLiter = appliedCostPerLiter
        };

        seed.VoucherMovements.Add(movement);
        return movement;
    }
}
