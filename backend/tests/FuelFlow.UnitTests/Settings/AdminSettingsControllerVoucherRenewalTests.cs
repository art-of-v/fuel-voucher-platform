using FluentAssertions;
using FuelFlow.Features.Settings;
using FuelFlow.Features.Vouchers.Renewal;
using FuelFlow.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FuelFlow.UnitTests.Settings;

/// <summary>
/// Covers the admin surface of the voucher-renewal config (Slice 2): the PUT clamps/validation and
/// that a partial PUT touching only the renewal section leaves the other settings alone.
/// </summary>
public sealed class AdminSettingsControllerVoucherRenewalTests : IDisposable
{
    private readonly ApplicationDbContext _context;
    private readonly RuntimeSettingsService _settings;
    private readonly AdminSettingsController _controller;

    public AdminSettingsControllerVoucherRenewalTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking)
            .Options;

        _context = new ApplicationDbContext(options);
        _settings = new RuntimeSettingsService(_context);
        _controller = new AdminSettingsController(_settings)
        {
            // No authenticated user — GetUserId/GetUserName resolve to null, which is fine here.
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
    }

    public void Dispose()
    {
        _context.Database.EnsureDeleted();
        _context.Dispose();
    }

    [Fact]
    public async Task Update_EmptyRequest_ShouldBadRequest()
    {
        var result = await _controller.Update(new UpdateSettingsRequest(), CancellationToken.None);
        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task Update_ShouldPersistFlagThresholdAndTiers_AndRoundTripViaGet()
    {
        await _controller.Update(new UpdateSettingsRequest
        {
            VoucherRenewal = new VoucherRenewalSettingsDto
            {
                Enabled = true,
                TriggerThresholdDays = 21,
                Tiers = new List<VoucherRenewalTierSettingsDto>
                {
                    new() { Term = "3m", Enabled = true, RatePerLiterUah = 24.5m },
                    new() { Term = "1w", Enabled = false, RatePerLiterUah = 10m }
                }
            }
        }, CancellationToken.None);

        var config = await _settings.GetVoucherRenewalConfigAsync();
        config.Enabled.Should().BeTrue();
        config.TriggerThresholdDays.Should().Be(21);

        var threeM = config.Tier(VoucherRenewalTerm.ThreeMonths)!;
        threeM.Enabled.Should().BeTrue();
        threeM.RatePerLiterUah.Should().Be(24.5m);
        threeM.IsOfferable.Should().BeTrue();

        var oneW = config.Tier(VoucherRenewalTerm.OneWeek)!;
        oneW.Enabled.Should().BeFalse();
        oneW.RatePerLiterUah.Should().Be(10m);
        oneW.IsOfferable.Should().BeFalse(); // disabled ⇒ not sellable even though priced
    }

    [Theory]
    [InlineData(0, 1)]      // 0/negative would never surface the button → clamp up to 1
    [InlineData(-5, 1)]
    [InlineData(1000, 365)] // runaway window → clamp down to a year
    [InlineData(30, 30)]    // in-range untouched
    public async Task Update_ShouldClampThresholdToWholeDayYearRange(int input, int expected)
    {
        await _controller.Update(new UpdateSettingsRequest
        {
            VoucherRenewal = new VoucherRenewalSettingsDto { Enabled = true, TriggerThresholdDays = input }
        }, CancellationToken.None);

        (await _settings.GetVoucherRenewalThresholdDaysAsync()).Should().Be(expected);
    }

    [Fact]
    public async Task Update_ShouldClampNegativeRateToZero_LeavingTierNotOfferable()
    {
        await _controller.Update(new UpdateSettingsRequest
        {
            VoucherRenewal = new VoucherRenewalSettingsDto
            {
                Enabled = true,
                TriggerThresholdDays = 14,
                Tiers = new List<VoucherRenewalTierSettingsDto>
                {
                    new() { Term = "2m", Enabled = true, RatePerLiterUah = -3m }
                }
            }
        }, CancellationToken.None);

        var tier = (await _settings.GetVoucherRenewalConfigAsync()).Tier(VoucherRenewalTerm.TwoMonths)!;
        tier.RatePerLiterUah.Should().Be(0m);
        tier.IsOfferable.Should().BeFalse();
    }

    [Fact]
    public async Task Update_ShouldIgnoreUnknownTierCodes()
    {
        await _controller.Update(new UpdateSettingsRequest
        {
            VoucherRenewal = new VoucherRenewalSettingsDto
            {
                Enabled = true,
                TriggerThresholdDays = 14,
                Tiers = new List<VoucherRenewalTierSettingsDto>
                {
                    new() { Term = "7d", Enabled = true, RatePerLiterUah = 5m },  // bogus
                    new() { Term = "6m", Enabled = true, RatePerLiterUah = 30m }  // real
                }
            }
        }, CancellationToken.None);

        var config = await _settings.GetVoucherRenewalConfigAsync();
        config.Tier(VoucherRenewalTerm.SixMonths)!.IsOfferable.Should().BeTrue();
        // The bogus code persisted no row, so it can't have leaked into any real tier.
        config.Tiers.Where(t => t.IsOfferable).Should().ContainSingle();
    }

    [Fact]
    public async Task Update_RenewalSection_ShouldNotClobberOtherSettings()
    {
        // Seed an unrelated section first.
        await _controller.Update(new UpdateSettingsRequest
        {
            OrderCleanup = new OrderCleanupSettingsDto { Enabled = true, RetentionDays = 45 }
        }, CancellationToken.None);

        // A partial PUT touching only renewal must leave order-cleanup intact.
        await _controller.Update(new UpdateSettingsRequest
        {
            VoucherRenewal = new VoucherRenewalSettingsDto { Enabled = true, TriggerThresholdDays = 10 }
        }, CancellationToken.None);

        (await _settings.IsOrderCleanupEnabledAsync()).Should().BeTrue();
        (await _settings.GetOrderCleanupRetentionDaysAsync()).Should().Be(45);
        (await _settings.IsVoucherRenewalEnabledAsync()).Should().BeTrue();
    }
}
