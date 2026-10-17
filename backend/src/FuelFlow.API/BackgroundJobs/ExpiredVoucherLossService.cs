using FuelFlow.Features.Settings;
using FuelFlow.Features.Vouchers;
using FuelFlow.Features.Vouchers.Events;
using FuelFlow.Features.Vouchers.SharedModels;
using FuelFlow.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.API.BackgroundJobs;

/// <summary>
/// Nightly loss-booking sweep for operator stock that lapsed unsold (pricing epic slice 4).
///
/// A voucher the operator imported but never sold sits in the in-stock pool
/// (<c>Imported</c> / <c>VerifiedWithWarnings</c> / <c>Available</c>) until its printed
/// <see cref="FuelVoucher.SupplierExpirationDate"/> passes — after which it can never be sold, yet nothing
/// moves it off the books. Left alone it keeps inflating per-batch P&amp;L "remaining / unrealized
/// margin" (<see cref="Features.Vouchers.PurchaseBatchCost.GetImportBatchPnlQueryHandler"/>) as if it
/// were still sellable. This job retires each lapsed unsold voucher to <see cref="VoucherStatus.Expired"/>,
/// so its cost lands in the batch's realised-loss bucket instead of pretending to be future margin.
///
/// Scope is deliberately operator-owned stock only. A voucher already <c>Assigned</c> to a customer was
/// sold (revenue realised); its expiry is the customer's concern and is handled by the paid
/// renewal/replacement flow, so this job never touches it. Redeemed (<c>Used</c>), <c>Blocked</c>,
/// <c>Deactivated</c> and already-<c>Expired</c> vouchers are likewise out of the candidate set.
///
/// Fail-safe by default: while <c>ExpiredVoucherLoss:Enabled</c> is off (the default) the job runs
/// read-only, logging the loss it *would* book (Σ litres × the batch's specific cost/L from slice 2a)
/// so an admin can gauge the impact before opting in. Only once enabled does it mutate voucher status.
/// The status flip is batched (load tracked + save, mirroring <see cref="DeletedUnpaidOrderCleanupService"/> and
/// <see cref="DataRetentionService"/>): each batch is one small transaction, and because a flipped
/// voucher leaves the in-stock candidate filter the loop naturally drains a large first-run backlog
/// over successive nightly runs rather than one unbounded write.
/// </summary>
public class ExpiredVoucherLossService
{
    private const int BatchSize = 500;

    // The operator-owned, unsold, sellable pool — identical to the P&L handler's remaining-stock set
    // and BlendedCostRecalculator's cost pool. Only these lapse into an operator loss.
    private static readonly VoucherStatus[] InStockStatuses =
        [VoucherStatus.Imported, VoucherStatus.VerifiedWithWarnings, VoucherStatus.Available];

    private readonly ApplicationDbContext _context;
    private readonly RuntimeSettingsService _settings;
    private readonly ILogger<ExpiredVoucherLossService> _logger;

    public ExpiredVoucherLossService(
        ApplicationDbContext context,
        RuntimeSettingsService settings,
        ILogger<ExpiredVoucherLossService> logger)
    {
        _context = context;
        _settings = settings;
        _logger = logger;
    }

