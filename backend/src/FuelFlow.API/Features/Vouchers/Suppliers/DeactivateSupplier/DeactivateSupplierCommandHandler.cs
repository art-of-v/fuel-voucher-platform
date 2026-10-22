using FuelFlow.Features.Brands;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Vouchers.Suppliers.DeactivateSupplier;

/// <summary>
/// Deactivates a supplier, and the audit row that says it happened.
/// </summary>
/// <remarks>
/// Deactivate rather than delete. Past vouchers and exchange rows point at this supplier, and that
/// audit trail has to keep naming a real counterparty — a hard delete would either be blocked by the
/// FK or silently orphan the history.
/// </remarks>
public sealed class DeactivateSupplierCommandHandler
{
    private readonly ApplicationDbContext _context;
    private readonly AuditEventService _events;

    public DeactivateSupplierCommandHandler(ApplicationDbContext context, AuditEventService events)
    {
        _context = context;
        _events = events;
    }

    public async Task<SupplierWriteResult> HandleAsync(
        DeactivateSupplierCommand command,
        CancellationToken ct = default)
    {
        var supplier = await _context.Suppliers.AsTracking().FirstOrDefaultAsync(s => s.Id == command.Id, ct);
        if (supplier is null) return SupplierWriteResult.NotFound();

        // Already inactive: nothing changed, so nothing is recorded either.
        if (!supplier.IsActive) return SupplierWriteResult.Done();

        supplier.IsActive = false;
        supplier.UpdatedAtUtc = SystemClock.UtcNow;
        await _context.SaveChangesAsync(ct);

        await SupplierAudit.RecordAsync(
            _events, "SupplierDeactivated", supplier, command.ActorUserId, command.ActorUserName, ct);

        return SupplierWriteResult.Done();
    }
}
