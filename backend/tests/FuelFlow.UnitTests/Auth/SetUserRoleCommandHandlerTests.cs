using FluentAssertions;
using FuelFlow.Features.Auth.AdminUser.SetUserRole;
using FuelFlow.Features.Auth.Logout;
using FuelFlow.Features.Providers;
using FuelFlow.Features.Auth.SharedModels;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace FuelFlow.UnitTests.Auth;

public sealed class SetUserRoleCommandHandlerTests
{
    [Theory]
    [InlineData("Admin", "User")]      // Loses Staff status entirely.
    [InlineData("Manager", "User")]    // Loses Staff status entirely.
    [InlineData("Admin", "Manager")]   // Steps down within Staff.
    public async Task HandleAsync_Demotion_ShouldRevokeAllSessionsAndBumpVersion(string fromRole, string toRole)
    {
        await using var context = CreateContext();
        await SeedRolesAsync(context);
        var target = await AddUserAsync(context, fromRole);
        var (t1, t2) = await AddTwoActiveRefreshTokensAsync(context, target.Id);
        var device = await AddActiveDeviceAsync(context, target.Id);
        var handler = CreateHandler(context);

        var result = await handler.HandleAsync(
            // ProductOwner actor clears the hierarchy check for any target/target-role pair.
            new SetUserRoleCommand(target.Id, toRole, Guid.NewGuid(), "Actor", "ProductOwner"),
            CancellationToken.None);

        result.Success.Should().BeTrue();
        target.Role!.Name.Should().Be(toRole);
        // Demotion is a hard revocation: bump the signed version and kill every live credential.
        target.TokenVersion.Should().Be(8);
        t1.IsRevoked.Should().BeTrue();
        t2.IsRevoked.Should().BeTrue();
        (await context.Devices.FirstAsync(d => d.Id == device.Id)).Status
            .Should().Be(DeviceStatus.Revoked);

        var audit = await context.Set<ProviderEventOutbox>().SingleAsync();
        audit.EventType.Should().Be("RoleChanged");
        audit.NewValue.Should().Contain("\"sessionsRevoked\":true");
    }

    [Theory]
    [InlineData("User", "Manager")]    // Gains Staff status.
    [InlineData("User", "Admin")]
    [InlineData("Manager", "Admin")]   // Steps up within Staff.
    public async Task HandleAsync_Promotion_ShouldNotRevokeExistingSessions(string fromRole, string toRole)
    {
        await using var context = CreateContext();
        await SeedRolesAsync(context);
        var target = await AddUserAsync(context, fromRole);
        var (t1, t2) = await AddTwoActiveRefreshTokensAsync(context, target.Id);
        var device = await AddActiveDeviceAsync(context, target.Id);
        var handler = CreateHandler(context);

        var result = await handler.HandleAsync(
            new SetUserRoleCommand(target.Id, toRole, Guid.NewGuid(), "Actor", "ProductOwner"),
            CancellationToken.None);

        result.Success.Should().BeTrue();
        target.Role!.Name.Should().Be(toRole);
        // Promotion leaves the existing session exactly as it was: no version bump, nothing revoked.
        target.TokenVersion.Should().Be(7);
        t1.IsRevoked.Should().BeFalse();
        t2.IsRevoked.Should().BeFalse();
        (await context.Devices.FirstAsync(d => d.Id == device.Id)).Status
            .Should().Be(DeviceStatus.Active);

        var audit = await context.Set<ProviderEventOutbox>().SingleAsync();
        audit.EventType.Should().Be("RoleChanged");
        audit.NewValue.Should().Contain("\"sessionsRevoked\":false");
    }

    [Fact]
    public async Task HandleAsync_ShouldRefuseToDemoteTheLastActiveProductOwner()
    {
        await using var context = CreateContext();
        await SeedRolesAsync(context);
        var lonelyOwner = await AddUserAsync(context, "ProductOwner", SeedRoles.ProductOwnerId);
        var (t1, _) = await AddTwoActiveRefreshTokensAsync(context, lonelyOwner.Id);
        var handler = CreateHandler(context);

        var result = await handler.HandleAsync(
            new SetUserRoleCommand(lonelyOwner.Id, "Admin", Guid.NewGuid(), "Actor", "ProductOwner"),
            CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("last active ProductOwner");
        // Nothing revoked, role unchanged.
        lonelyOwner.TokenVersion.Should().Be(7);
        t1.IsRevoked.Should().BeFalse();
        (await context.Set<ProviderEventOutbox>().CountAsync()).Should().Be(0);
    }

    private static ApplicationDbContext CreateContext() => new(
        new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static SetUserRoleCommandHandler CreateHandler(ApplicationDbContext context) => new(
        context,
        NullLogger<SetUserRoleCommandHandler>.Instance,
        new ProviderEventService(context),
        new LogoutEverywhereCommandHandler(context, NullLogger<LogoutEverywhereCommandHandler>.Instance));

    private static async Task SeedRolesAsync(ApplicationDbContext context)
    {
        context.Roles.AddRange(
            new Role { Id = SeedRoles.ProductOwnerId, Name = SeedRoles.ProductOwnerName, CreatedAtUtc = DateTime.UtcNow },
            new Role { Id = SeedRoles.AdminRoleId, Name = SeedRoles.AdminName, CreatedAtUtc = DateTime.UtcNow },
            new Role { Id = SeedRoles.ManagerRoleId, Name = SeedRoles.ManagerName, CreatedAtUtc = DateTime.UtcNow },
            new Role { Id = SeedRoles.UserRoleId, Name = SeedRoles.UserName, CreatedAtUtc = DateTime.UtcNow });
        await context.SaveChangesAsync();
    }

    private static async Task<User> AddUserAsync(ApplicationDbContext context, string roleName, Guid? roleId = null)
    {
        var rid = roleId ?? RoleIdFor(roleName);
        var user = new User
        {
            Id = Guid.NewGuid(), PhoneNumber = "+380991234567", RoleId = rid,
            IsActive = true, TokenVersion = 7, CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow
        };
        context.Users.Add(user);
        await context.SaveChangesAsync();
        // Attach the navigation so the handler's oldRoleName read matches without a reload.
        user.Role = await context.Roles.FirstAsync(r => r.Id == rid);
        return user;
    }

    private static Guid RoleIdFor(string roleName) => roleName switch
    {
        "ProductOwner" => SeedRoles.ProductOwnerId,
        "Admin" => SeedRoles.AdminRoleId,
        "Manager" => SeedRoles.ManagerRoleId,
        _ => SeedRoles.UserRoleId,
    };

    private static async Task<(RefreshToken, RefreshToken)> AddTwoActiveRefreshTokensAsync(
        ApplicationDbContext context, Guid userId)
    {
        var a = new RefreshToken
        {
            Id = Guid.NewGuid(), UserId = userId, FamilyId = Guid.NewGuid(), Token = Guid.NewGuid().ToString("N"),
            ExpiresAtUtc = DateTime.UtcNow.AddDays(7), CreatedAtUtc = DateTime.UtcNow, IsRevoked = false
        };
        var b = new RefreshToken
        {
            Id = Guid.NewGuid(), UserId = userId, FamilyId = Guid.NewGuid(), Token = Guid.NewGuid().ToString("N"),
            ExpiresAtUtc = DateTime.UtcNow.AddDays(7), CreatedAtUtc = DateTime.UtcNow, IsRevoked = false
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
