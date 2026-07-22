using FuelFlow.Features.Orders.SharedModels;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.DTOs;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Orders.GetAdminPurchases;

public sealed class AdminPurchaseDto
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string Provider { get; set; } = null!;
    public string FuelTypeId { get; set; } = null!;
    public decimal Liters { get; set; }
    public int Quantity { get; set; }
    public int Price { get; set; }
    public string Status { get; set; } = null!;
    public string? MonobankInvoiceId { get; set; }
    public string? MonobankStatus { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? FulfilledAtUtc { get; set; }
    public int VoucherCount { get; set; }
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
            .AsNoTracking()
            .Include(o => o.Fulfillments)
            .Include(o => o.LineItems)
            .OrderByDescending(o => o.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        return purchases.Select(o => new AdminPurchaseDto
        {
            Id = o.Id,
            UserId = o.UserId,
            Provider = o.Provider,
            FuelTypeId = o.FuelTypeId,
            Liters = o.Liters,
            Quantity = o.Quantity,
            Price = o.Price,
            Status = o.Status.ToString(),
            MonobankInvoiceId = o.MonobankInvoiceId,
            MonobankStatus = o.MonobankStatus?.ToString(),
            CreatedAtUtc = o.CreatedAtUtc,
            FulfilledAtUtc = o.FulfilledAtUtc,
            VoucherCount = o.Fulfillments.Count,
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
