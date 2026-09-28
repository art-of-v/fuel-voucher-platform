using FluentAssertions;
using FuelFlow.Features.Vouchers.Renewal;
using FuelFlow.Features.Vouchers.SharedModels;
using Xunit;

namespace FuelFlow.UnitTests.Vouchers.Renewal;

public sealed class VoucherRenewalEligibilityTests
{
    private static readonly DateOnly Today = new(2026, 6, 1);
    private const int Threshold = 14;

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
        VoucherRenewalEligibility.TryResolveBranch(VoucherStatus.Assigned, expiry, Today, Threshold, out var branch)
            .Should().BeTrue();
        branch.Should().Be(VoucherRenewalBranch.Extend);
    }

    [Fact]
    public void TryResolveBranch_AtWindowBoundary_IsEligibleExtend()
    {
        var expiry = Today.AddDays(Threshold); // exactly today + threshold is inclusive
        VoucherRenewalEligibility.TryResolveBranch(VoucherStatus.Assigned, expiry, Today, Threshold, out var branch)
            .Should().BeTrue();
        branch.Should().Be(VoucherRenewalBranch.Extend);
    }

    [Fact]
    public void TryResolveBranch_ExpiringToday_IsExtend()
    {
        VoucherRenewalEligibility.TryResolveBranch(VoucherStatus.Assigned, Today, Today, Threshold, out var branch)
            .Should().BeTrue();
        branch.Should().Be(VoucherRenewalBranch.Extend);
    }

    [Fact]
    public void TryResolveBranch_BeyondWindow_IsNotEligible()
    {
        var expiry = Today.AddDays(Threshold + 1);
        VoucherRenewalEligibility.TryResolveBranch(VoucherStatus.Assigned, expiry, Today, Threshold, out _)
            .Should().BeFalse();
    }

    [Fact]
    public void TryResolveBranch_LapsedButStillAssigned_IsReplace()
    {
        var expiry = Today.AddDays(-1);
        VoucherRenewalEligibility.TryResolveBranch(VoucherStatus.Assigned, expiry, Today, Threshold, out var branch)
            .Should().BeTrue();
        branch.Should().Be(VoucherRenewalBranch.Replace);
    }

    [Fact]
    public void TryResolveBranch_LongExpired_IsReplace()
    {
        var expiry = Today.AddDays(-120);
        VoucherRenewalEligibility.TryResolveBranch(VoucherStatus.Expired, expiry, Today, Threshold, out var branch)
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
        VoucherRenewalEligibility.TryResolveBranch(status, Today.AddDays(3), Today, Threshold, out _)
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

    [Fact]
    public void MinStockExpirationForReplace_ShouldBeTodayPlusTerm()
    {
        VoucherRenewalEligibility.MinStockExpirationForReplace(Today, VoucherRenewalTerm.ThreeMonths)
            .Should().Be(new DateOnly(2026, 9, 1));
    }
}
