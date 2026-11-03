using FuelFlow.API.Features.Orders.RefundOrder;
using FuelFlow.Features.Orders.SharedModels;
using FuelFlow.Persistence;
using FuelFlow.Payments.SharedModels;
using FuelFlow.SharedKernel.Database;
using FuelFlow.SharedKernel.DTOs;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Orders.GetAdminPurchases;

public sealed class AdminPurchaseDto
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string Brand { get; set; } = null!;
    public Guid FuelTypeId { get; set; }
    public decimal Liters { get; set; }
    public int Quantity { get; set; }
    public decimal Price { get; set; }
    public string Status { get; set; } = null!;
    public string? MonobankInvoiceId { get; set; }
    public string? MonobankStatus { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? FulfilledAtUtc { get; set; }
    public int VoucherCount { get; set; }
    /// <summary>
    /// What the bank has been given back: the order's refund row, or zero when there is none or that
    /// row <see cref="PaymentStatus.Failed"/> - a failed refund is money that never left, and its
    /// retry is answered on the same row rather than a second one.
    /// </summary>
    public decimal RefundedAmountKopecks { get; set; }
    /// <summary>
    /// What is still owed back: the unfulfilled value of the order minus
    /// <see cref="RefundedAmountKopecks"/>, never below zero.
    /// </summary>
    public decimal RefundableAmountKopecks { get; set; }
    public string? RefundStatus { get; set; }
    public string? RefundMonobankStatus { get; set; }
    public List<OrderLineItemDto> LineItems { get; set; } = new();
}

public sealed class GetAdminPurchasesQueryHandler
{
    private readonly ApplicationDbContext _context;

