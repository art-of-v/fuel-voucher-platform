using System.Diagnostics.Metrics;
using FluentAssertions;
using FuelFlow.Features.Orders.SharedModels;
using FuelFlow.Features.Vouchers.Availability;
using FuelFlow.Features.Vouchers.Security;
using FuelFlow.Features.Vouchers.SharedModels;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using FuelFlow.SharedKernel.Observability;
using FuelFlow.SharedKernel.Options;
using FuelFlow.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace FuelFlow.UnitTests.Observability;

/// <summary>
/// A business gauge with nothing to count must still report a zero, and a gauge that counts
/// something must report every combination it counts - including the ones that count nothing.
/// <para>
/// An observable gauge emits one series per measurement, so an empty sequence emits none. Prometheus
/// then has no sample at all for <c>fuelflow_vouchers_pool_available_vouchers</c> when the pool is
/// empty, and the rule thresholding it sees "no data" instead of 0 - which is why
/// <c>VoucherPoolLow</c> sat in NoData in production while the thing it watches for had already
/// happened. These pin the difference the code has to make: an empty <em>result</em> is 0, a failed
/// read is not.
/// </para>
/// <para>
/// The other half is the combination that ran out. Grouping the pool over the available vouchers
/// alone made an exhausted brand/fuel pair disappear from the metric, and a pair that is gone is
/// precisely what VoucherPoolLow has to be able to fire on.
/// </para>
/// </summary>
[Collection(BusinessGaugeCollection.Name)]
public sealed class BusinessGaugeZeroTests : IDisposable
{
    private readonly ServiceProvider _provider;
    private readonly IHost _host;

    public BusinessGaugeZeroTests()
    {
        // The name has to be fixed once: the options lambda runs per scope, so a Guid there would
        // give the seeding scope and the gauge's scope two different databases.
        var databaseName = Guid.NewGuid().ToString();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddSingleton<FuelFlowMetrics>();
        services.AddSingleton<IQrCipher>(TestQrCipher.Instance);
        services.AddDbContext<ApplicationDbContext>(
            options => options.UseInMemoryDatabase(databaseName));
        services.Configure<MonobankOptions>(_ => { });

        _provider = services.BuildServiceProvider();
        _host = new ServiceProviderHost(_provider);
    }

    public void Dispose() => _provider.Dispose();

    [Fact]
    public void EmptyVoucherPool_IsReportedAsZero_NotAsNoSeries()
    {
        var values = CollectGaugeSamples("fuelflow.vouchers.pool_available");

        values.Should().ContainSingle(
            "an empty pool is a count of zero; emitting nothing leaves the alert with no data at all")
            .Which.Value.Should().Be(0);
    }

    [Fact]
    public void NoStuckPayments_IsReportedAsZero_NotAsNoSeries()
    {
        var values = CollectGaugeSamples("fuelflow.payments.stuck_pending_payment");

        values.Should().ContainSingle(
            "no stuck payments is the healthy state, and it has to read as 0 rather than as NoData")
            .Which.Value.Should().Be(0);
    }

    [Fact]
    public void ExhaustedCombination_ReportsZero_NotAsAMissingSeries()
    {
        var fuelTypeId = Guid.NewGuid();
        Seed(db => db.StockSummary.Add(new StockSummary
        {
            Id = Guid.NewGuid(),
            Brand = "OKKO",
            FuelTypeId = fuelTypeId,
            Liters = 10,
            Status = VoucherStatus.Assigned,
            DataKind = DataKinds.Real,
            IsDeleted = false,
            SupplierExpirationDate = DateOnly.FromDateTime(TestClock.UtcNow).AddMonths(1),
            VoucherCount = 5
        }));

        var samples = CollectGaugeSamples("fuelflow.vouchers.pool_available");

        samples.Should().ContainSingle(
            "the combination is still in the pool's book - it sold out, it did not vanish")
            .Which.Value.Should().Be(0);
    }

