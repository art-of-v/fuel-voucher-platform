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
        _controller = new AdminSettingsController(_settings, _context)
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
            DeletedUnpaidOrderCleanup = new DeletedUnpaidOrderCleanupSettingsDto { Enabled = true, RetentionDays = 45 }
        }, CancellationToken.None);

        // A partial PUT touching only renewal must leave order-cleanup intact.
        await _controller.Update(new UpdateSettingsRequest
        {
            VoucherRenewal = new VoucherRenewalSettingsDto { Enabled = true, TriggerThresholdDays = 10 }
        }, CancellationToken.None);

        (await _settings.IsDeletedUnpaidOrderCleanupEnabledAsync()).Should().BeTrue();
        (await _settings.GetDeletedUnpaidOrderCleanupRetentionDaysAsync()).Should().Be(45);
        (await _settings.IsVoucherRenewalEnabledAsync()).Should().BeTrue();
    }

    // ── The purchase term ladder (Slice 3) ─────────────────────────────────────────────────────
    //
    // Same admin surface, different risk: these discounts are subtracted from the price at checkout, so a
    // value written here reaches a money path. The clamps below are the only thing standing between a
    // fat-fingered input and an under-cost sale.

    [Fact]
    public async Task UpdateVoucherTerm_ShouldPersistFlagAndTiers_AndRoundTripViaGet()
    {
        await _controller.Update(new UpdateSettingsRequest
        {
            VoucherTerm = new VoucherTermSettingsDto
            {
                Enabled = true,
                Tiers = new List<VoucherTermTierSettingsDto>
                {
                    new() { Term = "1w", Enabled = true, DiscountPerLiterUah = 7.5m },
                    new() { Term = "2m", Enabled = true, DiscountPerLiterUah = 2m }
                }
            }
        }, CancellationToken.None);

        var config = await _settings.GetVoucherTermConfigAsync();
        config.Enabled.Should().BeTrue();

        var week = config.Tier(VoucherRenewalTerm.OneWeek)!;
        week.Enabled.Should().BeTrue();
        week.DiscountPerLiterUah.Should().Be(7.5m);
        week.IsOfferable.Should().BeTrue();

        var twoMonths = config.Tier(VoucherRenewalTerm.TwoMonths)!;
        twoMonths.IsOfferable.Should().BeTrue();

        // A tier nobody priced stays in the ladder but unsellable.
        config.Tier(VoucherRenewalTerm.SixMonths)!.IsOfferable.Should().BeFalse();
    }

    [Fact]
    public async Task UpdateVoucherTerm_ShouldIgnoreUnknownTierCodes()
    {
        // A bogus code would otherwise persist as an orphan AppSettings row nobody ever reads again.
        await _controller.Update(new UpdateSettingsRequest
        {
            VoucherTerm = new VoucherTermSettingsDto
            {
                Enabled = true,
                Tiers = new List<VoucherTermTierSettingsDto>
                {
                    new() { Term = "7d", Enabled = true, DiscountPerLiterUah = 5m },  // bogus
                    new() { Term = "1w", Enabled = true, DiscountPerLiterUah = 9m }
                }
            }
        }, CancellationToken.None);

        _context.AppSettings.Should().NotContain(s => s.Key!.Contains("7d"));
        (await _settings.GetVoucherTermConfigAsync()).Tier(VoucherRenewalTerm.OneWeek)!
            .DiscountPerLiterUah.Should().Be(9m);
    }

    [Theory]
    [InlineData(-3, 0)]       // negative discount would raise the price, not lower it
    [InlineData(0, 0)]        // zero is allowed but leaves the tier unbuyable
    [InlineData(12.5, 12.5)]  // in-range untouched
    public async Task UpdateVoucherTerm_ShouldClampNegativeDiscountToZero(decimal input, decimal expected)
    {
        await _controller.Update(new UpdateSettingsRequest
        {
            VoucherTerm = new VoucherTermSettingsDto
            {
                Enabled = true,
                Tiers = new List<VoucherTermTierSettingsDto>
                {
                    new() { Term = "1w", Enabled = true, DiscountPerLiterUah = input }
                }
            }
        }, CancellationToken.None);

        var tier = (await _settings.GetVoucherTermConfigAsync()).Tier(VoucherRenewalTerm.OneWeek)!;
        tier.DiscountPerLiterUah.Should().Be(expected);
        tier.IsOfferable.Should().Be(expected > 0m);
    }

    [Fact]
    public async Task UpdateVoucherTerm_ShouldClampAnAbsurdDiscountSoItCannotOverflow()
    {
        // Not a business limit - a sanity limit. A discount of 10^30 would survive every money comparison
        // downstream and then fail to convert, so it is capped well above any real litre price instead.
        await _controller.Update(new UpdateSettingsRequest
        {
            VoucherTerm = new VoucherTermSettingsDto
            {
                Enabled = true,
                Tiers = new List<VoucherTermTierSettingsDto>
                {
                    new() { Term = "1w", Enabled = true, DiscountPerLiterUah = decimal.MaxValue }
                }
            }
        }, CancellationToken.None);

        (await _settings.GetVoucherTermConfigAsync()).Tier(VoucherRenewalTerm.OneWeek)!
            .DiscountPerLiterUah.Should().Be(1_000_000m);
    }

    [Fact]
    public async Task UpdateVoucherTerm_ShouldLeaveTheRenewalSectionAlone()
    {
        // The two ladders are independent: renewing longer and buying shorter are separate decisions, and
        // saving one must not silently reconfigure the other.
        await _controller.Update(new UpdateSettingsRequest
        {
            VoucherRenewal = new VoucherRenewalSettingsDto { Enabled = true, TriggerThresholdDays = 10 },
            VoucherTerm = new VoucherTermSettingsDto
            {
                Enabled = true,
                Tiers = new List<VoucherTermTierSettingsDto> { new() { Term = "1w", Enabled = true, DiscountPerLiterUah = 3m } }
            }
        }, CancellationToken.None);

        var renewal = await _settings.GetVoucherRenewalConfigAsync();
        renewal.Enabled.Should().BeTrue();
        renewal.TriggerThresholdDays.Should().Be(10);
        (await _settings.GetVoucherTermConfigAsync()).Enabled.Should().BeTrue();
    }

    [Fact]
    public async Task Update_OnlyVoucherTerm_ShouldNotTouchTheOtherSections()
    {
        // Partial PUT: a manager editing the purchase ladder must not reset unrelated settings to defaults.
        _context.AppSettings.AddRange(
            new FuelFlow.Features.Settings.SharedModels.AppSetting
            {
                Key = FuelFlow.Features.Settings.SharedModels.AppSettingKeys.VoucherRenewalEnabled,
                Value = "true",
                UpdatedAtUtc = DateTime.UtcNow
            },
            new FuelFlow.Features.Settings.SharedModels.AppSetting
            {
                Key = FuelFlow.Features.Settings.SharedModels.AppSettingKeys.DeletedUnpaidOrderCleanupRetentionDays,
                Value = "45",
                UpdatedAtUtc = DateTime.UtcNow
            });
        _context.SaveChanges();

        await _controller.Update(new UpdateSettingsRequest
        {
            VoucherTerm = new VoucherTermSettingsDto { Enabled = true }
        }, CancellationToken.None);

        (await _settings.IsVoucherRenewalEnabledAsync()).Should().BeTrue();
        (await _settings.GetDeletedUnpaidOrderCleanupRetentionDaysAsync()).Should().Be(45);
    }
}
