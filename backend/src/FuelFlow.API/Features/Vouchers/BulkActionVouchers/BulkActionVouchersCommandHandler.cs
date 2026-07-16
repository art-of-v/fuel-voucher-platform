using FuelFlow.Features.Vouchers.SharedModels;
using FuelFlow.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Vouchers.BulkActionVouchers;

public sealed class BulkActionVouchersCommandHandler
{
    private readonly ApplicationDbContext _context;

    public BulkActionVouchersCommandHandler(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<BulkActionResult> HandleAsync(
        BulkActionVouchersCommand command,
        CancellationToken cancellationToken = default)
    {
        if (command.Action == "delete_all")
        {
            _context.FuelVouchers.RemoveRange(await _context.FuelVouchers.ToListAsync(cancellationToken));
            await _context.SaveChangesAsync(cancellationToken);
            return new BulkActionResult { Success = true, Count = 0 };
        }

        if (command.Ids is null || command.Ids.Count == 0)
            return new BulkActionResult { Success = false, Error = "No IDs provided" };

        var entities = await _context.FuelVouchers
            .Where(v => command.Ids.Contains(v.Id))
            .ToListAsync(cancellationToken);

        switch (command.Action)
        {
            case "activate":
                foreach (var e in entities) { e.Status = VoucherStatus.Available; e.UpdatedAtUtc = DateTime.UtcNow; }
                break;
            case "expire":
                foreach (var e in entities) { e.Status = VoucherStatus.Expired; e.UpdatedAtUtc = DateTime.UtcNow; }
                break;
            case "assign":
                if (string.IsNullOrWhiteSpace(command.TargetUserId))
                    return new BulkActionResult { Success = false, Error = "Target User ID required" };
                foreach (var e in entities) { e.Status = VoucherStatus.Assigned; e.AssignedToUserId = command.TargetUserId; e.UpdatedAtUtc = DateTime.UtcNow; }
                break;
            case "delete":
                _context.FuelVouchers.RemoveRange(entities);
                break;
            default:
                return new BulkActionResult { Success = false, Error = $"Unknown action: {command.Action}" };
        }

        await _context.SaveChangesAsync(cancellationToken);
        return new BulkActionResult { Success = true, Count = entities.Count };
    }
}

public sealed class BulkActionResult
{
    public bool Success { get; set; }
    public int Count { get; set; }
    public string? Error { get; set; }
}
