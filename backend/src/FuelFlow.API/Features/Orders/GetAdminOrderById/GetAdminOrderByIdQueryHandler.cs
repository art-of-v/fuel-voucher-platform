using FuelFlow.Features.Orders.SharedModels;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.DTOs;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Orders.GetAdminOrderById;

public sealed class GetAdminOrderByIdQueryHandler
{
    private readonly ApplicationDbContext _context;

    public GetAdminOrderByIdQueryHandler(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<OrderDetailDto?> HandleAsync(
        GetAdminOrderByIdQuery query,
        CancellationToken cancellationToken = default)
    {
        var item = await _context.Orders
            .AsNoTracking()
            .Include(o => o.Fulfillments)
            .Include(o => o.LineItems)
            .FirstOrDefaultAsync(o => o.Id == query.Id, cancellationToken);

        if (item is null) return null;

        return new OrderDetailDto
        {
            Id = item.Id,
            UserId = item.UserId,
            ProductType = item.ProductType,
            Provider = item.Provider,
            FuelTypeId = item.FuelTypeId,
            Liters = item.Liters,
            Quantity = item.Quantity,
            Price = item.Price,
            Status = item.Status.ToString(),
            MonobankInvoiceId = item.MonobankInvoiceId,
            MonobankPaymentUrl = item.MonobankPaymentUrl,
            MonobankStatus = item.MonobankStatus?.ToString(),
            CreatedAtUtc = item.CreatedAtUtc,
            FulfilledAtUtc = item.FulfilledAtUtc,
            LineItems = item.LineItems.Select(li => new OrderLineItemDto
            {
                Id = li.Id,
                FuelTypeId = li.FuelTypeId,
                Liters = li.Liters,
                Quantity = li.Quantity,
                UnitPrice = li.UnitPrice,
                LineTotal = li.LineTotal
            }).ToList()
        };
    }
}

public sealed class OrderDetailDto
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
