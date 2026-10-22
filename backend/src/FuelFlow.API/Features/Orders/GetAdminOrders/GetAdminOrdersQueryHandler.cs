using FuelFlow.Features.Orders.SharedModels;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Database;
using FuelFlow.SharedKernel.DTOs;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Orders.GetAdminOrders;

public sealed class GetAdminOrdersQueryHandler
{
    private readonly ApplicationDbContext _context;

    public GetAdminOrdersQueryHandler(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<PagedResult<OrderDto>> HandleAsync(
        GetAdminOrdersQuery query,
        CancellationToken cancellationToken = default)
    {
        // The count is taken from the query WITHOUT the includes: a count that joined the
        // fulfillments and the line items would count rows, not orders.
        var baseQuery = _context.Orders
            .IncludingSoftDeletedAndReleasedFulfilments()
            .AsNoTracking();

        var total = await baseQuery.CountAsync(cancellationToken);

        // The page is taken from the same query WITH the includes, and the includes are split
        // because both of them are collections: joined, they would multiply each other.
        var orders = await baseQuery
            .AsSplitQuery()
            .Include(o => o.Fulfillments)
            .Include(o => o.LineItems)
            .OrderByDescending(o => o.CreatedAtUtc)
            // A stable order needs a tie-break, or two orders created in the same tick can swap
            // places between two pages and one of them is shown twice.
            .ThenBy(o => o.Id)
            .Skip(PageLimits.SkipFor(PageLimits.ClampPage(query.Page), PageLimits.ClampPageSize(query.Limit)))
            .Take(PageLimits.ClampPageSize(query.Limit))
            .ToListAsync(cancellationToken);

        var items = orders.Select(o =>
        {
            var lineItemsList = o.LineItems.ToList();
            var firstLi = lineItemsList.FirstOrDefault();
            var totalLiters = lineItemsList.Sum(li => li.Liters * li.Quantity);
            var totalQuantity = lineItemsList.Sum(li => li.Quantity);
            return new OrderDto
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
                MonobankPaymentUrl = o.MonobankPaymentUrl,
                MonobankStatus = o.MonobankStatus?.ToString(),
                CreatedAtUtc = o.CreatedAtUtc,
                FulfilledAtUtc = o.FulfilledAtUtc,
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

        return new PagedResult<OrderDto>
        {
            Items = items,
            TotalCount = total,
            Page = PageLimits.ClampPage(query.Page),
            PageSize = PageLimits.ClampPageSize(query.Limit)
        };
    }
}

public sealed class OrderDto
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
    public string? MonobankPaymentUrl { get; set; }
    public string? MonobankStatus { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? FulfilledAtUtc { get; set; }
    public List<OrderLineItemDto> LineItems { get; set; } = new();
}
