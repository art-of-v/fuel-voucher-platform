using FuelFlow.API.Features.Orders.RefundOrder;
using FuelFlow.Features.Orders.SharedModels;
using FuelFlow.Persistence;
using FuelFlow.Payments.SharedModels;
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

    public async Task<List<AdminPurchaseDto>> HandleAsync(
        GetAdminPurchasesQuery query,
        CancellationToken cancellationToken = default)
    {
        var purchases = await _context.Orders
            .IgnoreQueryFilters()
            .AsNoTracking()
            .AsSplitQuery()
            .Include(o => o.Fulfillments)
                .ThenInclude(f => f.Voucher)
            .Include(o => o.LineItems)
            .OrderByDescending(o => o.CreatedAtUtc)
            .ToListAsync(cancellationToken);

// A refund reverses a capture, and a capture always has an order. The filter says so rather than
    // casting it away, so an orderless payment cannot reach a dictionary keyed by order id.
    var refunds = await _context.Payments
    .AsNoTracking()
    .Where(p => p.Kind == PaymentKind.Refund
             && p.OrderId != null
             && purchases.Select(o => o.Id).Contains(p.OrderId.Value))
    .ToListAsync(cancellationToken);

    var refundByOrderId = refunds.ToDictionary(p => p.OrderId!.Value);

        return purchases.Select(o =>
        {
            var lineItemsList = o.LineItems.ToList();
            var firstLi = lineItemsList.FirstOrDefault();
            var totalLiters = lineItemsList.Sum(li => li.Liters * li.Quantity);
            var totalQuantity = lineItemsList.Sum(li => li.Quantity);
            refundByOrderId.TryGetValue(o.Id, out var refund);
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
                RefundableAmountKopecks = RefundOrderCommandHandler.ComputeRefundAmountKopecks(o),
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
