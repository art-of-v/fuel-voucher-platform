using FuelFlow.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Vouchers.Suppliers.GetSuppliers;

/// <summary>
/// Reads the supplier list, ordered by name - the order the operator picks a supplier in.
/// </summary>
public sealed class GetSuppliersQueryHandler
{
    private readonly ApplicationDbContext _context;

    public GetSuppliersQueryHandler(ApplicationDbContext context) => _context = context;

    public async Task<List<SupplierDto>> HandleAsync(
        GetSuppliersQuery query,
        CancellationToken ct = default)
    {
        var rows = _context.Suppliers.AsNoTracking().AsQueryable();
        if (!query.IncludeInactive) rows = rows.Where(s => s.IsActive);

        return await rows
            .OrderBy(s => s.Name)
            .Select(s => new SupplierDto(
                s.Id, s.Name, s.AcquisitionSource, s.LegalForm, s.Phone, s.Email, s.EdrIpn, s.Rnkrr, s.Address, s.Notes, s.IsActive))
            .ToListAsync(ct);
    }
}
