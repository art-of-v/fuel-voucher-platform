using FuelFlow.Features.Orders.SharedModels;
using FuelFlow.Persistence;
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

    public async Task<List<OrderDto>> HandleAsync(
        GetAdminOrdersQuery query,
        CancellationToken cancellationToken = default)
    {
        var orders = await _context.Orders
            .AsNoTracking()
            .Include(o => o.Fulfillments)
            .Include(o => o.LineItems)
            .OrderByDescending(o => o.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        return orders.Select(o => new OrderDto
        {
            Id = o.Id,
            UserId = o.UserId,
            ProductType = o.ProductType,
            Provider = o.Provider,
            FuelTypeId = o.FuelTypeId,
            Liters = o.Liters,
            Quantity = o.Quantity,
            Price = o.Price,
            Status = o.Status.ToString(),
            MonobankInvoiceId = o.MonobankInvoiceId,
            MonobankPaymentUrl = o.MonobankPaymentUrl,
            MonobankStatus = o.MonobankStatus?.ToString(),
            CreatedAtUtc = o.CreatedAtUtc,
            FulfilledAtUtc = o.FulfilledAtUtc,
            LineItems = o.LineItems.Select(li => new OrderLineItemDto
            {
                Id = li.Id,
                FuelTypeId = li.FuelTypeId,
                Liters = li.Liters,
                Quantity = li.Quantity,
                UnitPrice = li.UnitPrice,
                LineTotal = li.LineTotal
            }).ToList()
        }).ToList();
    }
}

public sealed class OrderDto
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string ProductType { get; set; } = null!;
    public string Provider { get; set; } = null!;
    public string FuelTypeId { get; set; } = null!;
    public decimal Liters { get; set; }
    public int Quantity { get; set; }
    public int Price { get; set; }
    public string Status { get; set; } = null!;
    public string? MonobankInvoiceId { get; set; }
    public string? MonobankPaymentUrl { get; set; }
    public string? MonobankStatus { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? FulfilledAtUtc { get; set; }
    public List<OrderLineItemDto> LineItems { get; set; } = new();
}
