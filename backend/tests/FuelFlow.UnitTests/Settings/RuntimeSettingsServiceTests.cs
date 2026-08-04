using FluentAssertions;
using FuelFlow.Features.Settings;
using FuelFlow.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FuelFlow.UnitTests.Settings;

public sealed class RuntimeSettingsServiceTests : IDisposable
{
    private readonly ApplicationDbContext _context;
    private readonly RuntimeSettingsService _service;

    public RuntimeSettingsServiceTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking)
            .Options;

        _context = new ApplicationDbContext(options);
        _service = new RuntimeSettingsService(_context);
    }

    public void Dispose()
    {
        _context.Database.EnsureDeleted();
        _context.Dispose();
    }

    [Fact]
    public async Task GetValue_ShouldReturnDefault_WhenKeyMissing()
    {
        (await _service.GetBoolAsync("AutoRefund:Enabled")).Should().BeFalse();
        (await _service.GetIntAsync("AutoRefund:DelayDays", 7)).Should().Be(7);
    }

    [Fact]
    public async Task Upsert_ShouldPersistNewValue_AndUpdateExistingInPlace()
    {
        await _service.UpsertAsync("AutoRefund:Enabled", "true");
        (await _service.GetBoolAsync("AutoRefund:Enabled")).Should().BeTrue();

        await _service.UpsertAsync("AutoRefund:Enabled", "false");
        (await _service.GetBoolAsync("AutoRefund:Enabled")).Should().BeFalse();

        var rows = await _context.AppSettings
            .Where(s => s.Key == "AutoRefund:Enabled")
            .ToListAsync();
        rows.Should().ContainSingle();
        rows[0].Value.Should().Be("false");
    }

    [Fact]
    public async Task Upsert_ShouldIgnoreMalformedValue_AndFallBackToDefault()
    {
        await _service.UpsertAsync("AutoRefund:DelayDays", "not-a-number");
        (await _service.GetIntAsync("AutoRefund:DelayDays", 7)).Should().Be(7);
    }
}
