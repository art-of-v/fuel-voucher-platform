using FuelFlow.Features.Ledger;
using FuelFlow.Features.Ledger.SharedModels;
using FuelFlow.Features.Orders.SharedModels;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using FuelFlow.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Orders.SettlePaidOrder;

/// <summary>
/// Takes back the part of the VAT that <c>payment.received.vat</c> carved out of an order's payment
/// which was delivered from stock acquired on the route that sits outside the tax perimeter.
/// </summary>
/// <remarks>
/// <para>
/// <b>The hole this closes.</b> The receipt carves VAT out of every payment the moment the money
/// lands, because at that moment nobody knows which route the fuel will come in on: a line item names
/// a brand, a fuel and a volume, and no line names a supplier — the route is decided when
/// <c>FulfillmentService</c> picks the vouchers, and it picks them by expiry date. So the receipt
/// books the whole payment inside the perimeter, and until now nothing ever revisited it, which left
/// every sale delivered from outside-perimeter stock asserting an output VAT liability to the state
/// for a supply the accounting decision (docs/ACQUISITION_CHANNELS.md §3) puts outside the perimeter
/// entirely. §3's own consequence is the rule being enforced here: a voucher on that route
/// "must not reach a VAT account".
/// </para>
/// <para>
/// <b>Why the carve is not simply moved to the delivery.</b> Decision D2 of
/// docs/LEDGER_CLOSE_2026-10.md fixes the VAT split at the first event — "once per payment, at the
/// receipt, and never again" — and the tax point really is the payment: the fiscal document for the
/// advertising service is issued for the money, not for the fuel that comes later. Moving the carve
/// would move the tax point. What the delivery is allowed to say is that the liability the receipt
/// asserted does not stand for the part it could not have known about, which is a release.
/// </para>
/// <para>
/// <b>Its shape is the refund's.</b> <c>payment.refunded.vat</c> (Dr 643 / Cr 704) already releases
/// the carve when the advance comes back, with the same formula and the same rate the receipt used
/// rather than a number apportioned off its entry. This is the same mirror on the
/// other reason the carve was wrong: not that the money returned, but that the goods went out on a
/// route with no taxable supply behind it. 704 is the deduction account (D3), so a credit there is
/// the income deduction coming back, and nothing new is added to the chart.
/// </para>
/// <para>
/// <b>One split, over the delivered amount.</b> The gross delivered outside the perimeter is summed
/// first and divided once, exactly as D2 requires ("the split is taken per payment … not taken per
/// line, per voucher"), because a VAT per voucher added up is the re-splitting defect that produced
/// October's three-kopeck difference between the books and the drafts. What this one does read off
/// the books is the ceiling — what remains of the amount the receipt actually carved after the
/// order's other corrections, so a refund and this release together cannot divide the same carve
/// into a kopeck more than the receipt put there. That residual stays on 643 where the close check
/// sees it, which is the same treatment D2 gives the revenue residual on 681. It may never release
/// what was never asserted.
/// </para>
/// <para>
/// <b>What it refuses to guess at.</b> A delivery that names no order line cannot be valued, and
/// valuing only the rows that do name one would understate the outside-perimeter part and still book
/// a confident number — so such an order releases nothing and says so. The same is true of a sale
/// that was never delivered, of one delivered entirely on the route inside the perimeter (where the
/// receipt was right and there is nothing to correct), and of one whose receipt booked no VAT at all.
/// </para>
/// <para>
/// <b>One release per order, and what that costs.</b> The key is the order, so a fulfilment that
/// replays gets this entry back instead of writing a second one — which is the whole point, and also
/// the limit: if the reconciliation later takes a voucher back and a different mix is issued, this
/// entry does not follow the new mix. That is the correct behaviour, not a gap. A posted entry is
/// immutable and its <see cref="FuelFlow.Features.Ledger.SharedModels.JournalEntryStatus"/> says so:
/// a book that has become wrong is answered by the opposite entry, and a background job rewriting a
/// signed number behind the accountant's back is how a trial balance stops being evidence.
/// </para>
/// </remarks>
public sealed class ReleaseVatOutsideThePerimeterCommandHandler
{
    /// <summary>The event, and the key its two accounts are mapped under.</summary>
    public const string ReleaseEvent = "payment.received.vat.out_of_perimeter";

    private readonly ApplicationDbContext _context;
    private readonly JournalPostingService _journal;
    private readonly ILogger<ReleaseVatOutsideThePerimeterCommandHandler> _logger;

    public ReleaseVatOutsideThePerimeterCommandHandler(
        ApplicationDbContext context,
        JournalPostingService journal,
        ILogger<ReleaseVatOutsideThePerimeterCommandHandler> logger)
    {
        _context = context;
        _journal = journal;
        _logger = logger;
    }

