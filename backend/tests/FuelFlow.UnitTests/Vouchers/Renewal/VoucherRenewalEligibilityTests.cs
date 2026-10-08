using FluentAssertions;
using FuelFlow.Features.Vouchers.Renewal;
using FuelFlow.Features.Vouchers.SharedModels;
using Xunit;

namespace FuelFlow.UnitTests.Vouchers.Renewal;

public sealed class VoucherRenewalEligibilityTests
{
    private static readonly DateOnly Today = new(2026, 6, 1);
    private const int Threshold = 14;

    /// <summary>The whole enabled ladder, as the quote and checkout handlers pass it.</summary>
    private static readonly IReadOnlyList<VoucherRenewalTerm> AllTerms = VoucherRenewalTerms.All;

    [Theory]
    [InlineData(VoucherStatus.Assigned, true)]
    [InlineData(VoucherStatus.Expired, true)]
    [InlineData(VoucherStatus.Used, false)]
    [InlineData(VoucherStatus.Blocked, false)]
    [InlineData(VoucherStatus.Deactivated, false)]
    [InlineData(VoucherStatus.Available, false)]
    [InlineData(VoucherStatus.Imported, false)]
    public void IsRenewableStatus_OnlyAssignedOrExpired(VoucherStatus status, bool expected)
    {
        VoucherRenewalEligibility.IsRenewableStatus(status).Should().Be(expected);
    }

    [Fact]
    public void TryResolveBranch_StillValidWithinWindow_IsExtend()
    {
        var expiry = Today.AddDays(5);
        VoucherRenewalEligibility.TryResolveBranch(VoucherStatus.Assigned, expiry, expiry.AddYears(1), Today, Threshold, AllTerms, out var branch)
            .Should().BeTrue();
        branch.Should().Be(VoucherRenewalBranch.Extend);
    }

    [Fact]
    public void TryResolveBranch_AtWindowBoundary_IsEligibleExtend()
    {
        var expiry = Today.AddDays(Threshold); // exactly today + threshold is inclusive
        VoucherRenewalEligibility.TryResolveBranch(VoucherStatus.Assigned, expiry, expiry.AddYears(1), Today, Threshold, AllTerms, out var branch)
            .Should().BeTrue();
        branch.Should().Be(VoucherRenewalBranch.Extend);
    }

    [Fact]
    public void TryResolveBranch_ExpiringToday_IsExtend()
    {
        VoucherRenewalEligibility.TryResolveBranch(VoucherStatus.Assigned, Today, Today.AddYears(1), Today, Threshold, AllTerms, out var branch)
            .Should().BeTrue();
        branch.Should().Be(VoucherRenewalBranch.Extend);
    }

    [Fact]
    public void TryResolveBranch_BeyondWindow_IsNotEligible()
    {
        var expiry = Today.AddDays(Threshold + 1);
        VoucherRenewalEligibility.TryResolveBranch(VoucherStatus.Assigned, expiry, expiry.AddYears(1), Today, Threshold, AllTerms, out _)
            .Should().BeFalse();
    }

    [Fact]
    public void TryResolveBranch_LapsedButStillAssigned_IsReplace()
    {
        var expiry = Today.AddDays(-1);
        VoucherRenewalEligibility.TryResolveBranch(VoucherStatus.Assigned, expiry, expiry.AddYears(1), Today, Threshold, AllTerms, out var branch)
            .Should().BeTrue();
        branch.Should().Be(VoucherRenewalBranch.Replace);
    }

    [Fact]
    public void TryResolveBranch_LongExpired_IsReplace()
    {
        var expiry = Today.AddDays(-120);
        VoucherRenewalEligibility.TryResolveBranch(VoucherStatus.Expired, expiry, expiry.AddYears(1), Today, Threshold, AllTerms, out var branch)
            .Should().BeTrue();
        branch.Should().Be(VoucherRenewalBranch.Replace);
    }

    [Theory]
    [InlineData(VoucherStatus.Used)]
    [InlineData(VoucherStatus.Blocked)]
    [InlineData(VoucherStatus.Deactivated)]
    public void TryResolveBranch_NonRenewableStatus_IsNeverEligible(VoucherStatus status)
    {
        // Even squarely inside the window, a spent/dead voucher can't be renewed.
        VoucherRenewalEligibility.TryResolveBranch(status, Today.AddDays(3), Today.AddYears(1), Today, Threshold, AllTerms, out _)
            .Should().BeFalse();
    }

    [Fact]
    public void NewExpirationForExtend_ShouldAddTermToOldExpiry_NotToday()
    {
        // Leftover days are kept: extend from the OLD expiry, not from today.
        var currentExpiry = Today.AddDays(10); // 2026-06-11
        var result = VoucherRenewalEligibility.NewExpirationForExtend(currentExpiry, VoucherRenewalTerm.OneMonth);
        result.Should().Be(new DateOnly(2026, 7, 11));
        result.Should().NotBe(VoucherRenewalTerm.OneMonth.ApplyTo(Today));
    }

    // ── The promise the replace branch owes ───────────────────────────────────────────────────────
    // Not `today + term`. A customer paying for 2 months while their voucher is already good until
    // 27.03 must be good until 27.05 — their leftover days are worth nothing if we hand them a fresh
    // voucher starting from now. That single value is also the floor a replacement voucher must reach,
    // which is why "smallest qualifying voucher" and "never clamp" both fall out of it.

