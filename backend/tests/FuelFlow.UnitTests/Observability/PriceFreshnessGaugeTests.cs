using System.Diagnostics.Metrics;
using FluentAssertions;
using FuelFlow.Features.Vouchers.Security;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using FuelFlow.SharedKernel.Observability;
using FuelFlow.SharedKernel.Options;
using FuelFlow.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace FuelFlow.UnitTests.Observability;

/// <summary>
/// The age of every brand's pump prices, as the staleness alarm reads it.
/// </summary>
/// <remarks>
/// The alarm exists because a pump-price sync that fails keeps the last known prices on purpose -
/// correct for an outage, indistinguishable from a market that has not moved. Measured on
/// 2026-10-29: WOG's seven grades had not been written for nineteen hours and the system looked
/// healthy. These pin what the gauge reports: every automatically priced brand that has fuel, the
/// brand as its label, and an unbounded age for a fuel that has never been priced.
/// </remarks>
[Collection(BusinessGaugeCollection.Name)]
public sealed class PriceFreshnessGaugeTests : IDisposable
{
    private readonly ServiceProvider _provider;
    private readonly IHost _host;

    public PriceFreshnessGaugeTests()
    {
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
    public void It_reports_the_age_of_a_brand_whose_last_price_was_written_ninety_minutes_ago()
    {
        Seed(db =>
        {
            db.Brands.Add(BrandFor(BrandIds.Wog, "wog"));
            db.FuelTypes.Add(FuelFor(BrandIds.Wog, "ДП Mustang+", TestClock.UtcNow.AddMinutes(-90)));
        });

        var samples = CollectGaugeSamples();

        var wog = samples.Should().ContainSingle().Which;
        wog.Tags.Should().Contain(new KeyValuePair<string, object?>("brand", "wog"),
            "the alert names the brand, so the series has to carry it");
        wog.Value.Should().BeApproximately(90 * 60, 1,
            "ninety minutes stale is the number the rule thresholds at an hour");
    }

    [Fact]
    public void A_brand_that_has_never_been_priced_reports_an_unbounded_age_not_a_missing_series()
    {
        Seed(db =>
        {
            db.Brands.Add(BrandFor(BrandIds.Klo, "klo"));
            db.FuelTypes.Add(FuelFor(BrandIds.Klo, "ДП", priceUpdatedAt: null));
        });

        var klo = CollectGaugeSamples().Should().ContainSingle().Which;

        klo.Tags.Should().Contain(new KeyValuePair<string, object?>("brand", "klo"));
        klo.Value.Should().BeGreaterThan(3600,
            "a fuel with no stamp is stale by any measure, and dropping it would hide the worst case");
    }

    [Fact]
    public void A_brand_with_no_live_fuel_is_not_reported()
    {
        Seed(db =>
        {
            db.Brands.Add(BrandFor(BrandIds.Okko, "okko"));
            db.Brands.Add(BrandFor(BrandIds.Upg, "upg"));
            db.FuelTypes.Add(FuelFor(BrandIds.Upg, "upg95", TestClock.UtcNow.AddMinutes(-5)));
        });

        CollectGaugeSamples().Should().ContainSingle()
            .Which.Tags.Should().Contain(new KeyValuePair<string, object?>("brand", "upg"),
                "a brand with nothing on the shelf has nothing that can go stale");
    }

    [Fact]
    public void A_brand_priced_by_hand_is_not_reported()
    {
        var manual = Guid.NewGuid();
        Seed(db =>
        {
            db.Brands.Add(new Brand
            {
                Id = manual,
                Slug = "brand-new",
                Name = "Brand New",
                LogoText = "BN",
                CreatedAtUtc = TestClock.UtcNow,
                UpdatedAtUtc = TestClock.UtcNow
            });
            db.FuelTypes.Add(FuelFor(manual, "A-92", TestClock.UtcNow.AddDays(-30)));
        });

        CollectGaugeSamples().Should().NotContain(
            sample => sample.Tags.Any(tag => tag.Key == "brand"),
            "no job feeds this brand, so its age is not a feed that stopped and must not page anyone");
    }

    private static Brand BrandFor(Guid id, string slug) => new()
    {
        Id = id,
        Slug = slug,
        Name = slug.ToUpperInvariant(),
        LogoText = slug.ToUpperInvariant(),
        CreatedAtUtc = TestClock.UtcNow,
        UpdatedAtUtc = TestClock.UtcNow
    };

    private static FuelTypeEntity FuelFor(Guid brandId, string name, DateTime? priceUpdatedAt) => new()
    {
        Id = Guid.CreateVersion7(),
        BrandId = brandId,
        Name = name,
        PumpPricePerLiter = 100m,
        PriceUpdatedAt = priceUpdatedAt,
        CreatedAtUtc = TestClock.UtcNow.AddDays(-90),
        UpdatedAtUtc = TestClock.UtcNow
    };

    private void Seed(Action<ApplicationDbContext> seed)
    {
        using var scope = _provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        seed(db);
        db.SaveChanges();
    }

    /// <summary>
    /// Registers the production gauges and reads the observations the exporter would collect.
    /// </summary>
    private List<GaugeSample> CollectGaugeSamples()
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
        listener.SetMeasurementEventCallback<double>((instrument, value, tags, _) =>
        {
            if (instrument.Name == "fuelflow.fuel_prices.last_sync_age")
            {
                samples.Add(new GaugeSample(value, tags.ToArray()));
            }
        });

        listener.Start();
        _host.RegisterFuelFlowBusinessGauges();
        listener.RecordObservableInstruments();

        return samples;
    }

    private sealed record GaugeSample(double Value, KeyValuePair<string, object?>[] Tags);

    private sealed class ServiceProviderHost(IServiceProvider services) : IHost
    {
        public IServiceProvider Services { get; } = services;
        public void Dispose() { }
        public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
