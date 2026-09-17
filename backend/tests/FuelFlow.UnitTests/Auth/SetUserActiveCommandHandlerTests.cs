using FluentAssertions;
using FuelFlow.Features.Auth.AdminUser.SetUserActive;
using FuelFlow.Features.Providers;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;
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

    private static ApplicationDbContext CreateContext() => new(
        new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

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
