using FuelFlow.Features.Orders.SharedModels;
using FuelFlow.Payments.SharedModels;
using FuelFlow.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Payments.Admin.GetUnallocatedPayments;

/// <summary>
/// Money with no order, newest first, and the open orders of the customers on it.
/// </summary>
/// <remarks>
/// <para>
/// Unallocated payments are invisible to everything else in the system: no order, so no order report,
/// no reconciliation, no lifecycle job. That is precisely why they need a screen — a table nobody
/// opens is a table nobody notices, and "we did not know whose money this was" is a thing said
/// months later.
/// </para>
/// <para>
/// The list is deliberately not filtered by customer: "money belonging to nobody" is the hardest case
/// and the one an operator arrives to solve.
/// </para>
/// <para>
/// Nor is it filtered by what is left of a transfer. A declared payment that has already paid for one
/// order is still money the company holds and still a row an operator has to be able to find — the
/// message finance typed is attached to it, and hiding the row would hide the evidence with it. So it
/// is listed whole, carrying <c>FreeUah</c> beside the amount that arrived. The two numbers part
/// company as soon as a balance is spent across orders, and an operator who read only the first would
/// place money that is already committed.
/// </para>
/// </remarks>
public sealed class GetUnallocatedPaymentsQueryHandler
{
    private readonly ApplicationDbContext _context;

    public GetUnallocatedPaymentsQueryHandler(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<UnallocatedPaymentsResponse> HandleAsync(
        GetUnallocatedPaymentsQuery query,
        CancellationToken cancellationToken = default)
    {
        // Dismissed money is money an operator has already looked at and closed: it is real, it stays
        // booked, and it will never be placed. Leaving it here would make the queue grow by every test
        // capture the platform ever takes, and a queue that is mostly noise stops being read.
        var payments = _context.Payments
            .AsNoTracking()
            .Where(p => p.OrderId == null
                     && p.Status == PaymentStatus.Succeeded
                     && p.DismissedAtUtc == null);

        var total = await payments.CountAsync(cancellationToken);

        var items = await payments
            .OrderByDescending(p => p.PaidAtUtc)
            .ThenByDescending(p => p.Id)
            .Skip(Math.Max(0, query.Page - 1) * Math.Clamp(query.Limit, 1, 200))
            .Take(Math.Clamp(query.Limit, 1, 200))
            .Select(p => new UnallocatedPaymentDto(
                p.Id,
                p.AmountUah,
                // What is left of the transfer, not what arrived: money drawn by earlier orders is
                // committed and the worklist is a list of what can still be placed.
                p.AmountUah - p.Allocations.Sum(a => a.AmountUah),
                p.Currency,
                p.PaidAtUtc,
                p.Provider,
                p.UserId,
                p.User == null ? null : p.User.PhoneNumber,
                p.User != null && !string.IsNullOrEmpty(p.User.FirstName)
                    ? (p.User.FirstName + " " + p.User.LastName).Trim()
                    : null,
                p.SettlementMessages.OrderBy(m => m.MessageId).Select(m => m.Code).FirstOrDefault(),
                p.SettlementMessages.OrderBy(m => m.MessageId).Select(m => m.Text).FirstOrDefault(),
                p.SettlementMessages.OrderBy(m => m.MessageId).Select(m => m.TelegramDisplayName).FirstOrDefault()))
            .ToListAsync(cancellationToken);

        // One extra query for the whole page, not one per row. The alternative is an operator pasting
        // an order id, which is not placing money — it is asking them to trust a UUID.
        var customerIds = items.Select(i => i.UserId).Where(id => id is not null).Select(id => id!.Value).Distinct().ToList();

        var openOrders = customerIds.Count == 0
            ? []
            : await _context.Orders
                .AsNoTracking()
                .Where(o => customerIds.Contains(o.UserId) && o.Status == OrderStatus.PendingPayment)
                .OrderByDescending(o => o.CreatedAtUtc)
                .Select(o => new OpenOrderRefDto(o.Id, o.UserId, o.Price, o.CreatedAtUtc))
                .ToListAsync(cancellationToken);

        return new UnallocatedPaymentsResponse(items, total, openOrders);
    }
}
