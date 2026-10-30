using FluentAssertions;
using FuelFlow.Features.Vouchers;
using FuelFlow.Features.Vouchers.SharedModels;
using Xunit;

namespace FuelFlow.UnitTests.Vouchers;

/// <summary>
/// The refusal to archive a paid voucher travels to the admin as a code plus a `gap` token, and the
/// admin owns the words for each token (admin/src/lib/i18n/locales/vouchers.ts). The tokens are
/// pinned here so neither side can grow one the other does not know about: a token the admin has no
/// key for arrives as the English paragraph the whole arrangement exists to remove.
/// </summary>
public sealed class VoucherDeletionGuardGapTests
{
    private static FuelVoucher Voucher(VoucherStatus status) =>
        new() { VoucherNumber = "V-0001", Status = status };

    [Theory]
    [InlineData(VoucherStatus.Assigned, "assigned")]
    [InlineData(VoucherStatus.PendingReconciliation, "pending_reconciliation")]
    [InlineData(VoucherStatus.Used, "used")]
    [InlineData(VoucherStatus.Expired, "expired")]
    [InlineData(VoucherStatus.Blocked, "blocked")]
    [InlineData(VoucherStatus.Deactivated, "deactivated")]
    public void Names_the_status_that_refused_the_voucher(
        VoucherStatus status,
        string expected)
        => VoucherDeletionGuard.Gap(Voucher(status)).Should().Be(expected);

    /// <summary>
    /// <see cref="VoucherDeletionGuard.IsStock"/> refuses on the fields as well as the status, so a
    /// voucher reading Available can still be unarchivable — and "it is part of an order" for one
    /// that is actually with a worker sends the operator to the wrong screen.
    /// </summary>
    [Fact]
    public void Names_the_field_that_refused_a_voucher_whose_status_reads_as_stock()
    {
        var withWorker = Voucher(VoucherStatus.Available);
        withWorker.WorkerUserId = Guid.CreateVersion7();
        withWorker.AssignedToUserId = Guid.CreateVersion7();

        var handedOver = Voucher(VoucherStatus.Available);
        handedOver.AssignedToUserId = Guid.CreateVersion7();

        var keptForHistory = Voucher(VoucherStatus.Available);
        keptForHistory.OrderId = Guid.CreateVersion7();

        VoucherDeletionGuard.Gap(withWorker).Should().Be("worker");
        VoucherDeletionGuard.Gap(handedOver).Should().Be("assigned");
        VoucherDeletionGuard.Gap(keptForHistory).Should().Be("order");
    }

    /// <summary>
    /// The prose beside the token is what a client that has not learnt the code still reads. A token
    /// with no sentence would make <see cref="VoucherDeletionGuard.Reason"/> throw while refusing a
    /// request — a 500 in place of a 400 that was doing its job.
    /// </summary>
    [Theory]
    [InlineData(VoucherStatus.Assigned)]
    [InlineData(VoucherStatus.PendingReconciliation)]
    [InlineData(VoucherStatus.Used)]
    [InlineData(VoucherStatus.Expired)]
    [InlineData(VoucherStatus.Blocked)]
    [InlineData(VoucherStatus.Deactivated)]
    public void Every_token_has_an_English_sentence_to_fall_back_to(VoucherStatus status)
    {
        var reason = VoucherDeletionGuard.Reason(Voucher(status));

        reason.Should().StartWith("Voucher V-0001 cannot be deleted: ");
        reason.Should().Contain("Use unarchive if this was archived by mistake.");
    }
}
