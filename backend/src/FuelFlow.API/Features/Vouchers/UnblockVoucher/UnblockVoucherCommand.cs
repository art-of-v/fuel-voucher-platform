using System.Text.Json;
using FuelFlow.Features.Providers;
using FuelFlow.Features.Vouchers.SharedModels;
using FuelFlow.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Vouchers.UnblockVoucher;

public sealed record UnblockVoucherCommand(
    Guid VoucherId,
    Guid? ActingAdminUserId = null,
    string? ActingAdminName = null);

public sealed class UnblockVoucherCommandHandler
{
    private readonly ApplicationDbContext _context;
    private readonly ProviderEventService _eventService;

    public UnblockVoucherCommandHandler(
        ApplicationDbContext context,
        ProviderEventService eventService)
    {
        _context = context;
        _eventService = eventService;
    }

    public async Task<UnblockVoucherResult?> HandleAsync(
        UnblockVoucherCommand command,
        CancellationToken cancellationToken = default)
    {
        var voucher = await _context.FuelVouchers
            .FirstOrDefaultAsync(v => v.Id == command.VoucherId, cancellationToken);

        if (voucher is null)
        {
            return null;
        }

        if (voucher.Status != VoucherStatus.Blocked)
        {
            return new UnblockVoucherResult
            {
                Success = false,
                Error = $"Only blocked vouchers can be unblocked (current status: {voucher.Status})"
            };
        }

        var oldStatus = voucher.Status;
        var oldWorkerUserId = voucher.WorkerUserId;

        voucher.Status = VoucherStatus.Assigned;
        voucher.WorkerUserId = null;
        voucher.UpdatedAtUtc = DateTime.UtcNow;

        _context.FuelVouchers.Update(voucher);
        await _context.SaveChangesAsync(cancellationToken);

        if (command.ActingAdminUserId.HasValue)
        {
            await _eventService.RecordEventAsync(
                "Voucher",
                voucher.Id.ToString(),
                "VoucherUnblocked",
                JsonSerializer.Serialize(new { status = oldStatus.ToString(), workerUserId = oldWorkerUserId }),
                JsonSerializer.Serialize(new { status = voucher.Status.ToString(), workerUserId = voucher.WorkerUserId }),
                command.ActingAdminUserId.Value,
                command.ActingAdminName,
                $"Unblocked voucher {voucher.VoucherNumber} ({voucher.Provider})",
                voucher.Provider,
                cancellationToken);
        }

        return new UnblockVoucherResult { Success = true };
    }
}

public sealed class UnblockVoucherResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
}
