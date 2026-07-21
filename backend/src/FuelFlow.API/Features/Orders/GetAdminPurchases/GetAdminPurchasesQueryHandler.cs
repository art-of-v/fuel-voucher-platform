using FuelFlow.Features.Orders.SharedModels;
using FuelFlow.Persistence;
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
        return await _context.Orders
            .AsNoTracking()
            .Include(o => o.Fulfillments)
            .OrderByDescending(o => o.CreatedAtUtc)
            .Select(o => new AdminPurchaseDto
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
                MonobankStatus = o.MonobankStatus != null ? o.MonobankStatus.ToString() : null,
                CreatedAtUtc = o.CreatedAtUtc,
                FulfilledAtUtc = o.FulfilledAtUtc,
                VoucherCount = o.Fulfillments.Count
            })
            .ToListAsync(cancellationToken);
    }
}
