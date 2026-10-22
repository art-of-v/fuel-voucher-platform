namespace FuelFlow.Features.Vouchers.Suppliers.ReactivateSupplier;

/// <summary>
/// The reactivation of one supplier, and who asked for it. The actor is a plain value here: the
/// controller is the only place that reads a claims principal.
/// </summary>
public sealed record ReactivateSupplierCommand(
    Guid Id,
    Guid? ActorUserId,
    string? ActorUserName);
