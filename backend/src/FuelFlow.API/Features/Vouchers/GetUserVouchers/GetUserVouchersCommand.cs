using FuelFlow.Persistence;

namespace FuelFlow.Features.Vouchers.GetUserVouchers;

/// <summary>
/// The caller's own wallet.
/// </summary>
/// <param name="UserId">Whose wallet it is.</param>
/// <param name="Paging">
/// Which page of it. The wallet is the customer's most-used screen and it is the one
/// list that carries the REDEEMABLE QR of every voucher in it - the bearer material and
/// a rendered PNG per row - so an unbounded list is an availability problem, not a
/// nicety: one customer with a few hundred vouchers made the API render every one of
/// their QR images in a single request.
/// </param>
public sealed record GetUserVouchersCommand(Guid UserId, PagedRequest Paging);
