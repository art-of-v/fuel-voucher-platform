using FuelFlow.Persistence;
using FuelFlow.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.API.Features.Orders.SharedServices.Monobank;

/// <summary>
/// Turns order lines into the receipt lines Monobank registers with the tax authority.
///
/// Three checkout paths and the refund path all need this, and they must not disagree: a receipt
/// whose goods lines do not match the order is worse than one with no goods lines, because it is
/// wrong in a way an inspector will catch.
/// </summary>
public static class MonobankBasketBuilder
{
    /// <summary>
    /// The fuel name for a set of fuel types, keyed by id. Missing ids are simply absent, and the
    /// caller falls back to the brand - a receipt with a brand name is better than a receipt
    /// that declares nothing.
    /// </summary>
    public static async Task<Dictionary<Guid, string>> FuelNamesAsync(
        ApplicationDbContext context,
        IEnumerable<Guid> fuelTypeIds,
        CancellationToken cancellationToken = default)
    {
        var ids = fuelTypeIds.Distinct().ToList();
        if (ids.Count == 0) return new Dictionary<Guid, string>();

        return await context.FuelTypes
            .AsNoTracking()
            .Where(f => ids.Contains(f.Id))
            .Select(f => new { f.Id, f.Name })
            .ToDictionaryAsync(x => x.Id, x => x.Name, cancellationToken);
    }

    /// <summary>
    /// One receipt line per order line.
    /// <para>
    /// <c>Qty</c> is litres, not units: a 10 L package ordered once is 10 litres, and two 1 L
    /// packages are 2 litres. <c>Sum</c> is the price of ONE litre, because that is the unit the
    /// customer buys in and the unit the receipt has to be readable in. Total is the line total.
    /// </para>
    /// <para>
    /// A refund passes lines describing only the unfulfilled part, so a half-returned order yields
    /// half the litres rather than the whole line.
    /// </para>
    /// <para>
    /// <c>FuelName</c> is nullable on purpose: <see cref="FuelNamesAsync"/> simply omits a fuel type
    /// it could not find, and the declared type says so rather than making every caller lie with a
    /// placeholder. Such a line falls back to the brand name, which at least tells an inspector
    /// who supplied the fuel.
    /// </para>
    /// </summary>
    public static List<MonobankBasketItem> Build(
        IEnumerable<(string Brand, Guid FuelTypeId, string? FuelName, decimal Liters, int Quantity, decimal UnitPrice, decimal LineTotal)> lines)
    {
        var items = new List<MonobankBasketItem>();

        foreach (var line in lines)
        {
            if (line.Quantity <= 0 || line.Liters <= 0) continue;

            var fuelName = line.FuelName;

            items.Add(new MonobankBasketItem
            {
                Name = string.IsNullOrWhiteSpace(fuelName)
                    ? line.Brand.ToUpperInvariant()
                    : fuelName,
                Qty = line.Liters * line.Quantity,
                Sum = (long)Math.Round(Money.ToKopecks(line.UnitPrice), MidpointRounding.AwayFromZero),
                Total = (long)Math.Round(Money.ToKopecks(line.LineTotal), MidpointRounding.AwayFromZero),
                Unit = "╨╗",
                Code = line.FuelTypeId.ToString(),
            });
        }

        return items;
    }
}
