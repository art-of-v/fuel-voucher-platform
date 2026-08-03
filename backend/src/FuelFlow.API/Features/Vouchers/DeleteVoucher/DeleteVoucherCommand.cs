namespace FuelFlow.Features.Vouchers.DeleteVoucher;

public sealed record DeleteVoucherCommand(
    Guid Id,
    Guid? ActingAdminUserId = null,
    string? ActingAdminName = null);
