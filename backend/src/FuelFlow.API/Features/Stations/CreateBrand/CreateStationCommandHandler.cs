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
        if (string.IsNullOrWhiteSpace(command.Brand.Id) ||
            string.IsNullOrWhiteSpace(command.Brand.Name) ||
            string.IsNullOrWhiteSpace(command.Brand.LogoText))
            return new CreateBrandResult { Conflict = false, Error = "Id, Name and LogoText are required", Success = false };

        var exists = await _context.Brands.AnyAsync(x => x.Id == command.Brand.Id, ct);
        if (exists)
            return new CreateBrandResult { Conflict = true, Error = $"Brand with id '{command.Brand.Id}' already exists" };

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
