using FuelFlow.Features.Orders.SharedModels;
using FuelFlow.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Admin.GetRealizedMargin;

public sealed record GetRealizedMarginQuery(DateTime? FromDate = null, DateTime? ToDate = null, int Limit = 50);

/// <summary>
/// What we actually earned on fuel we actually handed over.
/// <para>
/// The guards prevent the systematic loss — a pump price below our cost is refused at checkout. They
/// cannot prevent the incidental one: the price is set from a blended cost across the whole pool,
/// while the voucher handed to the customer is picked by earliest expiry and carries its own cost.
/// Selling a 50 UAH voucher at a 43 UAH blended price loses 70 UAH and no guard can see it coming,
/// because the price was already correctly computed from a correctly blended cost.
/// </para>
/// <para>
/// This is the detective half. It measures the gap after the fact from frozen sale prices and the
/// voucher's own recorded cost, which is the only pairing that cannot be argued with — and it is
/// therefore the thing that actually backs a claim of "we do not lose money", rather than the guards,
/// which only assert that a particular route was not taken.
/// </para>
/// </summary>
public sealed class GetRealizedMarginQueryHandler
{
    // Money captured and not reversed. Mirrors the customer savings report's status set, so the two
    // views can be read against each other: what the customer saved and what we kept are computed
    // over exactly the same orders.
    private static readonly OrderStatus[] MoneyStatuses =
    [
        OrderStatus.Paid,
        OrderStatus.PendingFulfillment,
        OrderStatus.PartiallyFulfilled,
        OrderStatus.Fulfilled,
        OrderStatus.PartiallyRefunded
    ];

    private readonly ApplicationDbContext _context;

    public GetRealizedMarginQueryHandler(ApplicationDbContext context) => _context = context;

    public async Task<RealizedMarginReport> HandleAsync(
        GetRealizedMarginQuery query,
        CancellationToken cancellationToken = default)
    {
        var fromUtc = query.FromDate ?? DateTime.MinValue;
        var toUtc = query.ToDate ?? DateTime.MaxValue;

        // Two grouped queries, and the report is put together from their answers.
        //
        // The revenue of each (order, brand, fuel) group: the frozen line total, which is the
        // amount actually invoiced and never a recomputation.
        var lineGroups = await (
            from o in _context.Orders.AsNoTracking()
            join li in _context.OrderLineItems.AsNoTracking() on o.Id equals li.OrderId
            where MoneyStatuses.Contains(o.Status)
                  && o.Kind != OrderKind.ReceivedFromCompany
                  && o.CreatedAtUtc >= fromUtc
                  && o.CreatedAtUtc <= toUtc
            group li by new { o.Id, o.CreatedAtUtc, li.Brand, li.FuelTypeId } into g
            select new
            {
                g.Key.Id,
                g.Key.CreatedAtUtc,
                g.Key.Brand,
                g.Key.FuelTypeId,
                Revenue = g.Sum(li => li.LineTotal),
                LitersSold = g.Sum(li => li.Liters * li.Quantity)
            })
            .ToListAsync(cancellationToken);

        // And the vouchers that actually went out, per (order, brand, fuel) group: the cost is
        // the voucher's own recorded cost, and the vouchers that carry no cost are counted
        // separately rather than folded in.
        var deliveredGroups = await (
            from o in _context.Orders.AsNoTracking()
            join f in _context.Fulfillments.AsNoTracking() on o.Id equals f.OrderId
            join v in _context.FuelVouchers.AsNoTracking() on f.VoucherId equals v.Id
            where MoneyStatuses.Contains(o.Status)
                  && o.Kind != OrderKind.ReceivedFromCompany
                  && o.CreatedAtUtc >= fromUtc
                  && o.CreatedAtUtc <= toUtc
            group v by new { o.Id, v.Brand, v.FuelTypeId } into g
            select new
            {
                g.Key.Id,
                g.Key.Brand,
                g.Key.FuelTypeId,
                Delivered = g.Count(),
                // A voucher with no cost, or a cost of zero, carries no cost into the figure.
                Cost = g.Where(v => v.CostPerLiter > 0m).Sum(v => v.CostPerLiter * v.Liters),
                UncostedLiters = g.Where(v => v.CostPerLiter == null || v.CostPerLiter <= 0m).Sum(v => v.Liters),
                // Liters-weighted, like the pricing pool: a 10 L voucher's cost and a 50 L
                // voucher's cost must not carry equal weight.
                WeightedLiters = g.Where(v => v.CostPerLiter > 0m).Sum(v => v.Liters)
            })
            .ToListAsync(cancellationToken);

        // The two sets are joined on the order and the fuel, and on the brand - case-insensitively,
        // because a line names the network the way it was written and a voucher the way it was
        // imported.
        var deliveredByGroup = deliveredGroups.ToDictionary(
            g => (g.Id, Brand: g.Brand.ToLowerInvariant(), g.FuelTypeId));

        var lines = new List<RealizedMarginLine>();

        foreach (var group in lineGroups)
        {
            // A group with no voucher handed over earns no margin: it is not a sale yet.
            if (!deliveredByGroup.TryGetValue((group.Id, group.Brand.ToLowerInvariant(), group.FuelTypeId), out var delivered))
            {
                continue;
            }

            var weightedLiters = delivered.WeightedLiters;

            lines.Add(new RealizedMarginLine
            {
                OrderId = group.Id,
                CreatedAtUtc = group.CreatedAtUtc,
                Brand = group.Brand,
                FuelTypeId = group.FuelTypeId,
                LitersSold = group.LitersSold,
                VouchersDelivered = delivered.Delivered,
                RevenueUah = group.Revenue,
                CostUah = delivered.Cost ?? 0m,
                UncostedLiters = delivered.UncostedLiters,
                SupplierCostPerLiter = weightedLiters > 0m ? (delivered.Cost ?? 0m) / weightedLiters : null,
            });
        }

        var report = new RealizedMarginReport
        {
            RevenueUah = lines.Sum(l => l.RevenueUah),
            CostUah = lines.Sum(l => l.CostUah),
            LossMakingLines = lines.Count(l => l.IsLossMaking),
            Lines = lines.Count,
            UncostedLiters = lines.Sum(l => l.UncostedLiters),
            WorstLines = lines
                .Where(l => l.IsLossMaking)
                .OrderBy(l => l.NetUah)
                .Take(Math.Clamp(query.Limit, 1, 200))
                .ToList(),
        };

        return report;
    }
}

