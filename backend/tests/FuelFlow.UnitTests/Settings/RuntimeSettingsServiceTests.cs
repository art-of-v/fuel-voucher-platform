using FluentAssertions;
using FuelFlow.Features.Settings;
using FuelFlow.Features.Vouchers.Renewal;
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

    [Fact]
    public async Task DeletedUnpaidOrderCleanup_ShouldFallBackToFailSafeDefaults_WhenUnset()
    {
        // Fail-safe: an irreversible purge must never run, and never with a zero/short window,
        // until an admin has explicitly opted in.
        (await _service.IsDeletedUnpaidOrderCleanupEnabledAsync()).Should().BeFalse();
        (await _service.GetDeletedUnpaidOrderCleanupRetentionDaysAsync())
            .Should().Be(RuntimeSettingsService.DefaultDeletedUnpaidOrderCleanupRetentionDays)
            .And.Be(30);
    }

    [Fact]
    public async Task DeletedUnpaidOrderCleanup_ShouldReflectPersistedValues()
    {
        await _service.UpsertAsync("DeletedUnpaidOrderCleanup:Enabled", "true");
        await _service.UpsertAsync("DeletedUnpaidOrderCleanup:RetentionDays", "14");

        (await _service.IsDeletedUnpaidOrderCleanupEnabledAsync()).Should().BeTrue();
        (await _service.GetDeletedUnpaidOrderCleanupRetentionDaysAsync()).Should().Be(14);
    }

    [Fact]
    public async Task DataRetention_ShouldDefaultToFalse_WhenUnset()
    {
        // Fail-safe: the retention job never deletes until an admin explicitly opts in.
        (await _service.IsDataRetentionEnabledAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task DataRetention_ShouldReflectPersistedValue()
    {
        await _service.UpsertAsync("DataRetention:Enabled", "true");
        (await _service.IsDataRetentionEnabledAsync()).Should().BeTrue();

        await _service.UpsertAsync("DataRetention:Enabled", "false");
        (await _service.IsDataRetentionEnabledAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task VoucherRenewal_ShouldFallBackToFailSafeDefaults_WhenUnset()
    {
        // Fail-safe: nothing renews and no tier is offerable until a manager opts in AND sets prices.
        (await _service.IsVoucherRenewalEnabledAsync()).Should().BeFalse();
        (await _service.GetVoucherRenewalThresholdDaysAsync())
            .Should().Be(RuntimeSettingsService.DefaultVoucherRenewalThresholdDays)
            .And.Be(14);

        var config = await _service.GetVoucherRenewalConfigAsync();
        config.Enabled.Should().BeFalse();
        config.TriggerThresholdDays.Should().Be(14);
        config.Tiers.Should().HaveCount(8);
        config.Tiers.Should().OnlyContain(t => !t.Enabled && t.RatePerLiterUah == 0m && !t.IsOfferable);
    }

    [Fact]
    public async Task VoucherRenewal_ShouldReflectPersistedConfig()
    {
        await _service.UpsertAsync(AppSettingKeysProxy.Enabled, "true");
        await _service.UpsertAsync(AppSettingKeysProxy.Threshold, "21");
        await _service.UpsertAsync(AppSettingKeysProxy.TierEnabled(VoucherRenewalTerm.ThreeMonths), "true");
        await _service.UpsertAsync(AppSettingKeysProxy.TierRate(VoucherRenewalTerm.ThreeMonths), "24.5");

        var config = await _service.GetVoucherRenewalConfigAsync();

        config.Enabled.Should().BeTrue();
        config.TriggerThresholdDays.Should().Be(21);

        var tier = config.Tier(VoucherRenewalTerm.ThreeMonths)!;
        tier.Enabled.Should().BeTrue();
        tier.RatePerLiterUah.Should().Be(24.5m);
        tier.IsOfferable.Should().BeTrue();
    }

    [Fact]
    public async Task VoucherRenewal_TierEnabledButUnpriced_IsNotOfferable()
    {
        // Enabled with no (or zero) rate must never be sold — we would otherwise charge 0 UAH.
        await _service.UpsertAsync(AppSettingKeysProxy.Enabled, "true");
        await _service.UpsertAsync(AppSettingKeysProxy.TierEnabled(VoucherRenewalTerm.OneWeek), "true");

        var tier = (await _service.GetVoucherRenewalConfigAsync()).Tier(VoucherRenewalTerm.OneWeek)!;
        tier.Enabled.Should().BeTrue();
        tier.RatePerLiterUah.Should().Be(0m);
        tier.IsOfferable.Should().BeFalse();
    }

    [Fact]
    public async Task VoucherRenewal_PricedButDisabled_IsNotOfferable()
    {
        await _service.UpsertAsync(AppSettingKeysProxy.TierRate(VoucherRenewalTerm.SixMonths), "30");

        var tier = (await _service.GetVoucherRenewalConfigAsync()).Tier(VoucherRenewalTerm.SixMonths)!;
        tier.Enabled.Should().BeFalse();
        tier.IsOfferable.Should().BeFalse();
    }

    [Fact]
    public async Task VoucherRenewal_MalformedRate_FallsBackToZero()
    {
        await _service.UpsertAsync(AppSettingKeysProxy.TierEnabled(VoucherRenewalTerm.OneMonth), "true");
        await _service.UpsertAsync(AppSettingKeysProxy.TierRate(VoucherRenewalTerm.OneMonth), "not-a-number");

        var tier = (await _service.GetVoucherRenewalConfigAsync()).Tier(VoucherRenewalTerm.OneMonth)!;
        tier.RatePerLiterUah.Should().Be(0m);
        tier.IsOfferable.Should().BeFalse();
    }

    // Thin shim so the persisted-config tests spell the app_settings keys exactly as production does,
    // without repeating the raw "VoucherRenewal:..." strings in every arrange step.
    private static class AppSettingKeysProxy
    {
        public const string Enabled = FuelFlow.Features.Settings.SharedModels.AppSettingKeys.VoucherRenewalEnabled;
        public const string Threshold = FuelFlow.Features.Settings.SharedModels.AppSettingKeys.VoucherRenewalTriggerThresholdDays;
        public static string TierEnabled(VoucherRenewalTerm term)
            => FuelFlow.Features.Settings.SharedModels.AppSettingKeys.VoucherRenewalTierEnabled(term.Code());
        public static string TierRate(VoucherRenewalTerm term)
            => FuelFlow.Features.Settings.SharedModels.AppSettingKeys.VoucherRenewalTierRatePerLiter(term.Code());
    }
}
