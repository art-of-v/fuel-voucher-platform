using FuelFlow.API.Features.Orders.RefundOrder;
using FuelFlow.Features.Orders.SharedModels;
using FuelFlow.Payments.SharedModels;
using FuelFlow.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Orders.Receipt.GetOrderReceipt;

/// <summary>
/// Reads the order a receipt is printed from.
/// </summary>
public sealed class GetOrderReceiptQueryHandler
{
    private readonly ApplicationDbContext _context;

    public GetOrderReceiptQueryHandler(ApplicationDbContext context) => _context = context;

    /// <summary>The order, or null when there is none the caller may see.</summary>
    public async Task<OrderReceiptData?> HandleAsync(
        GetOrderReceiptQuery query,
        CancellationToken cancellationToken = default)
    {
        // The whole order is loaded because the refund amount is computed from the line items by
        // RefundOrderCommandHandler, and that handler is the single definition of what is refundable.
        // Recomputing it here would let the two drift - and a receipt that disagrees with the refund
        // button in the admin UI is worse than no receipt.
        //
        // Fulfillments + Voucher are part of that graph, not an extra. ComputeRefundAmountKopecks
        // subtracts what has been delivered from what was paid; without them EF hands back an EMPTY
        // collection, every unit reads as undelivered, and the function reports the entire order
        // value as still refundable.
        var orders = _context.Orders
            .AsNoTracking()
            .Include(o => o.LineItems)
            .Include(o => o.Fulfillments)
                .ThenInclude(f => f.Voucher);

        // The scope is part of the query, not a check after the fetch: another customer's order has
        // to be indistinguishable from one that does not exist.
        var order = query.UserId is { } userId
            ? await orders.FirstOrDefaultAsync(o => o.Id == query.Id && o.UserId == userId, cancellationToken)
            : await orders.FirstOrDefaultAsync(o => o.Id == query.Id, cancellationToken);

        if (order is null)
        {
            return null;
        }

        // Order carries only LegalEntityId, so the company name needs an explicit lookup.
        var companyName = order.LegalEntityId is null
            ? null
            : await _context.LegalEntities
                .Where(le => le.Id == order.LegalEntityId)
                .Select(le => le.Name)
                .FirstOrDefaultAsync(cancellationToken);

        // The line stores the fuel type ID, which for real fuels is a GUID. Printing it makes the
        // receipt's fuel column unreadable, so the names come from the catalog the customer saw.
        var fuelIds = order.LineItems.Select(li => li.FuelTypeId).Distinct().ToList();
        var fuelNames = await _context.FuelTypes
            .Where(ft => fuelIds.Contains(ft.Id))
            .ToDictionaryAsync(ft => ft.Id, ft => ft.Name, cancellationToken);

        // The refund row knows when the refund was actually recorded; UpdatedAtUtc moves on any
        // later edit to the order, which is not when the money went back.
        var refundedAtUtc = await _context.Payments
            .Where(p => p.OrderId == order.Id && p.Kind == PaymentKind.Refund)
            .OrderByDescending(p => p.CreatedAtUtc)
            .Select(p => (DateTime?)p.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken) ?? order.UpdatedAtUtc;

        return new OrderReceiptData
        {
            Id = order.Id,
            CreatedAtUtc = order.CreatedAtUtc,
            UpdatedAtUtc = order.UpdatedAtUtc,
            RefundedAtUtc = refundedAtUtc,
            Price = order.Price,
            Status = order.Status,
            MonobankInvoiceId = order.MonobankInvoiceId,
            MonobankStatus = order.MonobankStatus,
            CompanyName = companyName,
            FuelNames = fuelNames,
            // Computed once, by the one function that defines it: the receipt and the refund
            // button have to agree, and calling it twice to share a condition and a value would
            // let them disagree if it ever stopped being pure.
            RefundableKopecks = RefundOrderCommandHandler.ComputeRefundAmountKopecks(order),
            Lines = order.LineItems
                .Select(li => new OrderReceiptLineData(li.FuelTypeId, li.Liters, li.Quantity, li.UnitPrice, li.LineTotal))
                .ToList()
        };
    }
}
