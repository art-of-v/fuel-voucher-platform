using System.Text.Json;
using FuelFlow.Features.Brands;
using FuelFlow.SharedKernel.Domain;

namespace FuelFlow.Features.Vouchers.Suppliers;

/// <summary>
/// Writes the audit row for one supplier change. The snapshot is the supplier as it stands after the
/// write, and the actor is a plain value: the handler never sees a claims principal.
/// </summary>
internal static class SupplierAudit
{
    public static Task RecordAsync(
        AuditEventService events,
        string type,
        Supplier supplier,
        Guid? actorUserId,
        string? actorUserName,
        CancellationToken ct)
    {
        // Without a signed-in user there is nobody to attribute the change to, so nothing is recorded.
        if (actorUserId is null) return Task.CompletedTask;

        // old_value/new_value are jsonb, not text: an unserialised string here is a 22P02 at SaveChanges,
        // and it fails in production only - the in-memory brand does not parse jsonb.
        var snapshot = JsonSerializer.Serialize(new
        {
            supplier.Id,
            supplier.Name,
            // Never changes, but recorded anyway: the question an audit asks about a supplier is
            // usually "what did we buy from them and through which arrangement", and the route is the
            // half of that answer that is not in the name.
            supplier.AcquisitionSource,
            supplier.LegalForm,
            supplier.Phone,
            supplier.Email,
            supplier.EdrIpn,
            supplier.Rnkrr,
            supplier.Address,
            supplier.Notes,
            supplier.IsActive
        });

        return events.RecordEventAsync("Supplier", supplier.Id.ToString(), type, null, snapshot,
            actorUserId.Value, actorUserName, $"{type}: {supplier.Name}", "all", ct);
    }
}
