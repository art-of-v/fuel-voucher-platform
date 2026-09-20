using FluentAssertions;
using FuelFlow.API.Services;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using FuelFlow.SharedKernel.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace FuelFlow.UnitTests.Services;

public sealed class ProductOwnerBootstrapTests
{
    private const string OwnerPhone = "+380991112233";

    [Fact]
    public async Task ExecuteAsync_ShouldPromoteConfiguredUserToProductOwner()
    {
        await using var context = CreateContext();
        await SeedRolesAsync(context);
        await AddUserAsync(context, OwnerPhone, SeedRoles.UserRoleId);
        var bootstrap = CreateBootstrap(context, OwnerPhone);

        await bootstrap.ExecuteAsync();

        var user = await context.Users.Include(u => u.Role).FirstAsync(u => u.PhoneNumber == OwnerPhone);
        user.Role!.Name.Should().Be(SeedRoles.ProductOwnerName);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldNormalizePhoneWithoutLeadingPlus()
    {
        await using var context = CreateContext();
        await SeedRolesAsync(context);
        await AddUserAsync(context, OwnerPhone, SeedRoles.UserRoleId);
        // Configured without the leading '+'; the bootstrap must normalize before matching.
        var bootstrap = CreateBootstrap(context, OwnerPhone.TrimStart('+'));

        await bootstrap.ExecuteAsync();

        var user = await context.Users.Include(u => u.Role).FirstAsync(u => u.PhoneNumber == OwnerPhone);
        user.RoleId.Should().Be(SeedRoles.ProductOwnerId);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldNoOpWhenPhoneNotConfigured()
    {
        await using var context = CreateContext();
        await SeedRolesAsync(context);
        await AddUserAsync(context, OwnerPhone, SeedRoles.UserRoleId);
        var bootstrap = CreateBootstrap(context, phone: null);

        await bootstrap.ExecuteAsync();

        (await context.Users.FirstAsync(u => u.PhoneNumber == OwnerPhone)).RoleId
            .Should().Be(SeedRoles.UserRoleId, "an unconfigured bootstrap must change nothing");
    }

    [Fact]
    public async Task ExecuteAsync_ShouldPersistPromotion_UnderProductionNoTrackingDefault()
    {
        // Regression (security): prod runs the DbContext with QueryTrackingBehavior.NoTracking
        // (DatabaseSetup). The bootstrap loads the user and mutates RoleId, so it must
        // AsTracking() the query or SaveChanges silently persists nothing — the routine logs
        // "promoted ... to ProductOwner" while the highest-privilege role never lands, and it
        // re-fails on every startup. Separate contexts over one shared store prove the new
        // role reaches the database, not just an in-memory copy.
        var root = new InMemoryDatabaseRoot();
        var dbName = Guid.NewGuid().ToString();

        await using (var seed = CreateNoTrackingContext(dbName, root))
        {
            await SeedRolesAsync(seed);
            await AddUserAsync(seed, OwnerPhone, SeedRoles.UserRoleId);
        }

        await using (var act = CreateNoTrackingContext(dbName, root))
        {
            await CreateBootstrap(act, OwnerPhone).ExecuteAsync();
        }

        await using var verify = CreateNoTrackingContext(dbName, root);
        var persisted = await verify.Users.FirstAsync(u => u.PhoneNumber == OwnerPhone);
        persisted.RoleId.Should().Be(SeedRoles.ProductOwnerId,
            "the ProductOwner promotion must survive a fresh read, not just mutate an in-memory copy");
    }

    private static ApplicationDbContext CreateContext() => new(
        new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static ApplicationDbContext CreateNoTrackingContext(string dbName, InMemoryDatabaseRoot root) => new(
        new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(dbName, root)
            .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking).Options);

    private static ProductOwnerBootstrap CreateBootstrap(ApplicationDbContext context, string? phone) => new(
        context,
        Options.Create(new AuthOptions { BootstrapProductOwnerPhone = phone }),
        NullLogger<ProductOwnerBootstrap>.Instance);

    private static async Task SeedRolesAsync(ApplicationDbContext context)
    {
        context.Roles.AddRange(
            new Role { Id = SeedRoles.ProductOwnerId, Name = SeedRoles.ProductOwnerName, CreatedAtUtc = DateTime.UtcNow },
            new Role { Id = SeedRoles.UserRoleId, Name = SeedRoles.UserName, CreatedAtUtc = DateTime.UtcNow });
        await context.SaveChangesAsync();
    }

    private static async Task AddUserAsync(ApplicationDbContext context, string phone, Guid roleId)
    {
        context.Users.Add(new User
        {
            Id = Guid.NewGuid(),
            PhoneNumber = phone,
            RoleId = roleId,
            IsActive = true,
            TokenVersion = 1,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow,
        });
        await context.SaveChangesAsync();
    }
}