    public virtual async Task BookExpiredLossAsync(CancellationToken cancellationToken = default)
    {
        var enabled = await _settings.IsExpiredVoucherLossEnabledAsync(cancellationToken);
        var graceDays = await _settings.GetExpiredVoucherLossGraceDaysAsync(cancellationToken);
        var dryRun = !enabled;

        // Strictly-before "today" so a voucher stays valid through the whole of its expiration date;
        // one clock read shared by the summary aggregate and the batched flip so they can't drift.
        // The grace window means we only retire vouchers whose expiry is older than (today - graceDays).
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var cutoff = today.AddDays(-graceDays);

        // One GROUP BY over the candidate stock: per (import × fuel) how many lapsed and their litres —
        // no per-row load just to total the loss. FuelTypeId encodes supplier+fuel, so joining it to the
        // batch's specific cost/L (slice 2a) values the loss exactly.
        var groups = await _context.FuelVouchers
            .Where(v => InStockStatuses.Contains(v.Status) && v.SupplierExpirationDate < cutoff)
            .GroupBy(v => new { v.ImportJobId, v.FuelTypeId })
            .Select(g => new
            {
                g.Key.ImportJobId,
                g.Key.FuelTypeId,
                Count = g.Count(),
                Liters = g.Sum(v => v.Liters),
                // Loss is booked at each voucher's OWN cost: a voucher a customer paid to extend is worth
                // less than its purchase price, and writing it off at the original price would overstate it.
                CostedValue = g.Where(v => v.CostPerLiter != null)
                    .Sum(v => v.Liters * v.CostPerLiter!.Value),
                CostedLiters = g.Where(v => v.CostPerLiter != null).Sum(v => v.Liters),
            })
            .ToListAsync(cancellationToken);

        if (groups.Count == 0)
        {
            _logger.LogDebug(
                "No expired-unsold vouchers to book (as of {Today:yyyy-MM-dd}, cutoff {Cutoff:yyyy-MM-dd})",
                today, cutoff);
            return;
        }

        var totalVouchers = 0;
        var totalLiters = 0m;
        var bookedLoss = 0m;      // Σ litres × cost for the vouchers that carry a cost.
        var uncostedLiters = 0m;  // liters we cannot value yet (no cost entered).
        foreach (var g in groups)
        {
            totalVouchers += g.Count;
            totalLiters += g.Liters;
            bookedLoss += g.CostedValue;
            uncostedLiters += g.Liters - g.CostedLiters;
        }

        if (dryRun)
        {
            _logger.LogInformation(
                "Expired-voucher loss booking is OFF (dry-run): {Vouchers} unsold voucher(s) / {Liters} L lapsed as of " +
                "{Today:yyyy-MM-dd} (cutoff {Cutoff:yyyy-MM-dd}) would be retired, booking ~{Loss} UAH loss ({UncostedLiters} L uncosted, excluded). " +
                "Set ExpiredVoucherLoss:Enabled to book them.",
                totalVouchers, totalLiters, today, cutoff, decimal.Round(bookedLoss, 2), uncostedLiters);
            return;
        }

        // Flip in bounded batches. A voucher set to Expired drops out of the InStockStatuses filter, so
        // successive Take(BatchSize) pulls fresh candidates until none remain.
        var flipped = 0;
        while (true)
        {
            var batch = await _context.FuelVouchers
                .AsTracking()
                .Where(v => InStockStatuses.Contains(v.Status) && v.SupplierExpirationDate < cutoff)
                .Take(BatchSize)
                .ToListAsync(cancellationToken);

            if (batch.Count == 0)
            {
                break;
            }

            var now = DateTime.UtcNow;
            foreach (var v in batch)
            {
                // Read the status BEFORE the flip. The candidate set spans three values (Imported /
                // VerifiedWithWarnings / Available), so from_status has to say which one this voucher
                // actually was - a constant would tell the history that every stock expiry started at
                // Available, which is false for imported-but-never-activated stock.
                var fromStatus = v.Status;

                v.Status = VoucherStatus.Expired;
                v.UpdatedAtUtc = now;

                // D-13, and the first writer with no person behind it. Every other D-13 action is a
                // consequence of an order somebody placed, so `user` was the only honest value available.
                // A nightly sweep has nobody to name, and 'system' is precisely why that second value
                // exists: an author an operator can read rather than a blank. The CHECK admits it only
                // with a NULL id, which is exactly what a scheduler has.
                //
                // An event and not a movement, because nothing changed place. The voucher is still the
                // operator's paper - it has been written off as a loss. A custody row would put it
                // somewhere it never went.
                _context.VoucherEvents.Add(new VoucherEvent
                {
                    Id = Guid.CreateVersion7(),
                    VoucherId = v.Id,
                    EventType = VoucherEventTypes.StatusChanged,
                    FromStatus = fromStatus.ToString(),
                    ToStatus = nameof(VoucherStatus.Expired),
                    ActorKind = VoucherActorKinds.System,
                    ActorUserId = null,
                    // The grace window is why this happened today and not on the printed date, and that
                    // is the first question anyone asks about a write-off. Answered on the row rather
                    // than in the log, which nobody reads twice.
                    Reason = $"expired unsold, retired by the nightly loss sweep (grace {graceDays}d)",
                    OccurredAtUtc = now
                });
            }

            await _context.SaveChangesAsync(cancellationToken);
            flipped += batch.Count;

            if (batch.Count < BatchSize)
            {
                break;
            }
        }

        _logger.LogInformation(
            "Expired-voucher loss booked: retired {Flipped} unsold voucher(s) / {Liters} L lapsed as of " +
            "{Today:yyyy-MM-dd} (cutoff {Cutoff:yyyy-MM-dd}) to Expired, realising ~{Loss} UAH loss ({UncostedLiters} L uncosted).",
            flipped, totalLiters, today, cutoff, decimal.Round(bookedLoss, 2), uncostedLiters);
    }
}
