using System.Diagnostics.Metrics;
using FuelFlow.Features.Vouchers.SharedModels;
using FuelFlow.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
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

                return db.FuelVouchers
                    .Where(v => v.AssignedToUserId == null
                        && !v.IsDeleted
                        && v.Status == VoucherStatus.Available)
                    .GroupBy(v => new { v.Provider, v.FuelTypeId })
                    .Select(g => new { g.Key.Provider, g.Key.FuelTypeId, Count = g.Count() })
                    .ToList()
                    .Select(x => new Measurement<int>(
                        x.Count,
                        new KeyValuePair<string, object?>("provider", x.Provider),
                        new KeyValuePair<string, object?>("fuel_type", x.FuelTypeId)))
                    .ToList();
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Voucher pool gauge could not be read");
                return [];
            }
        });

        return host;
    }
}
