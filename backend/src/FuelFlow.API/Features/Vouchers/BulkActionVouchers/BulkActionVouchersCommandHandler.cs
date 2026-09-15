using FuelFlow.API.BackgroundJobs;
using FuelFlow.Features.Orders.SharedModels;
using FuelFlow.Features.Providers;
using FuelFlow.Features.Vouchers.SharedModels;
using FuelFlow.Persistence;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace FuelFlow.Features.Vouchers.BulkActionVouchers;

public sealed class BulkActionVouchersCommandHandler
{
    private readonly ApplicationDbContext _context;
    private readonly IBackgroundJobClient _backgroundJobClient;
    private readonly ProviderEventService _eventService;

    public BulkActionVouchersCommandHandler(
        ApplicationDbContext context,
        IBackgroundJobClient backgroundJobClient,
        ProviderEventService eventService)
    {
        _context = context;
        _backgroundJobClient = backgroundJobClient;
        _eventService = eventService;
    }

    public async Task<BulkActionResult> HandleAsync(
        BulkActionVouchersCommand command,
        CancellationToken cancellationToken = default)
    {
        if (command.Action == "delete_all")
        {
            var all = await _context.FuelVouchers.ToListAsync(cancellationToken);
            var removedCount = all.Count;
            _context.FuelVouchers.RemoveRange(all);
            await _context.SaveChangesAsync(cancellationToken);

            if (command.ActingAdminUserId.HasValue)
            {
                await _eventService.RecordEventAsync(
                    "Voucher",
                    "all",
                    "VoucherBulkAction",
                    null,
                    JsonSerializer.Serialize(new { action = "delete_all", count = removedCount }),
                    command.ActingAdminUserId.Value,
                    command.ActingAdminName,
                    $"Bulk action delete_all removed {removedCount} vouchers",
                    "all",
                    cancellationToken);
            }

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
            case "deactivate":
                foreach (var e in entities) { e.Status = VoucherStatus.Deactivated; e.UpdatedAtUtc = DateTime.UtcNow; }
                break;
            case "assign":
                return new BulkActionResult
                {
                    Success = false,
                    Error = "Manual assign is disabled — vouchers are assigned by fulfilling an order"
                };
            case "delete":
                _context.FuelVouchers.RemoveRange(entities);
                break;
            default:
                return new BulkActionResult { Success = false, Error = $"Unknown action: {command.Action}" };
        }

        if (command.Action != "delete")
        {
            _context.FuelVouchers.UpdateRange(entities);
        }

        await _context.SaveChangesAsync(cancellationToken);

        if (command.ActingAdminUserId.HasValue)
        {
            await _eventService.RecordEventAsync(
                "Voucher",
                "bulk",
                "VoucherBulkAction",
                null,
                JsonSerializer.Serialize(new
                {
                    action = command.Action,
                    count = entities.Count,
                    targetUserId = command.TargetUserId
                }),
                command.ActingAdminUserId.Value,
                command.ActingAdminName,
                $"Bulk action {command.Action} on {entities.Count} vouchers",
                "all",
                cancellationToken);
        }

        if (command.Action == "activate" && entities.Count > 0)
        {
            _context.OutboxEvents.Add(new OutboxEvent
            {
                EventType = OutboxEventType.VoucherActivated,
                Payload = System.Text.Json.JsonSerializer.Serialize(new
                {
                    voucherIds = entities.Select(e => e.Id).ToList(),
                    activatedAt = DateTime.UtcNow
                }),
                Processed = false,
                CreatedAtUtc = DateTime.UtcNow
            });
            await _context.SaveChangesAsync(cancellationToken);

            _backgroundJobClient.Enqueue<FulfillmentService>(
                s => s.ProcessPendingOrdersAsync(CancellationToken.None));
        }

        return new BulkActionResult { Success = true, Count = entities.Count };
    }
}

public sealed class BulkActionResult
{
    public bool Success { get; set; }
    public int Count { get; set; }
    public string? Error { get; set; }
}
