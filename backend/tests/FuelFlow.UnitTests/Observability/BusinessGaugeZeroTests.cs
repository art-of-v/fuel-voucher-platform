using System.Diagnostics.Metrics;
using FluentAssertions;
using FuelFlow.Features.Vouchers.Security;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Observability;
using FuelFlow.SharedKernel.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace FuelFlow.UnitTests.Observability;

/// <summary>
/// A business gauge with nothing to count must still report a zero.
/// <para>
/// An observable gauge emits one series per measurement, so an empty sequence emits none. Prometheus
/// then has no sample at all for <c>fuelflow_vouchers_pool_available_vouchers</c> when the pool is
/// empty, and the rule thresholding it sees "no data" instead of 0 - which is why
/// <c>VoucherPoolLow</c> sat in NoData in production while the thing it watches for had already
/// happened. These pin the difference the code has to make: an empty <em>result</em> is 0, a failed
/// read is not.
/// </para>
/// </summary>
public sealed class BusinessGaugeZeroTests : IDisposable
{
    private readonly ServiceProvider _provider;
    private readonly IHost _host;

    public BusinessGaugeZeroTests()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<FuelFlowMetrics>();
        services.AddSingleton<IQrCipher>(TestQrCipher.Instance);
        services.AddDbContext<ApplicationDbContext>(
            options => options.UseInMemoryDatabase(Guid.NewGuid().ToString()));
        services.Configure<MonobankOptions>(_ => { });

        _provider = services.BuildServiceProvider();
        _host = new ServiceProviderHost(_provider);
    }

    public void Dispose() => _provider.Dispose();

    [Fact]
    public void EmptyVoucherPool_IsReportedAsZero_NotAsNoSeries()
    {
        var values = CollectGaugeValues("fuelflow.vouchers.pool_available");

        values.Should().ContainSingle(
            "an empty pool is a count of zero; emitting nothing leaves the alert with no data at all")
            .Which.Should().Be(0);
    }

    [Fact]
    public void NoStuckPayments_IsReportedAsZero_NotAsNoSeries()
    {
        var values = CollectGaugeValues("fuelflow.payments.stuck_pending_payment");

        values.Should().ContainSingle(
            "no stuck payments is the healthy state, and it has to read as 0 rather than as NoData")
            .Which.Should().Be(0);
    }

    /// <summary>
    /// Registers the production gauges against the in-memory database and reads back the
    /// observations the exporter would have collected.
    /// </summary>
    private List<int> CollectGaugeValues(string instrumentName)
    {
        var values = new List<int>();

        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == ObservabilityConstants.MeterName)
            {
                l.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<int>((instrument, value, _, _) =>
        {
            if (instrument.Name == instrumentName)
            {
                values.Add(value);
            }
        });

        listener.Start();
        _host.RegisterFuelFlowBusinessGauges();
        listener.RecordObservableInstruments();

        return values;
    }

    private sealed class ServiceProviderHost(IServiceProvider services) : IHost
    {
        public IServiceProvider Services { get; } = services;
        public void Dispose() { }
        public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
