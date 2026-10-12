namespace FuelFlow.Features.Vouchers.RestoreVoucher;

public sealed record RestoreVoucherResponse(bool Success, string Message, string? ErrorCode = null);