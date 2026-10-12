namespace FuelFlow.Features.Vouchers.UnarchiveVoucher;

public sealed record UnarchiveVoucherCommand(
    Guid VoucherId,
    Guid? ActingAdminUserId = null,
    string? ActingAdminName = null);