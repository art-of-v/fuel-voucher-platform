namespace FuelFlow.Features.Vouchers.BulkActionVouchers;

public sealed record BulkActionVouchersCommand(
    string Action,
    List<Guid>? Ids,
    Guid? TargetUserId,
    Guid? ActingAdminUserId = null,
    string? ActingAdminName = null);
