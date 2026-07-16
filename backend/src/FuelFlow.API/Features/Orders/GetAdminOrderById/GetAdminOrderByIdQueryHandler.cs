using FuelFlow.Features.Orders.SharedModels;
using FuelFlow.Persistence;
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
            FulfilledAtUtc = item.FulfilledAtUtc
        };
    }
}

public sealed class OrderDetailDto
{
    public Guid Id { get; set; }
    public string UserId { get; set; } = null!;
    public string ProductType { get; set; } = null!;
    public string Provider { get; set; } = null!;
    public string FuelTypeId { get; set; } = null!;
    public int Liters { get; set; }
    public int Quantity { get; set; }
    public int Price { get; set; }
    public string Status { get; set; } = null!;
    public string? MonobankInvoiceId { get; set; }
    public string? MonobankPaymentUrl { get; set; }
    public string? MonobankStatus { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? FulfilledAtUtc { get; set; }
}
