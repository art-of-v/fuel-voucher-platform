using FluentAssertions;
using FuelFlow.Features.Auth.AdminUser.SetUserActive;
using FuelFlow.Features.Providers;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging.Abstractions;

namespace FuelFlow.UnitTests.Auth;

public sealed class SetUserActiveCommandHandlerTests
{
    [Theory]
    [InlineData("Manager", "Admin")]
    [InlineData("Manager", "ProductOwner")]
    [InlineData("Manager", "Manager")]
    [InlineData("Admin", "Admin")]
    [InlineData("Admin", "ProductOwner")]
    [InlineData("User", "User")]
    public async Task HandleAsync_ShouldRejectTargetsOutsideHierarchy(string actorRole, string targetRole)
    {
        await using var context = CreateContext();
        var target = await AddUserAsync(context, targetRole);
        var handler = CreateHandler(context);

        var result = await handler.HandleAsync(
            new SetUserActiveCommand(target.Id, false, Guid.NewGuid(), "Actor", actorRole),
            CancellationToken.None);

        result.Forbidden.Should().BeTrue();
        target.IsActive.Should().BeTrue();
        target.TokenVersion.Should().Be(7);
        (await context.Set<ProviderEventOutbox>().CountAsync()).Should().Be(0);
    }

    [Theory]
    [InlineData("Manager", "User", false)]
    [InlineData("Manager", "User", true)]
    [InlineData("Admin", "Manager", false)]
    [InlineData("ProductOwner", "Admin", false)]
    public async Task HandleAsync_ShouldChangeEligibilityWithoutInvalidatingSession(
        string actorRole, string targetRole, bool activate)
    {
        await using var context = CreateContext();
        var target = await AddUserAsync(context, targetRole);
        target.IsActive = !activate;
        await context.SaveChangesAsync();
        var handler = CreateHandler(context);
        var command = new SetUserActiveCommand(target.Id, activate, Guid.NewGuid(), "Actor", actorRole);

        var result = await handler.HandleAsync(command, CancellationToken.None);

        result.Success.Should().BeTrue();
        target.IsActive.Should().Be(activate);
        target.TokenVersion.Should().Be(7);
        var audit = await context.Set<ProviderEventOutbox>().SingleAsync();
        audit.EventType.Should().Be(activate ? "UserActivated" : "UserDeactivated");
        audit.ChangedByUserId.Should().Be(command.ActingUserId);

        // Repeating the same request is idempotent, including the audit trail.
        (await handler.HandleAsync(command, CancellationToken.None)).Success.Should().BeTrue();
        (await context.Set<ProviderEventOutbox>().CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task HandleAsync_ShouldPersistActivation_UnderProductionNoTrackingDefault()
    {
        // Regression: prod runs the DbContext with QueryTrackingBehavior.NoTracking
        // (DatabaseSetup). The handler must AsTracking() its target or the IsActive mutation
        // is silently dropped by SaveChanges — the exact prod failure where /activate returned
        // 204 and logged "activated" while the row stayed inactive. Separate contexts over one
        // shared store reproduce a request that neither pre-tracks the entity nor asserts an
        // in-memory copy (the two things that hide this bug in the tracking-on tests above).
        var root = new InMemoryDatabaseRoot();
        var dbName = Guid.NewGuid().ToString();
        Guid targetId;

        await using (var seed = CreateNoTrackingContext(dbName, root))
        {
            var role = new Role { Id = Guid.NewGuid(), Name = "User", CreatedAtUtc = DateTime.UtcNow };
            var user = new User
            {
                Id = Guid.NewGuid(), PhoneNumber = "+380991234567", RoleId = role.Id,
                IsActive = false, TokenVersion = 7, CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow
            };
            seed.Roles.Add(role);
            seed.Users.Add(user);
            await seed.SaveChangesAsync();
            targetId = user.Id;
        }

        await using (var act = CreateNoTrackingContext(dbName, root))
        {
            var handler = CreateHandler(act);
            var result = await handler.HandleAsync(
                new SetUserActiveCommand(targetId, true, Guid.NewGuid(), "Actor", "Admin"),
                CancellationToken.None);
            result.Success.Should().BeTrue();
        }

        await using var verify = CreateNoTrackingContext(dbName, root);
        var persisted = await verify.Users.FirstAsync(u => u.Id == targetId);
        persisted.IsActive.Should().BeTrue("activation must survive a fresh read, not just mutate an in-memory copy");
    }

    private static ApplicationDbContext CreateContext() => new(
        new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static ApplicationDbContext CreateNoTrackingContext(string dbName, InMemoryDatabaseRoot root) => new(
        new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(dbName, root)
            .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking).Options);

    private static SetUserActiveCommandHandler CreateHandler(ApplicationDbContext context) => new(
        context, NullLogger<SetUserActiveCommandHandler>.Instance, new ProviderEventService(context));

    private static async Task<User> AddUserAsync(ApplicationDbContext context, string roleName)
    {
        var role = new Role { Id = Guid.NewGuid(), Name = roleName, CreatedAtUtc = DateTime.UtcNow };
        var user = new User
        {
            Id = Guid.NewGuid(), PhoneNumber = "+380991234567", Role = role, RoleId = role.Id,
            IsActive = true, TokenVersion = 7, CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow
        };
        context.Users.Add(user);
        await context.SaveChangesAsync();
        return user;
    }
}
