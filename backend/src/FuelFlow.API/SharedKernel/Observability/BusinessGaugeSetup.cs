using System.Diagnostics.Metrics;
using FuelFlow.Features.Orders.SharedModels;
using FuelFlow.Features.Vouchers.SharedModels;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Options;
using FuelFlow.SharedKernel.Time;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Serilog;

namespace FuelFlow.SharedKernel.Observability;

/// <summary>
/// Registers the observable business gauges (currently the voucher pool) against the
/// shared <see cref="FuelFlowMetrics"/> meter.
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

        metrics.RegisterVoucherPoolGauge(() =>
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

                var byBrandAndFuel = db.FuelVouchers
                    .Where(v => v.AssignedToUserId == null
                        && !v.IsDeleted
                        && v.Status == VoucherStatus.Available)
                    .GroupBy(v => new { v.Brand, v.FuelTypeId })
                    .Select(g => new { g.Key.Brand, g.Key.FuelTypeId, Count = g.Count() })
                    .ToList()
                    .Select(x => new Measurement<int>(
                        x.Count,
                        new KeyValuePair<string, object?>("brand", x.Brand),
                        new KeyValuePair<string, object?>("fuel_type", x.FuelTypeId)))
                    .ToList();

                return ZeroWhenEmpty(byBrandAndFuel);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Voucher pool gauge could not be read");
                return [];
            }
        });

        metrics.RegisterStuckPaymentGauge(() =>
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
        });

        return host;
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
