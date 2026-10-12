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
        var from = query.FromDate ?? DateTime.MinValue;
        var to = query.ToDate ?? DateTime.MaxValue;

        var orders = await _context.Orders
            .AsNoTracking()
            .Include(o => o.LineItems)
            .Include(o => o.Fulfillments)
                .ThenInclude(f => f.Voucher)
            .Where(o => MoneyStatuses.Contains(o.Status)
                        && o.Kind != OrderKind.ReceivedFromCompany
                        && o.CreatedAtUtc >= from
                        && o.CreatedAtUtc <= to)
            .ToListAsync(cancellationToken);

        // Revenue is the frozen line total — the amount actually invoiced, never a recomputation.
        // Cost is the voucher that was actually handed over, at the cost that voucher carries.
        var lines = new List<RealizedMarginLine>();

        foreach (var order in orders)
        {
            foreach (var group in order.LineItems.GroupBy(li => (li.Provider, li.FuelTypeId)))
            {
                var revenue = group.Sum(li => li.LineTotal);

                var delivered = order.Fulfillments
                    .Where(f => f.Voucher is not null
                                && string.Equals(f.Voucher.Provider, group.Key.Provider, StringComparison.OrdinalIgnoreCase)
                                && f.Voucher.FuelTypeId == group.Key.FuelTypeId)
                    .Select(f => f.Voucher!)
                    .ToList();

                if (delivered.Count == 0) continue;

                var costed = delivered.Where(v => v.CostPerLiter is { } c && c > 0m).ToList();
                var cost = costed.Sum(v => v.CostPerLiter!.Value * v.Liters);
                var uncostedLiters = delivered.Where(v => v.CostPerLiter is not { } c || c <= 0m).Sum(v => v.Liters);

                // Uncosted vouchers make the figure a partial truth, and a partial truth that looks
                // like profit is the dangerous direction — so it is reported, never silently folded in.
                // Liters-weighted, like the pricing pool: a 10 L voucher's cost and a 50 L voucher's cost
                // must not carry equal weight. Costless vouchers contribute no cost, so they are left
                // out of both sums rather than dragging the average toward zero.
                var weightedLiters = costed.Sum(v => v.Liters);
                lines.Add(new RealizedMarginLine
                {
                    OrderId = order.Id,
                    CreatedAtUtc = order.CreatedAtUtc,
                    Provider = group.Key.Provider,
                    FuelTypeId = group.Key.FuelTypeId,
                    LitersSold = group.Sum(li => li.Liters * li.Quantity),
                    VouchersDelivered = delivered.Count,
                    RevenueUah = revenue,
                    CostUah = cost,
                    UncostedLiters = uncostedLiters,
                    SupplierCostPerLiter = weightedLiters > 0m ? cost / weightedLiters : null,
                });
            }
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
    public string Provider { get; init; } = null!;
    public string FuelTypeId { get; init; } = null!;
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