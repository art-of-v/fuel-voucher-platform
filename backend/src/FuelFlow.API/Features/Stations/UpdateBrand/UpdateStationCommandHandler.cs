using FuelFlow.SharedKernel.Domain;
using FuelFlow.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Brands.UpdateBrand;

public sealed class UpdateBrandCommandHandler
{
    private readonly ApplicationDbContext _context;

    public UpdateBrandCommandHandler(ApplicationDbContext context) => _context = context;

    public async Task<bool> HandleAsync(UpdateBrandCommand command, CancellationToken ct = default)
    {
        var entity = await _context.Brands.FirstOrDefaultAsync(x => x.Id == command.Id, ct);
        if (entity is null) return false;

        entity.Name = command.Updated.Name;
        entity.Color = command.Updated.Color;
        entity.LogoText = command.Updated.LogoText;
        _context.Brands.Update(entity);

        await _context.SaveChangesAsync(ct);
        return true;
    }
}