public sealed class RealizedMarginReport
{
    /// <summary>What customers were invoiced, summed over every line that reached fulfilment.</summary>
    public decimal RevenueUah { get; init; }

    /// <summary>What those exact vouchers cost us, from each voucher's own recorded cost.</summary>
    public decimal CostUah { get; init; }

    public decimal NetUah => RevenueUah - CostUah;

    /// <summary>Lines that lost money. Non-zero here means the guards did not prevent it.</summary>
    public int LossMakingLines { get; init; }

    public int Lines { get; init; }

    /// <summary>
    /// Litres delivered by vouchers carrying no cost. Excluded from <see cref="CostUah"/>, so the net
    /// figure is optimistic by whatever those cost. Surfaced so a healthy-looking report cannot hide
    /// that part of the stock was never priced.
    /// </summary>
    public decimal UncostedLiters { get; init; }

    /// <summary>Loss-making lines, worst first — the actionable half of the report.</summary>
    public List<RealizedMarginLine> WorstLines { get; init; } = [];
}

public sealed class RealizedMarginLine
{
    public Guid OrderId { get; init; }
    public DateTime CreatedAtUtc { get; init; }
    public string Brand { get; init; } = null!;
    public Guid FuelTypeId { get; init; }
    public decimal LitersSold { get; init; }
    public int VouchersDelivered { get; init; }
    public decimal RevenueUah { get; init; }
    public decimal CostUah { get; init; }
    public decimal UncostedLiters { get; init; }

    /// <summary>What the vouchers actually handed over cost on average — the specific identification.</summary>
    public decimal? SupplierCostPerLiter { get; init; }

    public decimal NetUah => RevenueUah - CostUah;

    /// <summary>
    /// Only a fully costed line can be called a loss. With uncosted litres in the mix the true cost is
    /// unknown and above <see cref="CostUah"/>, so claiming "loss" would overstate and claiming "profit"
    /// would understate; the line is reported without a verdict instead.
    /// </summary>
    public bool IsLossMaking => UncostedLiters <= 0m && NetUah < 0m;
}