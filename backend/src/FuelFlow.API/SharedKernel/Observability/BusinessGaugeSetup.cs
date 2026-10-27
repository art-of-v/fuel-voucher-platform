using System.Diagnostics.Metrics;
using FuelFlow.Features.Orders.SharedModels;
using FuelFlow.Features.Vouchers.SharedModels;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using FuelFlow.SharedKernel.Options;
using FuelFlow.SharedKernel.Time;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Serilog;

namespace FuelFlow.SharedKernel.Observability;

/// <summary>
/// Registers the observable business gauges (the voucher pool, the order statuses and the stuck
/// payments) against the shared <see cref="FuelFlowMetrics"/> meter.
/// <para>
/// Observable gauges only produce a series once registered, and the callback is polled
/// by the Prometheus exporter on every scrape — so this MUST be called once at startup in
/// EVERY process that is actually scraped, and the callback must stay cheap and never throw
/// (a throwing observable aborts the entire /metrics response).
/// </para>
/// <para>
/// This lives in one place on purpose. In production only the API is deployed (Hangfire runs
/// in-process; there is no JobsWorker container and Prometheus scrapes only fuelflow-api), so
/// registering the gauge only in the worker meant <c>fuelflow_vouchers_pool_available_vouchers</c>
/// was never emitted in production — the VoucherPoolLow alert could never fire and the business
/// dashboard panels sat empty. Both hosts now call this same method so they cannot drift again.
/// </para>
/// </summary>
public static class BusinessGaugeSetup
{
    public static IHost RegisterFuelFlowBusinessGauges(this IHost host)
    {
        var metrics = host.Services.GetRequiredService<FuelFlowMetrics>();
        var scopeFactory = host.Services.GetRequiredService<IServiceScopeFactory>();

        metrics.RegisterVoucherPoolGauge(() => ReadVoucherPool(scopeFactory));
        metrics.RegisterOrderStatusGauge(() => ReadOrderStatuses(scopeFactory));
        metrics.RegisterStuckPaymentGauge(() => ReadStuckPayments(scopeFactory));

        return host;
    }

    /// <summary>
    /// The sellable pool, one series per brand and fuel type.
    /// <para>
    /// The series set is the inventory we CARRY, not the inventory we happen to have available: a
    /// combination whose stock sold out still has rows (status Assigned, Used, Expired), so it still
    /// reports — with a zero. Grouping over the available rows alone made an exhausted combination
    /// disappear, and a combination that is gone is exactly the one the operator has to be told
    /// about: VoucherPoolLow could not fire on a stock-out, because the series it thresholds had
    /// ceased to exist.
    /// </para>
    /// <para>
    /// The counts come from <c>stock_summary</c> rather than from <c>fuel_vouchers</c> itself. The
    /// summary is kept by a trigger, so it answers the same question without scanning the largest
    /// table in the schema on every scrape.
    /// </para>
    /// </summary>
    private static IEnumerable<Measurement<int>> ReadVoucherPool(IServiceScopeFactory scopeFactory)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var expiryEnabled = scope.ServiceProvider.GetRequiredService<IConfiguration>()
                .GetValue("VoucherExpiration:Enabled", true);
            var today = DateOnly.FromDateTime(SystemClock.UtcNow);

            var carried = db.StockSummary
                .Where(s => s.DataKind == DataKinds.Real && !s.IsDeleted)
                .Select(s => new { s.Brand, s.FuelTypeId })
                .Distinct()
                .ToList();

            if (carried.Count == 0)
            {
                return ZeroWhenEmpty([]);
            }

            var available = db.StockSummary
                .Where(s => s.DataKind == DataKinds.Real
                         && !s.IsDeleted
                         && s.Status == VoucherStatus.Available
                         && (!expiryEnabled || s.SupplierExpirationDate >= today))
                .GroupBy(s => new { s.Brand, s.FuelTypeId })
                .Select(g => new { g.Key.Brand, g.Key.FuelTypeId, Count = g.Sum(s => s.VoucherCount) })
                .ToList()
                .ToDictionary(x => (x.Brand, x.FuelTypeId), x => x.Count);

            // The name, not the id: the legend of the pool panel is read by an operator, and a uuid
            // says nothing. The catalogue query filter drops archived fuels, so an archived fuel
            // falls back to its id — two live fuels cannot share a name within a brand, so the label
            // stays unique and cannot make Prometheus reject a duplicate sample.
            var fuelNames = db.FuelTypes
                .Select(f => new { f.Id, f.Name })
                .ToDictionary(f => f.Id, f => f.Name);

