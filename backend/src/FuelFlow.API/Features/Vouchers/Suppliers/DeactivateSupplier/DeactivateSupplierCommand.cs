namespace FuelFlow.Features.Vouchers.Suppliers.DeactivateSupplier;

/// <summary>
/// The deactivation of one supplier, and who asked for it. The actor is a plain value here: the
/// controller is the only place that reads a claims principal.
/// </summary>
public sealed record DeactivateSupplierCommand(
    Guid Id,
    Guid? ActorUserId,
    string? ActorUserName);
