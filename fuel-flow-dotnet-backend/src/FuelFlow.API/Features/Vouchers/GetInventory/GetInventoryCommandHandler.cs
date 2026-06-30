using FuelFlow.Features.Vouchers.SharedModels;
using FuelFlow.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Vouchers.GetInventory;

public sealed class GetInventoryCommandHandler
{
    private readonly ApplicationDbContext _context;

    public GetInventoryCommandHandler(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<GetInventoryResponse> HandleAsync(
        GetInventoryCommand command,
        CancellationToken cancellationToken = default)
    {
        var vouchers = await _context.FuelVouchers
            .Include(v => v.FuelType)
            .ToListAsync(cancellationToken);


        var inventory = vouchers
            .GroupBy(v => new { v.Provider, v.FuelTypeId, FuelTypeName = v.FuelType?.Name ?? v.FuelTypeId, v.Liters })
            .Select(g => new InventoryItemDto(
                g.Key.Provider,
                g.Key.FuelTypeId,
                g.Key.FuelTypeName,
                g.Key.Liters,
                g.Count(v => v.Status == VoucherStatus.Available),
                g.Count(v => v.Status == VoucherStatus.Assigned),
                g.Count(v => v.Status == VoucherStatus.Used),
                g.Count(v => v.Status == VoucherStatus.Expired),
                g.Count()
            ))
            .OrderBy(i => i.Provider)
            .ThenBy(i => i.FuelTypeName)
            .ThenBy(i => i.Liters)
            .ToList();

        return new GetInventoryResponse(inventory);
    }
}
