namespace FuelFlow.Features.Vouchers.UpdateVoucher;

public sealed record UpdateVoucherCommand(
    Guid Id,
    string? Status,
    Guid? AssignedToUserId,
    Guid? ActingAdminUserId = null,
    string? ActingAdminName = null);
