using System.Diagnostics.Metrics;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using FuelFlow.Features.Settings;
using FuelFlow.Features.Settings.SharedModels;
using FuelFlow.Features.Updates;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Observability;
using FuelFlow.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FuelFlow.UnitTests.Updates;

/// <summary>
/// The serving half of the OTA rollback guard (F-13, planning #681).
/// <para>
/// The publisher refuses to write a manifest older than the one it is replacing, but the bucket
/// can still move backward through a path that is not the script. These tests cover what the API
/// does when it sees that: it records what each channel serves, and it says so - in the log and in
/// a metric - when the record goes backward. They also cover the cases that must stay silent,
/// because a warning on every publish would be read as noise and then ignored.
/// </para>
/// </summary>
public sealed class ServedManifestTrackerTests : IDisposable
{
    private const string Platform = "ios";
    private const string Runtime = "9f2c4e7a1b3d5f6089a2b4c6d8e0f1a3b5c7d9e1";
    private const string Key = ServedManifestTracker.SettingKeyPrefix + Platform + "/" + Runtime;
    private static readonly DateTime SeededWrite = DateTime.SpecifyKind(new DateTime(2020, 1, 1), DateTimeKind.Utc);

    private readonly ApplicationDbContext _context;
    private readonly CapturingLogger _logger = new();

