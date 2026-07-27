using FuelFlow.Features.Stations.SharedModels;
using FuelFlow.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Stations.GetPriceChangeHistory;

public sealed class GetPriceChangeHistoryQueryHandler
{
    private readonly ApplicationDbContext _context;
    public GetPriceChangeHistoryQueryHandler(ApplicationDbContext context) => _context = context;

    public async Task<List<PriceChangeAuditDto>> HandleAsync(GetPriceChangeHistoryQuery query, CancellationToken ct = default)
    {
        return await _context.PriceChangeAudits
            .OrderByDescending(a => a.ChangedAtUtc)
            .Take(200)
            .Select(a => new PriceChangeAuditDto
            {
                Id = a.Id,
                FuelTypeId = a.FuelTypeId,
                OldBasePrice = a.OldBasePrice,
                NewBasePrice = a.NewBasePrice,
                OldDiscountPrice = a.OldDiscountPrice,
                NewDiscountPrice = a.NewDiscountPrice,
                ChangedByUserId = a.ChangedByUserId,
                ChangedAtUtc = a.ChangedAtUtc
            })
            .ToListAsync(ct);
    }
}

public sealed class PriceChangeAuditDto
{
    public Guid Id { get; set; }
    public string FuelTypeId { get; set; } = null!;
    public int OldBasePrice { get; set; }
    public int NewBasePrice { get; set; }
    public int OldDiscountPrice { get; set; }
    public int NewDiscountPrice { get; set; }
    public Guid ChangedByUserId { get; set; }
    public DateTime ChangedAtUtc { get; set; }
}
