using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Brands.UpdateFuelType;

public sealed class UpdateFuelTypeCommandHandler
{
    private readonly ApplicationDbContext _context;
    public UpdateFuelTypeCommandHandler(ApplicationDbContext context) => _context = context;

    public async Task<bool> HandleAsync(UpdateFuelTypeCommand command, CancellationToken ct = default)
    {
        var entity = await _context.FuelTypes.FirstOrDefaultAsync(x => x.Id == command.Id, ct);
        if (entity is null) return false;

        entity.Name = command.Updated.Name;
        entity.BrandId = command.Updated.BrandId;
        entity.BasePrice = command.Updated.BasePrice;
        entity.DiscountPrice = command.Updated.DiscountPrice;
        // Copied rather than ignored: this endpoint takes the whole entity, and a write path that
        // silently dropped a field is how a fuel ends up with a name the catalogue import cannot match.
        entity.BrandFuelCode = FuelTypeEntity.NormaliseBrandFuelCode(command.Updated.BrandFuelCode);
        _context.FuelTypes.Update(entity);

        await _context.SaveChangesAsync(ct);
        return true;
    }
}
