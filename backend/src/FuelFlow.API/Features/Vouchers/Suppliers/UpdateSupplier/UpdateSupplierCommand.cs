namespace FuelFlow.Features.Vouchers.Suppliers.UpdateSupplier;

/// <summary>
/// A changed supplier as the admin form sent it, and who sent it. The actor is a plain value here:
/// the controller is the only place that reads a claims principal.
/// </summary>
public sealed record UpdateSupplierCommand(
    Guid Id,
    UpdateSupplierRequest Request,
    Guid? ActorUserId,
    string? ActorUserName);