            return carried.Select(c => new Measurement<int>(
                available.GetValueOrDefault((c.Brand, c.FuelTypeId)),
                new KeyValuePair<string, object?>("brand", c.Brand),
                new KeyValuePair<string, object?>("fuel_type",
                    fuelNames.GetValueOrDefault(c.FuelTypeId, c.FuelTypeId.ToString()))))
                .ToList();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Voucher pool gauge could not be read");
            return [];
        }
    }

    /// <summary>
    /// Every order status, including the ones with nothing in them.
    /// <para>
    /// A status with no orders is still a status, and a panel that drops it leaves a gap where the
    /// number should be. The gauge reports all of them, so "no order is waiting for payment" is a
    /// zero in the panel rather than a missing series.
    /// </para>
    /// </summary>
    private static IEnumerable<Measurement<int>> ReadOrderStatuses(IServiceScopeFactory scopeFactory)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var byStatus = db.Orders
                .GroupBy(o => o.Status)
                .Select(g => new { Status = g.Key, Count = g.Count() })
                .ToList()
                .ToDictionary(x => x.Status, x => x.Count);

            return Enum.GetValues<OrderStatus>()
                .Select(status => new Measurement<int>(
                    byStatus.GetValueOrDefault(status),
                    new KeyValuePair<string, object?>("status", status.ToString())))
                .ToList();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Order status gauge could not be read");
            return [];
        }
    }

    /// <summary>
    /// Orders that have been awaiting payment past the reconciliation grace window, by merchant.
    /// </summary>
    private static IEnumerable<Measurement<int>> ReadStuckPayments(IServiceScopeFactory scopeFactory)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var options = scope.ServiceProvider
                .GetRequiredService<IOptions<MonobankOptions>>().Value;

            // The same grace window reconciliation itself uses: below it, an order is legitimately
            // still awaiting payment and the webhook simply has not arrived yet. Above it,
            // reconciliation has already had a chance to poll it, so a still-pending order means
            // the webhook was lost AND the backstop did not recover it. Orders older than the
            // polling ceiling count too - those are no longer polled at all, so they would
            // otherwise sit here forever, unpaid and unfulfillable, with nothing to show for it.
            var stuckBefore = SystemClock.UtcNow.AddMinutes(-options.ReconciliationMinAgeMinutes);

            var byMerchant = db.Orders
                .Where(o => o.Status == OrderStatus.PendingPayment
                         && o.MonobankInvoiceId != null
                         && o.CreatedAtUtc < stuckBefore)
                .GroupBy(o => o.MonobankMerchant ?? MonobankMerchant.Live)
                .Select(g => new { Merchant = g.Key, Count = g.Count() })
                .ToList()
                .Select(x => new Measurement<int>(
                    x.Count,
                    new KeyValuePair<string, object?>("merchant", x.Merchant.ToString())))
                .ToList();

            return ZeroWhenEmpty(byMerchant);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Stuck payment gauge could not be read");
            return [];
        }
    }

    /// <summary>
    /// A gauge with nothing to count still has something to report: zero.
    /// <para>
    /// An observable gauge returns measurements, and an empty sequence emits no series at all.
    /// Prometheus therefore has no sample for <c>fuelflow_vouchers_pool_available_vouchers</c> when
    /// the pool is empty, the rule evaluating it sees no data instead of a 0, and a system with
    /// nothing left to sell is indistinguishable from an exporter that stopped reporting. That is
    /// what production did until 2026-10-27: the gauge was registered correctly and emitted nothing,
    /// so <c>VoucherPoolLow</c> sat in NoData instead of saying the pool was empty.
    /// </para>
    /// <para>
    /// An empty <em>result</em> becomes 0. A failed read deliberately does not: the callers return
    /// an empty sequence from their catch block, and that must stay NoData - "could not count" is
    /// not "counted, and it is zero", and reporting a broken read as a healthy zero would silence
    /// the one alert that can see it.
    /// </para>
    /// </summary>
    private static IEnumerable<Measurement<int>> ZeroWhenEmpty(List<Measurement<int>> measurements) =>
        measurements.Count > 0 ? measurements : [new Measurement<int>(0)];
}