    [Fact]
    public void EachPackSize_IsAPoolOfItsOwn()
    {
        // Fulfilment matches a line on brand, fuel type AND litres, so the sizes are not
        // interchangeable: a 10 L order cannot be filled from 20 L vouchers. Counting them
        // together is what let "34 available" mean "nothing this order can use" (planning #781).
        var fuelTypeId = Guid.NewGuid();
        Seed(db =>
        {
            db.StockSummary.Add(new StockSummary
            {
                Id = Guid.NewGuid(),
                Brand = "WOG",
                FuelTypeId = fuelTypeId,
                Liters = 10,
                Status = VoucherStatus.Available,
                DataKind = DataKinds.Real,
                IsDeleted = false,
                SupplierExpirationDate = DateOnly.FromDateTime(TestClock.UtcNow).AddMonths(1),
                VoucherCount = 7
            });
            db.StockSummary.Add(new StockSummary
            {
                Id = Guid.NewGuid(),
                Brand = "WOG",
                FuelTypeId = fuelTypeId,
                Liters = 20,
                Status = VoucherStatus.Available,
                DataKind = DataKinds.Real,
                IsDeleted = false,
                SupplierExpirationDate = DateOnly.FromDateTime(TestClock.UtcNow).AddMonths(1),
                VoucherCount = 3
            });
        });

        var samples = CollectGaugeSamples("fuelflow.vouchers.pool_available");

        samples.Should().HaveCount(2, "a 10 L pack and a 20 L pack are two pools to the sale");
        samples.Should().ContainSingle(s => LitersLabel(s) == "10").Which.Value.Should().Be(7);
        samples.Should().ContainSingle(s => LitersLabel(s) == "20").Which.Value.Should().Be(3);
    }

    [Fact]
    public void ASoldOutPackSize_ReportsZero_WhileAnotherSizeStaysStocked()
    {
        var fuelTypeId = Guid.NewGuid();
        Seed(db =>
        {
            // Carried, all in customers' hands: no Available 10 L voucher left.
            db.StockSummary.Add(new StockSummary
            {
                Id = Guid.NewGuid(),
                Brand = "OKKO",
                FuelTypeId = fuelTypeId,
                Liters = 10,
                Status = VoucherStatus.Assigned,
                DataKind = DataKinds.Real,
                IsDeleted = false,
                SupplierExpirationDate = DateOnly.FromDateTime(TestClock.UtcNow).AddMonths(1),
                VoucherCount = 20
            });
            db.StockSummary.Add(new StockSummary
            {
                Id = Guid.NewGuid(),
                Brand = "OKKO",
                FuelTypeId = fuelTypeId,
                Liters = 20,
                Status = VoucherStatus.Available,
                DataKind = DataKinds.Real,
                IsDeleted = false,
                SupplierExpirationDate = DateOnly.FromDateTime(TestClock.UtcNow).AddMonths(1),
                VoucherCount = 30
            });
        });

        var samples = CollectGaugeSamples("fuelflow.vouchers.pool_available");

        samples.Should().ContainSingle(s => LitersLabel(s) == "10").Which.Value.Should().Be(0,
            "the brand/fuel total reads 30, but the size the customer orders is gone - " +
            "that is the state VoucherPoolLow has to be able to fire on");
        samples.Should().ContainSingle(s => LitersLabel(s) == "20").Which.Value.Should().Be(30);
    }

