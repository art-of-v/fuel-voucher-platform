using FuelFlow.Features.Ledger;
using FuelFlow.Features.Ledger.SharedModels;
using FuelFlow.Features.Vouchers.SharedModels;
using FuelFlow.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FuelFlow.Features.Vouchers.Redemptions;

/// <summary>
/// Writes the sale a redemption represents as provisional journal entries.
/// </summary>
/// <remarks>
/// <para>
/// A report is not a confirmation. The entries are composed and numbered at the report, but they stay
/// drafts until an operator accepts the supplier's evidence (planning #684). The source id is the
/// voucher id, so a confirmation posts every draft of the redemption together.
/// </para>
/// <para>
/// <b>The advance is released gross, and the VAT is not split again.</b> The VAT inside the price was
/// carved out when the money arrived - <c>payment.received.vat</c> debits the deduction account and
/// credits the liability - so the deliverable's job is only to move the whole advance out of 681 and
/// into revenue. Releasing the net instead, with a second VAT pair, left the VAT portion of an earned
/// advance sitting on 681 as an obligation to a customer who has already drawn the fuel, and it carved
/// the same VAT out twice (planning #780: 2 298.72 stranded on 681 at October's close).
/// </para>
/// <para>
/// <b>How much is one unit's revenue.</b> The line is sold as a whole, so a unit carries
/// <c>line total ÷ quantity</c> - not the whole line, which recognised a 10-pack ten times over
/// (planning #779). The division is exact to the kopeck for every line sold at October's prices; when
/// it is not, the difference is at most one kopeck per line and shows up on 681 at close rather than
/// being absorbed by a rounding plug.
/// </para>
/// </remarks>
public sealed class RedemptionJournalPoster
{
    private readonly ApplicationDbContext _context;
    private readonly JournalPostingService _journal;
    private readonly ILogger<RedemptionJournalPoster> _logger;

    public RedemptionJournalPoster(
        ApplicationDbContext context,
        JournalPostingService journal,
        ILogger<RedemptionJournalPoster> logger)
    {
        _context = context;
        _journal = journal;
        _logger = logger;
    }

    public async Task PostProvisionalAsync(
        Guid voucherId,
        decimal liters,
        Guid actorUserId,
        DateTime occurredAtUtc,
        CancellationToken cancellationToken = default)
    {
        var voucher = await _context.FuelVouchers
            .AsNoTracking()
            .Where(v => v.Id == voucherId)
            .Select(v => new
            {
                v.Id,
                v.CostPerLiter,
                v.OrderId,
            })
            .SingleAsync(cancellationToken);

        var id = voucher.Id;
        var entryDate = DateOnly.FromDateTime(occurredAtUtc);
        var cost = Math.Round(liters * (voucher.CostPerLiter ?? 0m), 2, MidpointRounding.AwayFromZero);

        if (cost > 0m)
        {
            await _journal.PostProvisionalAsync(
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
            _logger.LogWarning(
                "PAYMENTS: voucher {VoucherId} was reported with no cost per liter recorded, so no "
                + "cost of sales draft was written. Run the batch cost command for its import before "
                + "the month closes, or the margin on this sale is overstated.",
                id);
        }

        if (voucher.OrderId is null)
        {
            _logger.LogWarning(
                "PAYMENTS: voucher {VoucherId} was reported with no order, so no revenue draft was "
                + "written - only cost of sales.",
                id);
            return;
        }

        var orderLine = await (
            from movement in _context.VoucherMovements.AsNoTracking()
            join line in _context.OrderLineItems.AsNoTracking() on movement.LineItemId equals line.Id
            where movement.VoucherId == id
            select new { line.LineTotal, line.Quantity })
            .FirstOrDefaultAsync(cancellationToken);

        if (orderLine is null || orderLine.LineTotal <= 0m)
        {
            _logger.LogWarning(
                "PAYMENTS: voucher {VoucherId} was reported but its order line carried no amount, so "
                + "no revenue draft was written; only cost of sales.",
                id);
            return;
        }

        // One voucher is one unit of the line, so it carries one unit's revenue. Booking the whole
        // line gave every voucher on a multi-unit line the full amount - a 10-pack recognised ten
        // times its sale (planning #779). quantity is the divisor the database guarantees positive
        // (ck_order_line_positive_quantity).
        var share = Math.Round(orderLine.LineTotal / orderLine.Quantity, 2, MidpointRounding.AwayFromZero);

        await _journal.PostProvisionalAsync(
            new PostJournalEntryRequest
            {
                SourceEvent = "voucher.redeemed.revenue",
                SourceType = JournalSourceType.Fulfillment,
                SourceId = id,
                EntryDate = entryDate,
                AmountUah = share,
                Description = "Виручка від викупу ваучера",
                VoucherId = id,
                PostedByUserId = actorUserId,
            },
            cancellationToken);
    }
}
