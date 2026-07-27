using FuelFlow.Features.Stations.SharedModels;
using FuelFlow.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Stations.UpdateFuelType;

public sealed class UpdateFuelTypeCommandHandler
{
    private readonly ApplicationDbContext _context;
    public UpdateFuelTypeCommandHandler(ApplicationDbContext context) => _context = context;

    public async Task<bool> HandleAsync(UpdateFuelTypeCommand command, CancellationToken ct = default)
    {
        var entity = await _context.FuelTypes.FirstOrDefaultAsync(x => x.Id == command.Id, ct);
        if (entity is null) return false;

        var audit = new PriceChangeAudit
        {
            Id = Guid.NewGuid(),
            FuelTypeId = entity.Id,
            OldBasePrice = entity.BasePrice,
            NewBasePrice = command.Updated.BasePrice,
            OldDiscountPrice = entity.DiscountPrice,
            NewDiscountPrice = command.Updated.DiscountPrice,
            ChangedByUserId = command.ChangedByUserId,
            ChangedAtUtc = DateTime.UtcNow
        };

        entity.Name = command.Updated.Name;
        entity.StationId = command.Updated.StationId;
        entity.BasePrice = command.Updated.BasePrice;
        entity.DiscountPrice = command.Updated.DiscountPrice;

        _context.PriceChangeAudits.Add(audit);
        await _context.SaveChangesAsync(ct);
        return true;
    }
}