    /// <summary>
    /// Posts the release for a delivery that just completed, inside the caller's transaction.
    /// </summary>
    /// <returns>
    /// What came off 643. Zero means nothing came off — either there was nothing to release, or the
    /// posting was refused and the log says which; the caller is never made responsible for the books.
    /// </returns>
    public async Task<decimal> HandleAsync(
        ReleaseVatOutsideThePerimeterCommand command,
        CancellationToken cancellationToken = default)
    {
        // A replay is answered before the delivery is read again: the entry is the fact, and an order
        // whose vouchers were later taken back would otherwise report a release that is already booked
        // as nothing at all. This is also the answer the operator endpoint promises on a second press.
        var alreadyReleased = await CarvedVat.PostedAmountAsync(
            _context, ReleaseEvent, JournalSourceType.Payment, command.OrderId, cancellationToken);
        if (alreadyReleased > 0m)
        {
            _logger.LogInformation(
                "VAT PERIMETER: order {OrderId} already released {Release:0.00} of the VAT its receipt "
                + "carved; not releasing it again.", command.OrderId, alreadyReleased);
            return alreadyReleased;
        }

        // Not IncludingSoftDeleted: an order that was taken out of view after its money arrived is a
        // case for the refund and repair paths, and releasing its VAT here would edit the same books
        // from a second direction that has no idea what the first one already did to them.
        var order = await _context.Orders
            .AsNoTracking()
            .Where(o => o.Id == command.OrderId)
            .Select(o => new { o.Price, o.UserId })
            .FirstOrDefaultAsync(cancellationToken);

        if (order is null)
        {
            _logger.LogWarning(
                "VAT PERIMETER: order {OrderId} has no row in view, so the VAT its receipt carved was "
                + "left standing.", command.OrderId);
            return 0m;
        }

        // The deliveries that stand. Rows the reconciliation took back are excluded by the query
        // filter on released_at_utc, which is what makes a corrected order release the corrected
        // amount rather than the amount once promised.
        var claimed = await _context.Fulfillments
            .AsNoTracking()
            .Where(f => f.OrderId == command.OrderId)
            .Select(f => new { f.VoucherId, f.LineItemId })
            .ToListAsync(cancellationToken);

        if (claimed.Count == 0)
            return 0m;

        if (claimed.Any(c => c.LineItemId is null))
        {
            _logger.LogWarning(
                "VAT PERIMETER: order {OrderId} delivered {Count} voucher(s) and at least one names no "
                + "order line, so nothing of the VAT its receipt carved was released. Fix the line "
                + "attribution first: a release summed over only the rows that do name a line would "
                + "book a confident number for a delivery whose size is not actually known.",
                command.OrderId, claimed.Count);
            return 0m;
        }

        // Read as three queries rather than as one over the navigations, because what is being asked
        // for here is the row's own recorded fact, and a missing one has to be an answer rather than a
        // dropped row: an inner join would quietly shrink a delivery nobody decided to shrink.
        var voucherIds = claimed.Select(c => c.VoucherId).Distinct().ToArray();
        var routes = await _context.FuelVouchers
            .AsNoTracking()
            .Where(v => voucherIds.Contains(v.Id))
            .ToDictionaryAsync(v => v.Id, v => v.AcquisitionSource, cancellationToken);

        var lineIds = claimed.Select(c => c.LineItemId!.Value).Distinct().ToArray();
        var lines = await _context.OrderLineItems
            .AsNoTracking()
            .Where(l => lineIds.Contains(l.Id))
            .ToDictionaryAsync(l => l.Id, cancellationToken);

        var unvalued = claimed
            .Where(c => !routes.TryGetValue(c.VoucherId, out _) || !lines.ContainsKey(c.LineItemId!.Value))
            .Count();

        if (unvalued > 0)
        {
            _logger.LogWarning(
                "VAT PERIMETER: order {OrderId} delivered {Count} voucher(s) whose route or line row is "
                + "not in view, so nothing of the VAT its receipt carved was released.",
                command.OrderId, unvalued);
            return 0m;
        }

        // One voucher is one unit of its line, so it carries one unit's money — the same share the
        // redemption recognises as revenue, computed the same way, which is what keeps the amount
        // released from disagreeing with the amount booked.
        var outsideGross = claimed
            .Where(c => routes[c.VoucherId] == AcquisitionSources.Partner)
            .GroupBy(c => c.LineItemId!.Value)
            .Sum(g => g.Count() * Math.Round(
                lines[g.Key].LineTotal / lines[g.Key].Quantity, 2, MidpointRounding.AwayFromZero));

        if (outsideGross <= 0m)
            return 0m;

        // What the receipt actually asserted and what still stands of it, read off its own entry
        // rather than recomputed from the price: the release is a correction of that figure, so the
        // figure it may correct is the most authoritative one there is, and a hand-edited book cannot
        // be made to go negative by a job.
        //
        // The account the carve sits on comes from the carve's own credit line, and nothing here names
        // one. The chart is the only place an account is decided (docs/ACQUISITION_CHANNELS.md §4 and
        // the mapping rule's own purpose), and a class that said "643" would be a second copy of a
        // decision the accountant owns.
        var carve = await CarvedVat.ForOrderAsync(_context, command.OrderId, cancellationToken);
        var carved = carve.CarvedUah;

        const decimal vatRate = VatCalculator.StandardRate;

        // The rate and the formula are written here as the receipt writes them, so that a sale which
        // stops being standard-rated changes both call sites together and the release cannot disagree
        // with its own carve. The ceiling is what remains, not the whole carve: a refund of the same
        // order may already have released its share, and the divisions of the halves can round a
        // kopeck past the single division the receipt made.
        var release = Math.Min(VatCalculator.VatFromGross(outsideGross, vatRate), carve.RemainingUah);

        if (release <= 0m)
        {
            if (carved <= 0m)
            {
                // A promo order is the exception that proves the check: the 2026-11-07 ruling has it
                // book nothing at all, so an empty carve is the expected state and not a book to
                // repair. Only an inside-perimeter sale with no receipt of its own is one.
                if (await OrderPayment.IsPromoOrderAsync(_context, command.OrderId, cancellationToken))
                {
                    _logger.LogInformation(
                        "VAT PERIMETER: order {OrderId} is a promo order, which books nothing, so it "
                        + "has no VAT to release.", command.OrderId);
                    return 0m;
                }

                // Nothing was carved, so there is nothing to release. The settle is the only door to a
                // delivered order and it books the carve in the same transaction, so reaching here
                // means the receipt was never booked at all - which is a book to be repaired by hand,
                // not a number to be invented here.
                _logger.LogWarning(
                    "VAT PERIMETER: order {OrderId} delivered {Outside:0.00} on the route outside the "
                    + "perimeter, but no VAT of its own is booked against it ({Carved:0.00}), so nothing "
                    + "was released. The receipt is missing, not the release.",
                    command.OrderId, outsideGross, carved);
            }
            else
            {
                // The correction is legitimate, but every hryvnia of this order's carve is already
                // off the liability - another release for the same order, or a refund of it, got
                // there first.
                _logger.LogInformation(
                    "VAT PERIMETER: order {OrderId} delivered {Outside:0.00} on the route outside the "
                    + "perimeter, but its carve ({Carved:0.00} UAH) is already released "
                    + "({Taken:0.00} UAH); nothing was booked.",
                    command.OrderId, outsideGross, carved, carve.TakenUah);
            }

            return 0m;
        }

        try
        {
            await _journal.PostAsync(
                new PostJournalEntryRequest
                {
                    SourceEvent = ReleaseEvent,
                    // The same family, and the same source id, as the entry it releases: an accountant
                    // reading 643 for one order sees the carve and its correction against one row.
                    SourceType = JournalSourceType.Payment,
                    SourceId = command.OrderId,
                    EntryDate = DateOnly.FromDateTime(SystemClock.UtcNow),
                    AmountUah = release,
                    Description = $"Зняття ПДВ з оплати замовлення {command.OrderId} (видана стока поза податковим периметром)",
                    // The customer, which is who the receipt's two entries name. No person decided this
                    // release; the stock mix did.
                    PostedByUserId = order.UserId,
                },
                cancellationToken);
        }
        catch (Exception ex) when (ex is LedgerPostingException
                                       or LedgerUnmappedEventException
                                       or LedgerPeriodClosedException)
        {
            // The same trade planning #854 made for the refund legs, and for the same reason: a
            // refused posting is logged and left to a person, and it does not undo the delivery.
            // Stopping fuel from reaching a customer who paid would trade a known hole in the books
            // for a customer who was charged and not served, and none of these three failures is
            // something the next run of this job can fix by itself.
            _logger.LogError(
                "VAT PERIMETER: order {OrderId} delivered {Outside:0.00} on the route outside the "
                + "perimeter and the {Release:0.00} VAT release was NOT booked: {Error}. The VAT its "
                + "receipt carved stands until it is posted by hand.",
                command.OrderId, outsideGross, release, ex.Message);
            return 0m;
        }

        _logger.LogInformation(
            "VAT PERIMETER: order {OrderId} delivered {Outside:0.00} of its {Price:0.00} on the route "
            + "outside the perimeter; released {Release:0.00} of the VAT the receipt carved.",
            command.OrderId, outsideGross, order.Price, release);

        return release;
    }
}
