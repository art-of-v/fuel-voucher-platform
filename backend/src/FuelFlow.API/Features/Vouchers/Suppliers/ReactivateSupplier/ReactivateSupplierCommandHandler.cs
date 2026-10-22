using FuelFlow.Features.Brands;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Vouchers.Suppliers.ReactivateSupplier;

/// <summary>
/// Brings a deactivated supplier back, and the audit row that says it happened.
/// </summary>
public sealed class ReactivateSupplierCommandHandler
{
    private readonly ApplicationDbContext _context;
    private readonly AuditEventService _events;

    public ReactivateSupplierCommandHandler(ApplicationDbContext context, AuditEventService events)
    {
        _context = context;
        _events = events;
    }

    public async Task<SupplierWriteResult> HandleAsync(
        ReactivateSupplierCommand command,
        CancellationToken ct = default)
    {
        var supplier = await _context.Suppliers.AsTracking().FirstOrDefaultAsync(s => s.Id == command.Id, ct);
        if (supplier is null) return SupplierWriteResult.NotFound();

        supplier.IsActive = true;
        supplier.UpdatedAtUtc = SystemClock.UtcNow;

        // The audit row is tracked before the save, so this file's own save commits it in the same
        // transaction as the change it describes (planning #533). Without a signed-in user there
        // is nobody to attribute the change to, so nothing is recorded.
        if (command.ActorUserId is { } actorId)
        {
            _events.RecordEvent(
                "Supplier", supplier.Id.ToString(), "SupplierReactivated",
                null, SupplierAudit.Snapshot(supplier),
                actorId, command.ActorUserName,
                SupplierAudit.Summary("SupplierReactivated", supplier),
                "all");
        }

        await _context.SaveChangesAsync(ct);

        return SupplierWriteResult.Done(SupplierDto.From(supplier));
    }
}