    public ServedManifestTrackerTests()
    {
        _context = new ApplicationDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options,
            TestQrCipher.Instance);
    }

    public void Dispose()
    {
        _context.Database.EnsureDeleted();
        _context.Dispose();
    }

    [Fact]
    public async Task TheFirstManifestForARuntime_IsRecordedUnderAPerChannelKey()
    {
        using var metrics = new FuelFlowMetrics();

        await ObserveAsync(Manifest("update-1", "abc123", "2026-10-30T10:00:00+00:00"), metrics);

        var row = await StoredRowAsync();
        row.Should().NotBeNull("the channel's first manifest is the baseline the next one is compared with");

        var stored = StoredJson(row!);
        stored.GetProperty("id").GetString().Should().Be("update-1");
        stored.GetProperty("commit").GetString().Should().Be("abc123");
        stored.GetProperty("committedAt").GetString().Should().Contain("2026-10-30");
    }

    [Fact]
    public async Task TheSameManifestAgain_WritesNothing()
    {
        await SeedAsync(JsonSerializer.Serialize(new
        {
            id = "update-1",
            commit = "abc123",
            committedAt = "2026-10-30T10:00:00+00:00"
        }));
        using var metrics = new FuelFlowMetrics();

        // Every app launch asks for the manifest; only the first one may cost a write.
        await ObserveAsync(Manifest("update-1", "abc123", "2026-10-30T10:00:00+00:00"), metrics);

        (await StoredRowAsync())!.UpdatedAtUtc.Should().Be(SeededWrite,
            "a request that finds the manifest it already knows must not write to app_settings");
    }

    [Fact]
    public async Task AManifestOlderThanTheOneServed_IsAWarningAndAMetric()
    {
        await SeedAsync(JsonSerializer.Serialize(new
        {
            id = "update-2",
            commit = "newer",
            committedAt = "2026-10-31T09:00:00+00:00"
        }));

        var readings = new List<long>();
        using var listener = ListenForRollbacks(readings);
        using var metrics = new FuelFlowMetrics();

        await ObserveAsync(Manifest("update-1", "older", "2026-10-30T10:00:00+00:00"), metrics);

        var entry = _logger.Entries.Should().ContainSingle().Subject;
        entry.Level.Should().Be(LogLevel.Warning);
        entry.Message.Should().Contain("BACKWARD").And.Contain("older").And.Contain("newer");

        readings.Should().ContainSingle().Which.Should().Be(1);

        // The record follows what is actually being served, so the next comparison is honest.
        StoredJson((await StoredRowAsync())!).GetProperty("id").GetString().Should().Be("update-1");
    }

    [Fact]
    public async Task AManifestNewerThanTheOneServed_IsSilent()
    {
        await SeedAsync(JsonSerializer.Serialize(new
        {
            id = "update-1",
            commit = "older",
            committedAt = "2026-10-30T10:00:00+00:00"
        }));

        var readings = new List<long>();
        using var listener = ListenForRollbacks(readings);
        using var metrics = new FuelFlowMetrics();

        await ObserveAsync(Manifest("update-2", "newer", "2026-10-31T09:00:00+00:00"), metrics);

        _logger.Entries.Should().BeEmpty("a forward publish is the normal case and must not page anybody");
        readings.Should().BeEmpty();
        StoredJson((await StoredRowAsync())!).GetProperty("id").GetString().Should().Be("update-2");
    }

    [Fact]
    public async Task AManifestPublishedBeforeTheStamp_IsNeitherRecordedNorCompared()
    {
        await SeedAsync(JsonSerializer.Serialize(new
        {
            id = "update-2",
            commit = "newer",
            committedAt = "2026-10-31T09:00:00+00:00"
        }));
        using var metrics = new FuelFlowMetrics();

        // No metadata at all: the shape every manifest had before this change.
        await ObserveAsync(Manifest("update-3"), metrics);
        // A commit but no commit time: still nothing to compare on.
        await ObserveAsync(Manifest("update-4", "some-commit"), metrics);

        _logger.Entries.Should().BeEmpty();
        StoredJson((await StoredRowAsync())!).GetProperty("id").GetString().Should().Be("update-2",
            "an unstamped manifest says nothing about age, so it must neither invent a baseline nor erase one");
    }

    [Fact]
    public async Task AManifestThatIsNotJson_DoesNotThrowAndWritesNothing()
    {
        using var metrics = new FuelFlowMetrics();

        await ObserveAsync(Encoding.UTF8.GetBytes("not json at all"), metrics);
        await ObserveAsync(Encoding.UTF8.GetBytes("[\"an\",\"array\"]"), metrics);
        await ObserveAsync(Encoding.UTF8.GetBytes("{\"metadata\":{\"commit\":\"abc\"}}"), metrics);

        (await StoredRowAsync()).Should().BeNull();
    }

    [Fact]
    public async Task AStoredRowThatCannotBeRead_IsReplacedRatherThanTrusted()
    {
        await SeedAsync("this is not json");
        using var metrics = new FuelFlowMetrics();

        await ObserveAsync(Manifest("update-1", "abc123", "2026-10-30T10:00:00+00:00"), metrics);

        _logger.Entries.Should().BeEmpty("a corrupt row is not a baseline, so nothing went backward");
        StoredJson((await StoredRowAsync())!).GetProperty("id").GetString().Should().Be("update-1");
    }

    [Fact]
    public async Task ARuntimeVersionTooLongForTheSettingKey_IsNotRecorded()
    {
        using var metrics = new FuelFlowMetrics();

        await ObserveAsync(
            Manifest("update-1", "abc123", "2026-10-30T10:00:00+00:00"),
            metrics,
            runtimeVersion: new string('a', 49));

        (await _context.AppSettings.AsNoTracking().ToListAsync()).Should().BeEmpty(
            "app_settings.key is varchar(100); the header is anonymous, so a key that cannot fit is skipped");
    }

    [Fact]
    public async Task AnAuditThatCannotReachTheDatabase_DoesNotEscape()
    {
        // Best-effort by design: whatever breaks in the bookkeeping, the update still ships.
        var unreachable = new ApplicationDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options,
            TestQrCipher.Instance);
        var tracker = new ServedManifestTracker(
            new RuntimeSettingsService(unreachable), new FuelFlowMetrics(), _logger);
        unreachable.Dispose();

        await tracker.ObserveAsync(
            Platform, Runtime,
            Manifest("update-1", "abc123", "2026-10-30T10:00:00+00:00"),
            CancellationToken.None);

        var entry = _logger.Entries.Should().ContainSingle().Subject;
        entry.Level.Should().Be(LogLevel.Warning);
        entry.Message.Should().Contain("unaffected");
    }

    // --- helpers -------------------------------------------------------------------------

    private Task ObserveAsync(byte[] manifest, FuelFlowMetrics metrics, string runtimeVersion = Runtime) =>
        new ServedManifestTracker(new RuntimeSettingsService(_context), metrics, _logger)
            .ObserveAsync(Platform, runtimeVersion, manifest, CancellationToken.None);

    private static byte[] Manifest(string id, string? commit = null, string? committedAt = null)
    {
        var metadata = commit is null && committedAt is null
            ? "{}"
            : JsonSerializer.Serialize(new { commit, committedAt });

        return Encoding.UTF8.GetBytes($"{{\"id\":\"{id}\",\"metadata\":{metadata}}}");
    }

    private async Task SeedAsync(string value)
    {
        _context.AppSettings.Add(new AppSetting
        {
            Key = Key,
            Value = value,
            UpdatedAtUtc = SeededWrite
        });
        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();
    }

    private Task<AppSetting?> StoredRowAsync() =>
        _context.AppSettings.AsNoTracking().FirstOrDefaultAsync(s => s.Key == Key);

    private static JsonElement StoredJson(AppSetting row) => JsonSerializer.Deserialize<JsonElement>(row.Value);

    private static MeterListener ListenForRollbacks(List<long> readings)
    {
        var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == ObservabilityConstants.MeterName)
                l.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<long>((instrument, value, _, _) =>
        {
            if (instrument.Name == "fuelflow.updates.manifest_rollback")
                readings.Add(value);
        });
        listener.Start();
        return listener;
    }

    private sealed class CapturingLogger : ILogger<ServedManifestTracker>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            Entries.Add((logLevel, formatter(state, exception)));
    }
}