    public GetAdminPurchasesQueryHandler(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<PagedResult<AdminPurchaseDto>> HandleAsync(
        GetAdminPurchasesQuery query,
        CancellationToken cancellationToken = default)
    {
        // The count is taken from the query WITHOUT the includes: a count that joined the
        // fulfillments and the line items would count rows, not orders.
        var baseQuery = _context.Orders
            .IncludingSoftDeletedAndReleasedFulfilments()
            .AsNoTracking();

        var total = await baseQuery.CountAsync(cancellationToken);

        var limit = PageLimits.ClampPageSize(query.Limit);

        // The page is taken from the same query WITH the includes, and the includes are split
        // because both of them are collections: joined, they would multiply each other.
        var purchases = await baseQuery
            .AsSplitQuery()
            .Include(o => o.Fulfillments)
                .ThenInclude(f => f.Voucher)
            .Include(o => o.LineItems)
            .OrderByDescending(o => o.CreatedAtUtc)
            // A stable order needs a tie-break, or two orders created in the same tick can swap
            // places between two pages and one of them is shown twice.
            .ThenBy(o => o.Id)
            .Skip(PageLimits.SkipFor(PageLimits.ClampPage(query.Page), limit))
            .Take(limit)
            .ToListAsync(cancellationToken);

        // A refund reverses a capture, and a capture always has an order. The filter says so rather than
        // casting it away, so an orderless refund cannot reach a dictionary keyed by order id. One row
        // per order is not an assumption: ux_payments_one_refund_per_order enforces it, and a retry of a
        // failed refund reuses that row instead of minting a second one.
        var refunds = await _context.Payments
            .AsNoTracking()
            .Where(p => p.Kind == PaymentKind.Refund
                     && p.OrderId != null
                     && purchases.Select(o => o.Id).Contains(p.OrderId.Value))
            .ToDictionaryAsync(p => p.OrderId!.Value, cancellationToken);

        var items = purchases.Select(o =>
        {
            var lineItemsList = o.LineItems.ToList();
            var firstLi = lineItemsList.FirstOrDefault();
            var totalLiters = lineItemsList.Sum(li => li.Liters * li.Quantity);
            var totalQuantity = lineItemsList.Sum(li => li.Quantity);
            refunds.TryGetValue(o.Id, out var refund);
            // A failed refund is money that never left, so it counts as nothing given back - and the
            // press stays offered, because the retry is answered on that same row.
            var returned = refund is null || refund.Status == PaymentStatus.Failed
                ? 0m
                : refund.AmountKopecks;
            // The unfulfilled value says what the delivery owes; the bank answer says what has already
            // been given back. The panel needs the difference, or a refund it has finished paying keeps
            // offering a press whose only reply is "Completed" (planning #862).
            //
            // Clamped at zero, because the two figures move apart in both directions: a delivery that
            // lands after the bank has returned its value leaves less owed than the row says was given.
            var stillOwed = Math.Max(
                0m, RefundOrderCommandHandler.ComputeRefundAmountKopecks(o) - returned);
            return new AdminPurchaseDto
            {
                Id = o.Id,
                UserId = o.UserId,
                Brand = firstLi?.Brand ?? "",
                FuelTypeId = firstLi?.FuelTypeId ?? Guid.Empty,
                Liters = totalLiters,
                Quantity = totalQuantity,
                Price = o.Price,
                Status = o.Status.ToString(),
                MonobankInvoiceId = o.MonobankInvoiceId,
                MonobankStatus = o.MonobankStatus?.ToString(),
                CreatedAtUtc = o.CreatedAtUtc,
                FulfilledAtUtc = o.FulfilledAtUtc,
                VoucherCount = o.Fulfillments.Count,
                RefundedAmountKopecks = returned,
                RefundableAmountKopecks = stillOwed,
                RefundStatus = refund is null ? null : RefundStatusWord(refund.Status),
                RefundMonobankStatus = refund is null ? null : ProviderWordFor(refund.Status),
                LineItems = lineItemsList.Select(li => new OrderLineItemDto
                {
                    Id = li.Id,
                    Brand = li.Brand,
                    FuelTypeId = li.FuelTypeId,
                    Liters = li.Liters,
                    Quantity = li.Quantity,
                    UnitPrice = li.UnitPrice,
                    LineTotal = li.LineTotal
                }).ToList()
            };
        }).ToList();

        return new PagedResult<AdminPurchaseDto>
        {
            Items = items,
            TotalCount = total,
            Page = PageLimits.ClampPage(query.Page),
            PageSize = limit
        };
    }

    /// <summary>
    /// Our refund status, spelled the way the admin UI already expects.
    /// </summary>
    /// <remarks>
    /// The DTO field has always carried a <c>RefundStatus</c> name, and the translations are keyed on
    /// <c>Processing</c> / <c>Completed</c> / <c>Failed</c>. <see cref="PaymentStatus"/> says the same
    /// three things with different words, so the names are mapped rather than the UI's keys rewritten.
    /// </remarks>
    private static string RefundStatusWord(PaymentStatus status) => status switch
    {
        PaymentStatus.Pending => "Processing",
        PaymentStatus.Succeeded => "Completed",
        // A refund is never collected-then-reversed or expired, so Cancelled and Expired have no
        // refund equivalent. They are still folded into Failed rather than left unmapped: the admin's
        // translation keys are exactly these three words, and returning null for a status the schema
        // permits would render an empty cell on the one screen an operator reads when money is wrong.
        // GetReport and GetReconciliation say the same thing, so the word is consistent across reports.
        _ => "Failed"
    };

    /// <summary>
    /// The acquirer's word for a refund state, reconstructed from ours for display.
    /// </summary>
    /// <remarks>
    /// <c>refunds.monobank_status</c> was free text - the F-15 defect - and did not survive into
    /// <c>payments</c>, which stores a typed status instead. The admin renders it as a secondary line
    /// under the status above, so this inverts <see cref="PaymentFactory.FromProviderStatus"/> to keep
    /// that line working. It is a display reconstruction, not a stored observation: the brand's own
    /// word lives on <c>webhook_events.provider_status</c> and on the payment's payload, where an
    /// audit would read it.
    /// </remarks>
    private static string? ProviderWordFor(PaymentStatus status) => status switch
    {
        PaymentStatus.Pending => "processing",
        PaymentStatus.Succeeded => "success",
        PaymentStatus.Failed => "failure",
        _ => null
    };
}