    [Fact]
    public void PoolGauge_NamesTheFuelType_NotItsId()
    {
        var fuelTypeId = Guid.NewGuid();
        Seed(db =>
        {
            db.FuelTypes.Add(new FuelTypeEntity
            {
                Id = fuelTypeId,
                Name = "A-95",
                BrandId = Guid.NewGuid(),
                CreatedAtUtc = TestClock.UtcNow,
                UpdatedAtUtc = TestClock.UtcNow
            });
            db.StockSummary.Add(new StockSummary
            {
                Id = Guid.NewGuid(),
                Brand = "OKKO",
                FuelTypeId = fuelTypeId,
                Liters = 10,
                Status = VoucherStatus.Available,
                DataKind = DataKinds.Real,
                IsDeleted = false,
                SupplierExpirationDate = DateOnly.FromDateTime(TestClock.UtcNow).AddMonths(1),
                VoucherCount = 4
            });
        });

        var sample = CollectGaugeSamples("fuelflow.vouchers.pool_available").Should().ContainSingle().Which;

        sample.Tags.Should().Contain(new KeyValuePair<string, object?>("brand", "OKKO"));
        sample.Tags.Should().Contain(new KeyValuePair<string, object?>("fuel_type", "A-95"),
            "the legend of the pool panel is read by an operator, and a uuid says nothing");
        sample.Tags.Should().Contain(new KeyValuePair<string, object?>("liters", "10"),
            "the alert names the size to top up, and 10 and 10.00 must not split into two series");
    }

    [Fact]
    public void QaStock_IsNotCountedAsRealPool()
    {
        var fuelTypeId = Guid.NewGuid();
        Seed(db => db.StockSummary.Add(new StockSummary
        {
            Id = Guid.NewGuid(),
            Brand = "OKKO",
            FuelTypeId = fuelTypeId,
            Liters = 10,
            Status = VoucherStatus.Available,
            DataKind = DataKinds.Qa,
            IsDeleted = false,
            SupplierExpirationDate = DateOnly.FromDateTime(TestClock.UtcNow).AddMonths(1),
            VoucherCount = 99
        }));

        var samples = CollectGaugeSamples("fuelflow.vouchers.pool_available");

        samples.Should().ContainSingle()
            .Which.Value.Should().Be(0,
                "rehearsal vouchers are not fuel a customer can be given, so they are not the pool");
    }

    [Fact]
    public void OrderStatusGauge_ReportsEveryStatus_IncludingTheEmptyOnes()
    {
        var orderId = Guid.NewGuid();
        Seed(db => db.Orders.Add(new Order
        {
            Id = orderId,
            UserId = Guid.NewGuid(),
            Status = OrderStatus.PendingFulfillment,
            CreatedAtUtc = TestClock.UtcNow,
            UpdatedAtUtc = TestClock.UtcNow
        }));

        var samples = CollectGaugeSamples("fuelflow.orders.by_status");

        samples.Should().HaveCount(Enum.GetValues<OrderStatus>().Length,
            "a status with no orders is still a status, and a missing series is a missing series");

        samples.Should().Contain(s =>
            s.Tags.Contains(new KeyValuePair<string, object?>("status", nameof(OrderStatus.PendingFulfillment)))
            && s.Value == 1);
    }

    private void Seed(Action<ApplicationDbContext> seed)
    {
        using var scope = _provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        seed(db);
        db.SaveChanges();
    }

    /// <summary>
    /// Registers the production gauges against the in-memory database and reads back the
    /// observations the exporter would have collected.
    /// </summary>
    private List<GaugeSample> CollectGaugeSamples(string instrumentName)
    {
        var samples = new List<GaugeSample>();

        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == ObservabilityConstants.MeterName)
            {
                l.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<int>((instrument, value, tags, _) =>
        {
            if (instrument.Name == instrumentName)
            {
                samples.Add(new GaugeSample(value, tags.ToArray()));
            }
        });

        listener.Start();
        _host.RegisterFuelFlowBusinessGauges();
        listener.RecordObservableInstruments();

        return samples;
    }

    private static string? LitersLabel(GaugeSample sample)
        => sample.Tags
            .Where(t => t.Key == "liters")
            .Select(t => t.Value?.ToString())
            .SingleOrDefault();

    private sealed record GaugeSample(int Value, KeyValuePair<string, object?>[] Tags);

    private sealed class ServiceProviderHost(IServiceProvider services) : IHost
    {
        public IServiceProvider Services { get; } = services;
        public void Dispose() { }
        public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
