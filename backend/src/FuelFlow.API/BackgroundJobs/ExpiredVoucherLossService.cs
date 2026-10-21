using FuelFlow.Features.Ledger;
using FuelFlow.Features.Ledger.SharedModels;
using FuelFlow.Features.Settings;
using FuelFlow.Features.Settings.SharedModels;
using FuelFlow.Features.Vouchers;
using FuelFlow.Features.Vouchers.Events;
using FuelFlow.Features.Vouchers.SharedModels;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Time;
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

    /// <summary>
    /// The ledger event for writing expired unsold stock off. It names a mapping rule, not an
    /// account: which accounts it lands on is data, so the accountant can move it without the
    /// code changing. Dr 807 (Витрати від утрати товарів) / Cr 281 (Товари на складі).
    /// </summary>
    private const string ExpiredStockWriteOffEvent = "stock.expired.writeoff";

    // The operator-owned, unsold, sellable pool — identical to the P&L handler's remaining-stock set
    // and BlendedCostRecalculator's cost pool. Only these lapse into an operator loss.
    private static readonly VoucherStatus[] InStockStatuses =
        [VoucherStatus.Imported, VoucherStatus.VerifiedWithWarnings, VoucherStatus.Available];

    private readonly ApplicationDbContext _context;
    private readonly RuntimeSettingsService _settings;
    private readonly JournalPostingService _journal;
    private readonly ILogger<ExpiredVoucherLossService> _logger;

    public ExpiredVoucherLossService(
        ApplicationDbContext context,
        RuntimeSettingsService settings,
        JournalPostingService journal,
        ILogger<ExpiredVoucherLossService> logger)
    {
        _context = context;
        _settings = settings;
        _journal = journal;
        _logger = logger;
    }

    public virtual async Task BookExpiredLossAsync(CancellationToken cancellationToken = default)
    {
        var enabled = await _settings.IsExpiredVoucherLossEnabledAsync(cancellationToken);
        var graceDays = await _settings.GetExpiredVoucherLossGraceDaysAsync(cancellationToken);
        var dryRun = !enabled;

        // Who stands behind the write-off. The journal insists every posted entry names a person
        // ("a posted entry without both a moment and a person is an entry nobody can be asked
        // about"), and a nightly sweep has nobody of its own.
        //
        // The operator who turned the sweep on does have somebody: the person who decided that
        // lapsed unsold stock is written off is the one the entry is about. The admin screen records
        // who changed a setting, so the answer is already in the database - no invented user, no
        // schema change, and no entry attributed to somebody who did not decide it.
        var author = await _settings.GetSettingAsync(AppSettingKeys.ExpiredVoucherLossEnabled, cancellationToken);
        var authorId = author?.UpdatedByUserId;

        if (enabled && authorId is null)
        {
            // All or nothing. A voucher retired without its write-off in the ledger is the exact
            // divergence this exists to remove - account 281 carrying stock that is gone - and
            // attributing the entry to a made-up user would be a worse lie. So the job books
            // nothing and says why.
            _logger.LogError(
                "Expired-voucher loss booking is ON but the setting {Key} names nobody, so there is "
                + "no person to post the write-off under. Nothing was booked. Re-save the setting "
                + "from the admin screen so it records who decided it.",
                AppSettingKeys.ExpiredVoucherLossEnabled);
            return;
        }

        // Strictly-before "today" so a voucher stays valid through the whole of its expiration date;
        // one clock read shared by the summary aggregate and the batched flip so they can't drift.
        // The grace window means we only retire vouchers whose expiry is older than (today - graceDays).
        var today = DateOnly.FromDateTime(SystemClock.UtcNow);
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
                CostedCount = g.Count(v => v.CostPerLiter != null),
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
        var costedVouchers = 0;   // how many vouchers carry a cost, and so can be written off.
        foreach (var g in groups)
        {
            totalVouchers += g.Count;
            totalLiters += g.Liters;
            bookedLoss += g.CostedValue;
            uncostedLiters += g.Liters - g.CostedLiters;
            costedVouchers += g.CostedCount;
        }

        if (dryRun)
        {
            _logger.LogInformation(
                "Expired-voucher loss booking is OFF (dry-run): {Vouchers} unsold voucher(s) / {Liters} L lapsed as of " +
                "{Today:yyyy-MM-dd} (cutoff {Cutoff:yyyy-MM-dd}) would be retired, booking ~{Loss} UAH loss ({UncostedLiters} L uncosted, excluded). " +
                "The ledger would take {CostedVouchers} write-off entr(y/ies), Dr 807 (Витрати від утрати товарів) / Cr 281 (Товари на складі), " +
                "for the same ~{Loss} UAH. Set ExpiredVoucherLoss:Enabled to book them.",
                totalVouchers, totalLiters, today, cutoff, decimal.Round(bookedLoss, 2), uncostedLiters,
                costedVouchers, decimal.Round(bookedLoss, 2));
            return;
        }

        // Flip in bounded batches. A voucher set to Expired drops out of the InStockStatuses filter, so
        // successive Take(BatchSize) pulls fresh candidates until none remain.
        var flipped = 0;
        var writtenOff = 0m;
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

            var now = SystemClock.UtcNow;
            var writeOffs = new List<PostJournalEntryRequest>();
            foreach (var v in batch)
            {
                // Read the status BEFORE the flip. The candidate set spans three values (Imported /
                // VerifiedWithWarnings / Available), so from_status has to say which one this voucher
                // actually was - a constant would tell the history that every stock expiry started at
                // Available, which is false for imported-but-never-activated stock.
                var fromStatus = v.Status;

                // The ledger first, the status second - and the order matters.
                //
                // Without this the stock left the sellable pool and the history said so, while account
                // 281 still carried its cost: two books, two answers, and the one an accountant reads
                // was the wrong one. The voucher is now written off in both, in the same breath.
                //
                // Posting BEFORE the flip is what makes a failure safe. If the posting throws, this
                // voucher is not retired, so the next night's run tries it again - and the posting is
                // idempotent on (source_event, source_type, source_id), so the retry reuses the entry
                // instead of writing a second one. A voucher with no cost carries no value to write
                // off, so it is retired without a journal entry - the same hole the batch P&L has.
                var writeOffAmount = v.CostPerLiter is null
                    ? 0m
                    : decimal.Round(v.Liters * v.CostPerLiter.Value, 2);

                if (writeOffAmount > 0m)
                {
                    writeOffs.Add(new PostJournalEntryRequest
                    {
                        SourceEvent = ExpiredStockWriteOffEvent,
                        SourceType = JournalSourceType.Cost,
                        // The voucher, so the idempotency key is per voucher and a re-run cannot
                        // write the same write-off twice.
                        SourceId = v.Id,
                        AmountUah = writeOffAmount,
                        Description = $"expired unsold voucher {v.VoucherNumber} written off",
                        EntryDate = DateOnly.FromDateTime(now),
                        // The operator who decided lapsed stock is written off. Not null: the job
                        // refuses to book at all when the setting names nobody.
                        PostedByUserId = authorId!.Value,
                        VoucherId = v.Id,
                        QuantityLiters = v.Liters,
                    });
                    writtenOff += writeOffAmount;
                }

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

            // The ledger for the whole batch, in one unit of work. A post is five queries of its
            // own - the idempotency check, the accounts, the period, the entry number and the two
            // saves - and the batch asks the same four questions once for the whole set.
            //
            // The order the comment above insists on is kept: every write-off is posted before any
            // status is flipped, so a failure here leaves the stock exactly as it was and the next
            // night's run tries it again.
            if (writeOffs.Count > 0)
            {
                await _journal.PostManyAsync(writeOffs, cancellationToken);
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
            "{Today:yyyy-MM-dd} (cutoff {Cutoff:yyyy-MM-dd}) to Expired, realising ~{Loss} UAH loss ({UncostedLiters} L uncosted). " +
            "The ledger took {WrittenOff} UAH of write-offs (Dr 807 / Cr 281).",
            flipped, totalLiters, today, cutoff, decimal.Round(bookedLoss, 2), uncostedLiters,
            decimal.Round(writtenOff, 2));
    }
}
