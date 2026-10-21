using FuelFlow.Features.Vouchers;
using FuelFlow.Features.Vouchers.Availability;
using FuelFlow.Features.Vouchers.SharedModels;
using FuelFlow.SharedKernel.Domain;

namespace FuelFlow.UnitTests;

/// <summary>
/// The summary row the database's trigger would have left for a voucher.
/// </summary>
/// <remarks>
/// <para>
/// The stock queries - the storefront's package list and the term ladder's stock coverage - read
/// the summary, not the voucher table, so a test that seeds stock has to seed the summary too.
/// The in-memory provider has no trigger to do it, and the trigger cannot be ported to it: it is
/// SQL, and it exists precisely because the writes that matter most do not go through EF.
/// </para>
/// <para>
/// So the row is written by hand here - and it is derived from the voucher, never from a second
/// set of values, because a hand-written summary that disagrees with the voucher it stands for
/// would make a test pass for the wrong reason.
/// </para>
/// <para>
/// The integration test <c>StockSummaryFollowsTheVouchers</c> is the one that proves these rows
/// are what the trigger would have written - against a real PostgreSQL, where the trigger is the
/// only thing that keeps the two in step.
/// </para>
/// </remarks>
internal static class TestStock
{
    /// <summary>
    /// The summary row for one voucher, in the group the trigger would have put it in.
    /// </summary>
    public static StockSummary SummaryFor(FuelVoucher voucher, int count = 1)
        => new()
        {
            Brand = voucher.Brand,
            FuelTypeId = voucher.FuelTypeId,
            Liters = voucher.Liters,
            Status = voucher.Status,
            DataKind = voucher.DataKind,
            IsDeleted = voucher.IsDeleted,
            SupplierExpirationDate = voucher.SupplierExpirationDate,
            VoucherCount = count
        };
}
