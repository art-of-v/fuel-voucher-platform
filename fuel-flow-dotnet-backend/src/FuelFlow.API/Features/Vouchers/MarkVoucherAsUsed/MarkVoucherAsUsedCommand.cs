namespace FuelFlow.Features.Vouchers.MarkVoucherAsUsed;

public sealed record MarkVoucherAsUsedCommand(Guid VoucherId, string UserId);
