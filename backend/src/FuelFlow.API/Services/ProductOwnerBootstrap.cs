using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using FuelFlow.SharedKernel.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace FuelFlow.API.Services;

public sealed class ProductOwnerBootstrap
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<ProductOwnerBootstrap> _logger;
    private readonly string? _phoneNumber;

    public ProductOwnerBootstrap(
        ApplicationDbContext context,
        IOptions<AuthOptions> authOptions,
        ILogger<ProductOwnerBootstrap> logger)
    {
        _context = context;
        _logger = logger;
        _phoneNumber = authOptions.Value.BootstrapProductOwnerPhone;
    }

    public async Task ExecuteAsync(CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_phoneNumber))
        {
            _logger.LogInformation("ProductOwner bootstrap not configured (Auth:BootstrapProductOwnerPhone not set)");
            return;
        }

        var normalized = _phoneNumber.Trim();
        if (!normalized.StartsWith('+'))
        {
            normalized = "+" + normalized;
        }

        var user = await _context.Users
            .Include(u => u.Role)
            .FirstOrDefaultAsync(u => u.PhoneNumber == normalized, ct);

        if (user is null)
        {
            _logger.LogWarning("ProductOwner bootstrap: user {PhoneNumber} not found", normalized);
            return;
        }

        if (user.Role?.Name == SeedRoles.ProductOwnerName)
        {
            _logger.LogInformation("ProductOwner bootstrap: user {PhoneNumber} already has ProductOwner role", normalized);
            return;
        }

        var productOwnerRole = await _context.Roles
            .FirstOrDefaultAsync(r => r.Name == SeedRoles.ProductOwnerName, ct);

        if (productOwnerRole is null)
        {
            _logger.LogError("ProductOwner bootstrap: ProductOwner role not found in database");
            return;
        }

        user.RoleId = productOwnerRole.Id;
        user.Role = productOwnerRole;
        user.UpdatedAtUtc = DateTime.UtcNow;

        await _context.SaveChangesAsync(ct);

        _logger.LogInformation("ProductOwner bootstrap: promoted user {PhoneNumber} ({UserId}) to ProductOwner", normalized, user.Id);
    }
}