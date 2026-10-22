using System.Text.Json;
using FuelFlow.SharedKernel.Domain;

namespace FuelFlow.Features.Vouchers.Suppliers;

/// <summary>
/// The audit row for one supplier change: what it says, and the values it carries.
/// </summary>
/// <remarks>
/// <para>
/// The row is BUILT here and RECORDED by the caller. The builder is shared because the snapshot is
/// the same for every kind of supplier change, and the recording is left to the handler because the
/// handler is the file that saves - and the row has to be committed by the same save as the change
/// it describes, or not at all (planning #533).
/// </para>
/// </remarks>
internal static class SupplierAudit
{
    /// <summary>
    /// The snapshot: the supplier as it stands after the write.
    /// </summary>
    /// <remarks>
    /// old_value/new_value are jsonb, not text: an unserialised string here is a 22P02 at SaveChanges,
    /// and it fails in production only - the in-memory brand does not parse jsonb.
    /// </remarks>
    public static string Snapshot(Supplier supplier) => JsonSerializer.Serialize(new
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

    /// <summary>The one line the audit screen shows for this change.</summary>
    public static string Summary(string type, Supplier supplier) => $"{type}: {supplier.Name}";
}
