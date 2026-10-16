using FuelFlow.Features.Vouchers.Exchange;
using FuelFlow.Persistence;

namespace FuelFlow.IntegrationTests;

/// <summary>
/// Seeds the <see cref="MovementBatch"/> parent that a <see cref="VoucherExchange"/> row points at.
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
}