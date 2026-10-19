using FuelFlow.Features.Ledger;
using FuelFlow.Features.Ledger.SharedModels;
using FuelFlow.Features.Vouchers.SharedModels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FuelFlow.Features.Vouchers.MarkVoucherAsUsed;

public sealed partial class MarkVoucherAsUsedCommandHandler
{
    /// <summary>The VAT rate the business is registered for. See <see cref="VatCalculator"/>.</summary>
    private const decimal VatRate = VatCalculator.StandardRate;

    /// <summary>
    /// Everything the posting needs about a redeemed voucher, read once and passed on. Named rather
    /// than projected anonymously so the posting code cannot reach for a field the query does not
    /// carry - a missing field should be a compile error, not a null at runtime.
    /// </summary>
    private sealed record RedeemedVoucher(
        Guid Id,
        VoucherStatus Status,
        Guid? WorkerUserId,
        Guid? AssignedToUserId,
        decimal Liters,
        decimal? CostPerLiter,
        Guid? OrderId);

    /// <summary>
    /// Books the sale. Redemption is where the advance stops being an obligation and becomes
    /// revenue - the customer got the fuel, so the money is earned - and it is where the stock
    /// leaves the warehouse, so it is also where cost of sales belongs.
    // <para>
    /// Cost of sales is recognised <b>here rather than at purchase</b>, deliberately (decided
    /// 2026-10-19). The alternative - Dr 808 / Cr 281 when the stock is bought - is simpler and
    /// needs nothing stored on the voucher, but it reports profit before anything was sold, and a
    /// month where stock was bought and not redeemed would show a loss that never happened. Paying
    /// it here means it is taken from <c>fuel_vouchers.cost_per_liter</c>, the price the supplier
    /// actually charged, snapshotted at import.
    // </para>
    /// </summary>
    private async Task PostSaleToJournalAsync(
        RedeemedVoucher voucher,
        Guid actorUserId,
        DateTime occurredAtUtc,
        CancellationToken cancellationToken)
    {
        var id = voucher.Id;
        var liters = voucher.Liters;
        var cost = Math.Round(liters * (voucher.CostPerLiter ?? 0m), 2, MidpointRounding.AwayFromZero);

        var entryDate = DateOnly.FromDateTime(occurredAtUtc);

        // Stock left the warehouse whatever the sale side says, so this posts unconditionally. A
        // voucher with no recorded cost posts zero rather than being skipped: it still consumed
        // inventory, and silently omitting the line would be the same hole as omitting revenue.
        // <para>
        // <b>Known wrinkle, deliberately not solved here.</b> <c>fuel_vouchers.cost_per_liter</c> is
        // mutable - <c>SetBatchCostCommandHandler</c> overwrites it - so restamping a batch's cost
        // after some of its vouchers were redeemed would restate cost of sales for those past
        // redemptions. The movement log already records what this cost was at the time
        /// (<c>cost_delta_uah</c>) for exactly this reason, and that is where the fix belongs: reading
        // the voucher column is correct while nobody restamps, and wrong the moment they do. Fixing
        // it properly means deciding what a restamp after a redemption means, which is an
        // accounting question rather than a code one.
        // </para>
if (cost > 0m)
        {
            await _journal.PostAsync(
                new PostJournalEntryRequest
                {
                    SourceEvent = "voucher.redeemed.cogs",
                    SourceType = JournalSourceType.Fulfillment,
                    SourceId = id,
                    EntryDate = entryDate,
                    AmountUah = cost,
                    Description = "Собівартість викупу ваучера",
                    VoucherId = id,
                    QuantityLiters = liters,
                    PostedByUserId = actorUserId,
                },
                cancellationToken);
        }
        else
        {
            // Loud, not silent. A voucher imported by a brand parser carries no cost - those parsers
            // read the QR and nothing else - so cost arrives later via SetBatchCostCommandHandler. If
            // it never did, booking zero would make the expense vanish with nothing to complain:
            // a book that is short and confident, discovered at month close. The revenue above still
            // posts, so this is a margin that is wrong rather than an entry that is missing.
            _logger.LogWarning(
                "PAYMENTS: voucher {VoucherId} was redeemed with no cost per liter recorded, so no "
                + "cost of sales was booked for it. Run the batch cost command for its import before "
                + "the month closes, or the margin on this sale is overstated.",
                id);
        }

        if (voucher.OrderId is null)
        {
            // A held voucher always carries an order (ck_voucher_held_has_order), so this is
            // unreachable for a voucher the handler accepted. It is here because the ledger must
            // never post revenue it cannot source, and "unreachable" is a claim about today's
            // schema rather than about the next one.
            _logger.LogWarning(
                "PAYMENTS: voucher {VoucherId} was redeemed with no order, so no revenue was "
                + "recognised for it - only cost of sales.",
                id);
            return;
        }

        // The price comes from the order line this voucher was issued against, reached through the
        // custody movement's line_item_id. NOT through OrderLineItem.SourceVoucherId: that column is
        // written only by the renewal branch, and ck_order_line_renewal_pair_is_whole forbids it
        // on a plain first purchase. Matching on brand and fuel type instead would be a guess, and
        // two vouchers for the same fuel on one order would make it a wrong one.
        var lineTotal = await (
            from movement in _context.VoucherMovements.AsNoTracking()
            join line in _context.OrderLineItems.AsNoTracking() on movement.LineItemId equals line.Id
            where movement.VoucherId == id
            select (decimal?)line.LineTotal)
            .FirstOrDefaultAsync(cancellationToken);

        if (lineTotal is null or <= 0m)
        {
            _logger.LogWarning(
                "PAYMENTS: voucher {VoucherId} redeemed but its order line carried no amount, so no "
                + "revenue was recognised; only cost of sales posted.",
                id);
            return;
        }

        var (netUah, vatUah) = VatCalculator.Split(lineTotal.Value, VatRate);

        await _journal.PostAsync(
            new PostJournalEntryRequest
            {
                SourceEvent = "voucher.redeemed.revenue",
                SourceType = JournalSourceType.Fulfillment,
                SourceId = id,
                EntryDate = entryDate,
                AmountUah = netUah,
                Description = "Виручка від викупу ваучера",
                VoucherId = id,
                PostedByUserId = actorUserId,
            },
            cancellationToken);

        await _journal.PostAsync(
            new PostJournalEntryRequest
            {
                SourceEvent = "voucher.redeemed.vat",
                SourceType = JournalSourceType.Fulfillment,
                SourceId = id,
                EntryDate = entryDate,
                AmountUah = vatUah,
                Description = "ПДВ з викупу ваучера",
                VoucherId = id,
                PostedByUserId = actorUserId,
            },
            cancellationToken);
    }
}