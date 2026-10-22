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
        var payments = _context.Payments
            .AsNoTracking()
            .Where(p => p.OrderId == null && p.Status == PaymentStatus.Succeeded);

        var total = await payments.CountAsync(cancellationToken);

        var items = await payments
            .OrderByDescending(p => p.PaidAtUtc)
            .ThenByDescending(p => p.Id)
            .Skip(Math.Max(0, query.Page - 1) * Math.Clamp(query.Limit, 1, 200))
            .Take(Math.Clamp(query.Limit, 1, 200))
            .Select(p => new UnallocatedPaymentDto(
                p.Id,
                p.AmountUah,
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
