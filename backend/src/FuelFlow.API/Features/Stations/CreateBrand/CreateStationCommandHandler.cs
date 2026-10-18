using FuelFlow.SharedKernel.Domain;
using FuelFlow.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Brands.CreateBrand;

public sealed class CreateBrandCommandHandler
{
    private readonly ApplicationDbContext _context;

    public CreateBrandCommandHandler(ApplicationDbContext context) => _context = context;

    public async Task<CreateBrandResult> HandleAsync(CreateBrandCommand command, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(command.Brand.Slug) ||
            string.IsNullOrWhiteSpace(command.Brand.Name) ||
            string.IsNullOrWhiteSpace(command.Brand.LogoText))
            return new CreateBrandResult { Conflict = false, Error = "Slug, Name and LogoText are required", Success = false };

        var slug = command.Brand.Slug.Trim();

        // Uniqueness is the slug's, not the id's: the slug is what an import file and an operator
        // both mean by "this network", so two rows claiming one slug is the conflict that matters.
        var exists = await _context.Brands.AnyAsync(x => x.Slug == slug, ct);
        if (exists)
            return new CreateBrandResult { Conflict = true, Error = $"Brand with slug '{slug}' already exists" };

        command.Brand.Slug = slug;
        // Assigned here, never taken from the caller: an id the client chose is an id nothing else
        // can vouch for.
        command.Brand.Id = Guid.CreateVersion7();
        command.Brand.CreatedAtUtc = DateTime.UtcNow;
        _context.Brands.Add(command.Brand);
        await _context.SaveChangesAsync(ct);
        return new CreateBrandResult { Success = true, Brand = command.Brand };
    }
}

public sealed class CreateBrandResult
{
    public bool Success { get; set; }
    public bool Conflict { get; set; }
    public string? Error { get; set; }
    public Brand? Brand { get; set; }
}