    [Fact]
    public void PromisedExpirationForReplace_AddsTheTermOnTopOfLeftoverDays()
    {
        // Customer is good until 14 days out and pays for 3 months → 3 months from THEIR date, not from today.
        VoucherRenewalEligibility.PromisedExpirationForReplace(
                Today, Today.AddDays(14), VoucherRenewalTerm.ThreeMonths)
            .Should().Be(new DateOnly(2026, 9, 15));
    }

    [Fact]
    public void PromisedExpirationForReplace_IgnoresLeftoverDaysOnceTheyHaveLapsed()
    {
        // Renewing an already-expired voucher keeps no leftover to preserve, so it falls back to today + term.
        VoucherRenewalEligibility.PromisedExpirationForReplace(
                Today, Today.AddDays(-30), VoucherRenewalTerm.ThreeMonths)
            .Should().Be(new DateOnly(2026, 9, 1));
    }

    [Fact]
    public void PromisedExpirationForReplace_WithNoLeftoverDays_IsTodayPlusTerm()
    {
        // The shape the buy-flow ladder relies on, which is why a purchase passes today as the prior date.
        VoucherRenewalEligibility.PromisedExpirationForReplace(
                Today, Today, VoucherRenewalTerm.ThreeMonths)
            .Should().Be(new DateOnly(2026, 9, 1));
    }

    // ── The provider-term ceiling ──────────────────────────────────────────────────────────────
    // We sell a shorter term than we bought, and the customer's extension may never reach past the
    // supplier voucher's real term. Before this rule the extend branch wrote `old + term` with no
    // ceiling, so a 1-month purchase on a 15-month voucher produced a date the supplier never
    // granted (#162). These are the guard rails for that.

    [Fact]
    public void CanExtend_AllowsTermThatStillFitsUnderTheProviderTerm()
    {
        // Customer holds 1 week of a 3-month voucher; a 1-month extension still fits.
        VoucherRenewalEligibility.CanExtend(
                Today.AddDays(7), Today.AddMonths(3), VoucherRenewalTerm.OneMonth)
            .Should().BeTrue();
    }

    [Fact]
    public void CanExtend_RefusesTermThatWouldReachPastTheProviderTerm()
    {
        // Customer holds 3 months of a 3-month voucher: nothing more can be promised.
        VoucherRenewalEligibility.CanExtend(
                Today.AddMonths(3), Today.AddMonths(3), VoucherRenewalTerm.OneWeek)
            .Should().BeFalse();
    }

    [Fact]
    public void CanExtend_ExactlyReachingTheProviderTerm_IsAllowed()
    {
        // Landing precisely on the ceiling is fine - the customer gets everything the supplier has.
        VoucherRenewalEligibility.CanExtend(
                Today.AddMonths(2), Today.AddMonths(3), VoucherRenewalTerm.OneMonth)
            .Should().BeTrue();
    }

    [Fact]
    public void CanExtend_OneDayPastTheProviderTerm_IsRefused()
    {
        VoucherRenewalEligibility.CanExtend(
                Today.AddDays(7), Today.AddMonths(1).AddDays(6), VoucherRenewalTerm.OneMonth)
            .Should().BeFalse();
    }

    [Fact]
    public void TryResolveBranch_StillValidButProviderTermHasNoRoom_IsReplace()
    {
        // The regression this rule exists for: a live customer voucher whose supplier term cannot
        // absorb even the shortest tier. Extend would have to invent validity, so it must swap.
        var customerExpiry = Today.AddDays(5);
        var providerExpiry = Today.AddDays(6); // one day of real life left - 1 week does not fit

        VoucherRenewalEligibility.TryResolveBranch(
                VoucherStatus.Assigned, customerExpiry, providerExpiry, Today, Threshold, AllTerms, out var branch)
            .Should().BeTrue();

        branch.Should().Be(VoucherRenewalBranch.Replace);
    }

    [Fact]
    public void TryResolveBranch_StillValidWithRoomUnderProviderTerm_IsExtend()
    {
        var customerExpiry = Today.AddDays(5);
        var providerExpiry = Today.AddMonths(3);

        VoucherRenewalEligibility.TryResolveBranch(
                VoucherStatus.Assigned, customerExpiry, providerExpiry, Today, Threshold, AllTerms, out var branch)
            .Should().BeTrue();

        branch.Should().Be(VoucherRenewalBranch.Extend);
    }

    [Fact]
    public void TryResolveBranch_NoOfferedTermsMeansNothingCanBeExtended()
    {
        // Every tier switched off in the admin: the ladder is empty, so no extension is possible even
        // though the date would allow one. Must not silently resolve to Extend.
        var customerExpiry = Today.AddDays(3);
        var providerExpiry = Today.AddYears(1);

        VoucherRenewalEligibility.TryResolveBranch(
                VoucherStatus.Assigned, customerExpiry, providerExpiry, Today, Threshold,
                Array.Empty<VoucherRenewalTerm>(), out var branch)
            .Should().BeTrue();

        branch.Should().Be(VoucherRenewalBranch.Replace);
    }

    [Fact]
    public void TryResolveBranch_LapsedVoucherWithGenerousProviderTerm_IsStillReplace()
    {
        // The ceiling never turns a lapsed voucher back into an extension - there is nothing to add to.
        VoucherRenewalEligibility.TryResolveBranch(
                VoucherStatus.Expired, Today.AddDays(-10), Today.AddYears(1), Today, Threshold, AllTerms,
                out var branch)
            .Should().BeTrue();

        branch.Should().Be(VoucherRenewalBranch.Replace);
    }
}
