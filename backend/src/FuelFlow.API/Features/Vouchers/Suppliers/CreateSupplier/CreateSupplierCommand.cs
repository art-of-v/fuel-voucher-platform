namespace FuelFlow.Features.Vouchers.Suppliers.CreateSupplier;

/// <summary>
/// A new supplier as the admin form sent it, and who sent it. The actor is a plain value here: the
/// controller is the only place that reads a claims principal.
/// </summary>
public sealed record CreateSupplierCommand(
    CreateSupplierRequest Request,
    Guid? ActorUserId,
    string? ActorUserName);
