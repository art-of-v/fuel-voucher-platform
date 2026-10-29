using FuelFlow.Features.Vouchers.SharedModels;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using FuelFlow.SharedKernel.Observability;
using FuelFlow.SharedKernel.Options;
using FuelFlow.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace FuelFlow.API.BackgroundJobs;

/// <summary>
/// Watches the available voucher pool and raises an alert when a brand / fuel type / pack size
/// combination drops to or below the configured threshold, or reaches zero.
/// <para>
/// This complements the Prometheus <c>VoucherPoolLow</c> rule: the metric drives the
/// dashboards, this job drives the immediate chat message with the concrete numbers.
/// </para>
/// <para>
/// The combination is the one a sale is matched on — <c>FulfillmentService.FindMatchingVouchersAsync</c>
/// compares brand, fuel type, litres and data kind — so a 10-litre line can never be satisfied by a
/// 20-litre voucher. Counting the sizes together, or counting rehearsal stock, reports a pool the
/// customer cannot buy from: planning #781 was filed on a paid 10 × 10 L order that had no voucher
/// while the alert read healthy.
/// </para>
/// <para>
/// Lives in the API (not FuelFlow.JobsWorker) because production deploys only the API and
/// runs Hangfire in-process - see the recurring-job registration in Program.cs. Keeping the
/// single copy here, alongside RefundStatusSyncService, avoids the API/JobsWorker drift.
/// </para>
/// </summary>
public sealed class VoucherStockMonitor
{
    private readonly ApplicationDbContext _context;
    private readonly NotificationDispatcher _notifications;
    private readonly TelegramOptions _telegram;
    private readonly ILogger<VoucherStockMonitor> _logger;

    public VoucherStockMonitor(
        ApplicationDbContext context,
        NotificationDispatcher notifications,
        IOptions<TelegramOptions> telegram,
        ILogger<VoucherStockMonitor> logger)
    {
        _context = context;
        _notifications = notifications;
        _telegram = telegram.Value;
        _logger = logger;
    }

    public async Task CheckLowStockAsync(CancellationToken cancellationToken = default)
    {
        var config = _telegram.Notifications.Vouchers;
        if (!config.NotifyOnLowStock && !config.NotifyOnZeroStock && !config.NotifyOnExpiringStock)
        {
            return;
        }

        var threshold = config.LowLevelThreshold;

        // Counting only Available rows cannot surface an exhausted combination: a group
        // with zero rows does not exist, so it would simply vanish from the results.
        // The set of combinations to check therefore comes from every known voucher,
        // and the available count is a conditional sum over that same set.
        //
        // Expiring is counted alongside rather than instead of Available. Those vouchers are
        // still sellable today, so the pool count reads healthy and the stock looks fine - but
        // they stop being worth anything on their expiry date, and nothing else in this job
        // (or, before this, in the whole system) says so while there is still time to price
        // them for renewal. A combination qualifies if *either* number is worth reporting.
        var horizon = DateOnly.FromDateTime(SystemClock.UtcNow).AddDays(config.ExpiringWithinDays);

        // Real stock only: a QA voucher is rehearsal data that only a test account can ever be
        // given (the fulfilment path picks the kind with DataKinds.ForPool), so counting it makes
        // the pool look stocked to the one alert that is supposed to see the shortage.
        var counts = await _context.FuelVouchers
            .Where(v => !v.IsDeleted && v.DataKind == DataKinds.Real)
            .GroupBy(v => new { v.Brand, v.FuelTypeId, v.Liters })
            .Select(g => new
            {
                g.Key.Brand,
                g.Key.FuelTypeId,
                g.Key.Liters,
                Available = g.Count(v => v.AssignedToUserId == null && v.Status == VoucherStatus.Available),
                ExpiringSoon = g.Count(v => v.AssignedToUserId == null
                                            && v.Status == VoucherStatus.Available
                                            && v.CustomerExpirationDate <= horizon)
            })
            .Where(x => x.Available <= threshold || x.ExpiringSoon > 0)
            .ToListAsync(cancellationToken);

        if (counts.Count == 0)
        {
            return;
        }

        // Resolve FuelTypeId -> display name so the alert reads "ДП ЄВРО" rather than a raw
        // id (the readable-label fix from #31/#640). A voucher whose fuel-type row has drifted
        // away (the OKKO import drift class) falls back to its id rather than being dropped;
        // the id is also kept in the log line below for traceability during such incidents.
        var fuelTypeIds = counts.Select(c => c.FuelTypeId).Distinct().ToList();
        var fuelTypeNames = await _context.FuelTypes
            .Where(f => fuelTypeIds.Contains(f.Id))
            .ToDictionaryAsync(f => f.Id, f => f.Name, cancellationToken);

        foreach (var combination in counts)
        {
            var fuelTypeLabel = fuelTypeNames.TryGetValue(combination.FuelTypeId, out var name)
                ? name
                : combination.FuelTypeId.ToString();

            // Expiring first: a pool that is about to lose stock is the more urgent of the two, and
            // the low-count alert below would repeat a number that is still comfortably above zero.
            if (combination.ExpiringSoon > 0)
            {
                _logger.LogWarning(
                    "Voucher pool expiring for {Brand}/{FuelType}/{Liters}L ({FuelTypeId}): {Expiring} of {Available} available expire within {Days} days",
                    combination.Brand, fuelTypeLabel, combination.Liters, combination.FuelTypeId,
                    combination.ExpiringSoon, combination.Available, config.ExpiringWithinDays);

                await _notifications.VoucherStockExpiringAsync(
                    combination.Brand,
                    fuelTypeLabel,
                    combination.Liters,
                    combination.ExpiringSoon,
                    combination.Available,
                    config.ExpiringWithinDays,
                    cancellationToken);
            }

            if (combination.Available <= threshold)
            {
                _logger.LogWarning(
                    "Voucher pool low for {Brand}/{FuelType}/{Liters}L ({FuelTypeId}): {Count} remaining (threshold {Threshold})",
                    combination.Brand, fuelTypeLabel, combination.Liters, combination.FuelTypeId,
                    combination.Available, threshold);

                await _notifications.VoucherStockLowAsync(
                    combination.Brand,
                    fuelTypeLabel,
                    combination.Liters,
                    combination.Available,
                    threshold,
                    cancellationToken);
            }
        }
    }
}
