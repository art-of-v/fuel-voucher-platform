using FluentAssertions;
using FuelFlow.Features.Auth.AdminUser.SetUserBanned;
using FuelFlow.Features.Auth.SharedModels;
using FuelFlow.Features.Providers;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace FuelFlow.UnitTests.Auth;

public sealed class SetUserBannedCommandHandlerTests
{
    [Theory]
    [InlineData("Manager", "User")]   // Managers cannot ban anyone, even Users.
    [InlineData("Manager", "Manager")]
    [InlineData("Admin", "Admin")]    // Admin cannot ban a peer Admin...
    [InlineData("Admin", "ProductOwner")] // ...nor anyone above.
    [InlineData("User", "User")]      // Non-staff cannot ban at all.
    public async Task HandleAsync_ShouldRejectTargetsOutsideHierarchy(string actorRole, string targetRole)
    {
        await using var context = CreateContext();
        var target = await AddUserAsync(context, targetRole);
        var handler = CreateHandler(context);

        var result = await handler.HandleAsync(
            new SetUserBannedCommand(target.Id, true, Guid.NewGuid(), "Actor", actorRole),
            CancellationToken.None);

        result.Forbidden.Should().BeTrue();
        target.IsBanned.Should().BeFalse();
        target.TokenVersion.Should().Be(7);
        (await context.Set<ProviderEventOutbox>().CountAsync()).Should().Be(0);
    }

    [Theory]
    [InlineData("ProductOwner", "Admin")]
    [InlineData("ProductOwner", "Manager")]
    [InlineData("ProductOwner", "User")]
    [InlineData("Admin", "Manager")]
    [InlineData("Admin", "User")]
    public async Task HandleAsync_Ban_ShouldRevokeAllSessionsAndAudit(string actorRole, string targetRole)
    {
        await using var context = CreateContext();
        var target = await AddUserAsync(context, targetRole);
        var (activeToken, otherToken) = await AddTwoActiveRefreshTokensAsync(context, target.Id);
        var device = await AddActiveDeviceAsync(context, target.Id);
        var handler = CreateHandler(context);
        var actingId = Guid.NewGuid();

        var result = await handler.HandleAsync(
            new SetUserBannedCommand(target.Id, true, actingId, "Actor", actorRole),
            CancellationToken.None);

        result.Success.Should().BeTrue();
        target.IsBanned.Should().BeTrue();
        // Ban is a hard revocation: bump the signed version and kill every live credential.
        target.TokenVersion.Should().Be(8);
        (await context.RefreshTokens.Where(rt => rt.UserId == target.Id).ToListAsync())
            .Should().OnlyContain(rt => rt.IsRevoked && rt.RevokedAtUtc != null);
        (await context.Devices.FirstAsync(d => d.Id == device.Id)).Status
            .Should().Be(DeviceStatus.Revoked);

        var audit = await context.Set<ProviderEventOutbox>().SingleAsync();
        audit.EventType.Should().Be("UserBanned");
        audit.ChangedByUserId.Should().Be(actingId);

        activeToken.IsRevoked.Should().BeTrue();
        otherToken.IsRevoked.Should().BeTrue();
    }

    [Fact]
    public async Task HandleAsync_Unban_ShouldLiftFlagButNotRestoreSessions()
    {
        await using var context = CreateContext();
        var target = await AddUserAsync(context, "User");
        target.IsBanned = true;
        target.TokenVersion = 9; // as left by the earlier ban
        // A ban revoked these; they must stay revoked after an unban.
        var (t1, t2) = await AddTwoActiveRefreshTokensAsync(context, target.Id, revoked: true);
        await context.SaveChangesAsync();
        var handler = CreateHandler(context);

        var result = await handler.HandleAsync(
            new SetUserBannedCommand(target.Id, false, Guid.NewGuid(), "Actor", "Admin"),
            CancellationToken.None);

        result.Success.Should().BeTrue();
        target.IsBanned.Should().BeFalse();
        // Unban restores nothing: version is untouched and tokens stay dead.
        target.TokenVersion.Should().Be(9);
        t1.IsRevoked.Should().BeTrue();
        t2.IsRevoked.Should().BeTrue();

        var audit = await context.Set<ProviderEventOutbox>().SingleAsync();
        audit.EventType.Should().Be("UserUnbanned");
    }

