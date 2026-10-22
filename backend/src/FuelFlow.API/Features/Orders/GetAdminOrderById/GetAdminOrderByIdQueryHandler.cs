using FuelFlow.Features.Orders.SharedModels;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Database;
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
            .IncludingSoftDeletedAndReleasedFulfilments()
            .AsNoTracking()
            .AsSplitQuery()
            .Include(o => o.Fulfillments)
            .Include(o => o.LineItems)
            .FirstOrDefaultAsync(o => o.Id == query.Id, cancellationToken);

        if (item is null) return null;

        var lineItemsList = item.LineItems.ToList();
        var firstLi = lineItemsList.FirstOrDefault();
        var totalLiters = lineItemsList.Sum(li => li.Liters * li.Quantity);
        var totalQuantity = lineItemsList.Sum(li => li.Quantity);

        return new OrderDetailDto
        {
            Id = item.Id,
            UserId = item.UserId,
            Brand = firstLi?.Brand ?? "",
            FuelTypeId = firstLi?.FuelTypeId ?? Guid.Empty,
            Liters = totalLiters,
            Quantity = totalQuantity,
            Price = item.Price,
            Status = item.Status.ToString(),
            MonobankInvoiceId = item.MonobankInvoiceId,
            MonobankPaymentUrl = item.MonobankPaymentUrl,
            MonobankStatus = item.MonobankStatus?.ToString(),
            CreatedAtUtc = item.CreatedAtUtc,
            FulfilledAtUtc = item.FulfilledAtUtc,
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
    }
}

public sealed class OrderDetailDto
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
