namespace FuelFlow.Features.Vouchers.Suppliers.GetSuppliers;

/// <summary>
/// The supplier list. Inactive suppliers are left out unless the caller asks for them: a
/// deactivated supplier stays on past vouchers for the audit, but it is not one an operator should
/// be offered.
/// </summary>
public sealed record GetSuppliersQuery(bool IncludeInactive);