    [Fact]
    public async Task HandleAsync_Ban_IsIdempotent()
    {
        await using var context = CreateContext();
        var target = await AddUserAsync(context, "User");
        var handler = CreateHandler(context);
        var command = new SetUserBannedCommand(target.Id, true, Guid.NewGuid(), "Actor", "Admin");

        (await handler.HandleAsync(command, CancellationToken.None)).Success.Should().BeTrue();
        var versionAfterFirst = target.TokenVersion;

        // A second ban is a no-op: no extra version bump, no second audit row.
        (await handler.HandleAsync(command, CancellationToken.None)).Success.Should().BeTrue();
        target.TokenVersion.Should().Be(versionAfterFirst);
        (await context.Set<ProviderEventOutbox>().CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task HandleAsync_ShouldRefuseToBanYourself()
    {
        await using var context = CreateContext();
        var actor = await AddUserAsync(context, "ProductOwner");
        var handler = CreateHandler(context);

        var result = await handler.HandleAsync(
            new SetUserBannedCommand(actor.Id, true, actor.Id, "Actor", "ProductOwner"),
            CancellationToken.None);

        result.Forbidden.Should().BeTrue();
        actor.IsBanned.Should().BeFalse();
        (await context.Set<ProviderEventOutbox>().CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task HandleAsync_ShouldRefuseToBanTheLastActiveProductOwner()
    {
        await using var context = CreateContext();
        var lonelyOwner = await AddUserAsync(context, "ProductOwner", SeedRoles.ProductOwnerId);
        var handler = CreateHandler(context);

        // A different ProductOwner tries to ban the only other one — would strand the platform.
        var result = await handler.HandleAsync(
            new SetUserBannedCommand(lonelyOwner.Id, true, Guid.NewGuid(), "Actor", "ProductOwner"),
            CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("last active ProductOwner");
        lonelyOwner.IsBanned.Should().BeFalse();
        (await context.Set<ProviderEventOutbox>().CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task HandleAsync_ShouldBanProductOwnerWhenAnotherActiveOwnerExists()
    {
        await using var context = CreateContext();
        var target = await AddUserAsync(context, "ProductOwner", SeedRoles.ProductOwnerId);
        // A second active, non-banned owner keeps the platform manageable.
        await AddUserAsync(context, "ProductOwner", SeedRoles.ProductOwnerId);
        var handler = CreateHandler(context);

        var result = await handler.HandleAsync(
            new SetUserBannedCommand(target.Id, true, Guid.NewGuid(), "Actor", "ProductOwner"),
            CancellationToken.None);

        result.Success.Should().BeTrue();
        target.IsBanned.Should().BeTrue();
    }

    [Fact]
    public async Task HandleAsync_ShouldReturnNotFoundForMissingOrDeletedUser()
    {
        await using var context = CreateContext();
        var deleted = await AddUserAsync(context, "User");
        deleted.IsDeleted = true;
        await context.SaveChangesAsync();
        var handler = CreateHandler(context);

        (await handler.HandleAsync(
            new SetUserBannedCommand(Guid.NewGuid(), true, Guid.NewGuid(), "Actor", "Admin"),
            CancellationToken.None)).NotFound.Should().BeTrue();

        (await handler.HandleAsync(
            new SetUserBannedCommand(deleted.Id, true, Guid.NewGuid(), "Actor", "Admin"),
            CancellationToken.None)).NotFound.Should().BeTrue();
    }

    private static ApplicationDbContext CreateContext() => new(
        new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static SetUserBannedCommandHandler CreateHandler(ApplicationDbContext context) => new(
        context, NullLogger<SetUserBannedCommandHandler>.Instance, new ProviderEventService(context));

    private static async Task<User> AddUserAsync(ApplicationDbContext context, string roleName, Guid? roleId = null)
    {
        var id = roleId ?? Guid.NewGuid();
        // Reuse an already-tracked role of the same id so two users can share one role
        // (needed for the last-ProductOwner scenarios where roleId is the fixed seed GUID).
        var role = await context.Roles.FirstOrDefaultAsync(r => r.Id == id)
                   ?? new Role { Id = id, Name = roleName, CreatedAtUtc = DateTime.UtcNow };
        var user = new User
        {
            Id = Guid.NewGuid(), PhoneNumber = "+380991234567", RoleId = role.Id,
            IsActive = true, TokenVersion = 7, CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow
        };
        if (context.Entry(role).State == EntityState.Detached)
            user.Role = role;
        context.Users.Add(user);
        await context.SaveChangesAsync();
        return user;
    }

    private static async Task<(RefreshToken, RefreshToken)> AddTwoActiveRefreshTokensAsync(
        ApplicationDbContext context, Guid userId, bool revoked = false)
    {
        var a = new RefreshToken
        {
            Id = Guid.NewGuid(), UserId = userId, FamilyId = Guid.NewGuid(), Token = Guid.NewGuid().ToString("N"),
            ExpiresAtUtc = DateTime.UtcNow.AddDays(7), CreatedAtUtc = DateTime.UtcNow,
            IsRevoked = revoked, RevokedAtUtc = revoked ? DateTime.UtcNow : null
        };
        var b = new RefreshToken
        {
            Id = Guid.NewGuid(), UserId = userId, FamilyId = Guid.NewGuid(), Token = Guid.NewGuid().ToString("N"),
            ExpiresAtUtc = DateTime.UtcNow.AddDays(7), CreatedAtUtc = DateTime.UtcNow,
            IsRevoked = revoked, RevokedAtUtc = revoked ? DateTime.UtcNow : null
        };
        context.RefreshTokens.AddRange(a, b);
        await context.SaveChangesAsync();
        return (a, b);
    }

    private static async Task<Device> AddActiveDeviceAsync(ApplicationDbContext context, Guid userId)
    {
        var device = new Device
        {
            Id = Guid.NewGuid(), UserId = userId, DeviceId = "device-" + Guid.NewGuid().ToString("N"),
            PublicKey = "pk", Status = DeviceStatus.Active,
            CreatedAt = DateTime.UtcNow, LastSeenAt = DateTime.UtcNow
        };
        context.Devices.Add(device);
        await context.SaveChangesAsync();
        return device;
    }
}
