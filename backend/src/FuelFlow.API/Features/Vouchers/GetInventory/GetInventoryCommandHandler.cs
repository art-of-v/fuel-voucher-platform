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
        // The GROUP BY happens in the database. This used to load EVERY voucher in the system and
        // group it in memory, which is the wrong question asked of a table that only ever grows:
        // the answer is one row per (brand, fuel, litres), and the database can produce exactly
        // that without shipping every voucher through the process to count it there.
        var grouped = await _context.FuelVouchers
            .IgnoreQueryFilters()
            .AsNoTracking()
            .GroupBy(v => new { v.Brand, v.FuelTypeId, v.Liters })
            .Select(g => new InventoryGroup(
                g.Key.Brand,
                g.Key.FuelTypeId,
                g.Key.Liters,
                g.Count(v => v.Status == VoucherStatus.Available),
                g.Count(v => v.Status == VoucherStatus.Assigned),
                g.Count(v => v.Status == VoucherStatus.Used),
                g.Count(v => v.Status == VoucherStatus.Expired),
                g.Count()))
            .ToListAsync(cancellationToken);

        // The fuel type's NAME is a lookup, not a grouping key: it is one row per fuel type, and
        // the group key already carries the id. A voucher whose fuel type row is gone falls back to
        // the id, the way it did when the name came from the navigation.
        var fuelTypeIds = grouped.Select(g => g.FuelTypeId).Distinct().ToList();
        var names = await _context.FuelTypes
            .AsNoTracking()
            .Where(f => fuelTypeIds.Contains(f.Id))
            .Select(f => new { f.Id, f.Name })
            .ToDictionaryAsync(x => x.Id, x => x.Name, cancellationToken);

        var inventory = grouped
            .Select(g => new InventoryItemDto(
                g.Brand,
                g.FuelTypeId,
                names.TryGetValue(g.FuelTypeId, out var name) ? name : g.FuelTypeId.ToString(),
                g.Liters,
                g.Available,
                g.Assigned,
                g.Used,
                g.Expired,
                g.Total))
            .OrderBy(i => i.Brand)
            .ThenBy(i => i.FuelTypeName)
            .ThenBy(i => i.Liters)
            .ToList();

        return new GetInventoryResponse(inventory);
    }

    /// <summary>One grouped row, projected straight out of the database.</summary>
    private sealed record InventoryGroup(
        string Brand,
        Guid FuelTypeId,
        decimal Liters,
        int Available,
        int Assigned,
        int Used,
        int Expired,
        int Total);
}
