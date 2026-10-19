using FluentAssertions;
using FuelFlow.Features.Pricing.Terms.SharedModels;
using FuelFlow.Features.Settings;
using FuelFlow.Features.Settings.SharedModels;
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

        _context = new ApplicationDbContext(options, TestQrCipher.Instance);
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
    public async Task HangfireCleanup_ShouldDefaultToOn_AndToAWeek()
    {
        // The opposite of every other cleanup switch, and deliberately. The rows it removes are the
        // scheduler's own record of work that already completed — no money, no customer, no
        // settlement — and they are the only thing on the box with an unbounded growth curve. Off by
        // default would leave that curve going.
        (await _service.IsHangfireCleanupEnabledAsync()).Should().BeTrue();
        (await _service.GetHangfireCleanupRetentionDaysAsync())
            .Should().Be(RuntimeSettingsService.DefaultHangfireCleanupRetentionDays)
            .And.Be(7);
    }

    [Fact]
    public async Task HangfireCleanup_ShouldReflectPersistedValues()
    {
        await _service.UpsertAsync(AppSettingKeys.HangfireCleanupEnabled, "false");
        (await _service.IsHangfireCleanupEnabledAsync()).Should().BeFalse();

        await _service.UpsertAsync(AppSettingKeys.HangfireCleanupEnabled, "true");
        (await _service.IsHangfireCleanupEnabledAsync()).Should().BeTrue();

        await _service.UpsertAsync(AppSettingKeys.HangfireCleanupRetentionDays, "30");
        (await _service.GetHangfireCleanupRetentionDaysAsync()).Should().Be(30);
    }

    [Fact]
    public async Task HangfireCleanup_AMalformedValue_LeavesItRunning()
    {
        // A typo must not silently stop the one thing that keeps the scheduler's table bounded. The
        // price of the typo is that the cleanup keeps running, which is the documented default —
        // the opposite of the "1" or "yes" trap the other switches have, where the typo stops them.
        await _service.UpsertAsync(AppSettingKeys.HangfireCleanupEnabled, "definitely-not-a-bool");
        (await _service.IsHangfireCleanupEnabledAsync()).Should().BeTrue();
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

    // --- The dated table over the key/value fallback ---------------------------------------
    // The migration moves the live ladder across, so from that point the table is what the quote
    // reads. The key/value row stays as a fallback for a tier with no row here, which is also what
    // makes deploy order irrelevant. These four pin the precedence and the write behaviour; the
    // partial unique index that backs "one open row" is asserted against real Postgres separately.

    private VoucherTermPricing OpenTier(string scheme, VoucherRenewalTerm term, bool enabled, decimal rate)
    {
        var row = new VoucherTermPricing
        {
            Id = Guid.NewGuid(),
            Scheme = scheme,
            TermCode = term.Code(),
            Enabled = enabled,
            RatePerLiter = rate,
            ValidFrom = DateTime.UtcNow.AddDays(-1),
            ValidTo = null,
            CreatedAtUtc = DateTime.UtcNow.AddDays(-1)
        };
        _context.VoucherTermPricings.Add(row);
        return row;
    }

    [Fact]
    public async Task ADatedRenewalTier_IsPreferredOverTheKeyValueSetting()
    {
        await _service.UpsertAsync(AppSettingKeysProxy.TierEnabled(VoucherRenewalTerm.OneWeek), "false");
        await _service.UpsertAsync(AppSettingKeysProxy.TierRate(VoucherRenewalTerm.OneWeek), "10");

        OpenTier(VoucherTermPricingSchemes.RenewalRate, VoucherRenewalTerm.OneWeek, enabled: true, rate: 24.5m);
        await _context.SaveChangesAsync();

        var tier = (await _service.GetVoucherRenewalConfigAsync()).Tier(VoucherRenewalTerm.OneWeek)!;
        tier.Enabled.Should().BeTrue();
        tier.RatePerLiterUah.Should().Be(24.5m);
    }

    [Fact]
    public async Task ATierWithNoDatedRow_FallsBackToTheKeyValueSetting()
    {
        await _service.UpsertAsync(AppSettingKeysProxy.TierEnabled(VoucherRenewalTerm.TwoMonths), "true");
        await _service.UpsertAsync(AppSettingKeysProxy.TierRate(VoucherRenewalTerm.TwoMonths), "27");

        // A different tier is dated, to prove the fallback is per tier and not "table is non-empty".
        OpenTier(VoucherTermPricingSchemes.RenewalRate, VoucherRenewalTerm.OneWeek, enabled: true, rate: 24.5m);
        await _context.SaveChangesAsync();

        var tier = (await _service.GetVoucherRenewalConfigAsync()).Tier(VoucherRenewalTerm.TwoMonths)!;
        tier.Enabled.Should().BeTrue();
        tier.RatePerLiterUah.Should().Be(27m);
    }

    [Fact]
    public async Task AChangedTier_ClosesTheRowItReplaces_SoTheHistoryIsKept()
    {
        var original = OpenTier(VoucherTermPricingSchemes.PurchaseTermDiscount, VoucherRenewalTerm.OneMonth, enabled: true, rate: 3m);
        await _context.SaveChangesAsync();

        var appended = await _service.SetTierAsync(
            VoucherTermPricingSchemes.PurchaseTermDiscount,
            VoucherRenewalTerm.OneMonth.Code(),
            enabled: true,
            ratePerLiter: 4.5m,
            updatedByUserId: null,
            updatedByUserName: "tester");
        appended.Should().BeTrue();

        _context.ChangeTracker.Clear();
        var rows = await _context.VoucherTermPricings
            .Where(r => r.Scheme == VoucherTermPricingSchemes.PurchaseTermDiscount)
            .OrderBy(r => r.ValidFrom)
            .ToListAsync();

        rows.Should().HaveCount(2);
        rows[0].Id.Should().Be(original.Id);
        rows[0].RatePerLiter.Should().Be(3m);
        rows[0].ValidTo.Should().NotBeNull();
        rows[0].ValidTo.Should().BeAfter(rows[0].ValidFrom);
        rows[1].ValidTo.Should().BeNull();
        rows[1].RatePerLiter.Should().Be(4.5m);

        var tier = (await _service.GetVoucherTermConfigAsync()).Tier(VoucherRenewalTerm.OneMonth)!;
        tier.DiscountPerLiterUah.Should().Be(4.5m);
    }

    [Fact]
    public async Task AnUnchangedTier_WritesNoRow_SoSavingOtherSettingsDoesNotFillTheHistoryWithNoise()
    {
        OpenTier(VoucherTermPricingSchemes.RenewalRate, VoucherRenewalTerm.OneWeek, enabled: true, rate: 24.5m);
        await _context.SaveChangesAsync();

        // The Settings screen submits the whole ladder on every save, most of it untouched. Only a
        // real change may append history.
        var appended = await _service.SetTierAsync(
            VoucherTermPricingSchemes.RenewalRate,
            VoucherRenewalTerm.OneWeek.Code(),
            enabled: true,
            ratePerLiter: 24.5m,
            updatedByUserId: null,
            updatedByUserName: "tester");
        appended.Should().BeFalse();

        var rows = await _context.VoucherTermPricings.ToListAsync();
        rows.Should().HaveCount(1);
        rows[0].ValidTo.Should().BeNull();
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
