using FluentAssertions;
using FuelFlow.Features.Auth.AdminUser.SetUserRole;
using FuelFlow.Features.Auth.Logout;
using FuelFlow.Features.Brands;
using FuelFlow.Features.Auth.SharedModels;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using FuelFlow.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
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

        var audit = await context.Set<AuditEventOutbox>().SingleAsync();
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

        var audit = await context.Set<AuditEventOutbox>().SingleAsync();
        audit.EventType.Should().Be("RoleChanged");
        audit.NewValue.Should().Contain("\"sessionsRevoked\":false");
    }

    [Fact]
    public async Task HandleAsync_ShouldRefuseToChangeAProductOwnersRole()
    {
        // The ProductOwner is a singleton established only via the env bootstrap and is
        // untouchable: RoleHierarchy.CanAssignRole rejects any change to a PO target outright
        // (a stronger guard than the older "last active ProductOwner" count check), so the
        // handler forbids it before ever reaching the demotion path.
        await using var context = CreateContext();
        await SeedRolesAsync(context);
        var owner = await AddUserAsync(context, "ProductOwner", SeedRoles.ProductOwnerId);
        var (t1, _) = await AddTwoActiveRefreshTokensAsync(context, owner.Id);
        var handler = CreateHandler(context);

        var result = await handler.HandleAsync(
            new SetUserRoleCommand(owner.Id, "Admin", Guid.NewGuid(), "Actor", "ProductOwner"),
            CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Forbidden.Should().BeTrue();
        // Nothing revoked, role unchanged.
        owner.TokenVersion.Should().Be(7);
        t1.IsRevoked.Should().BeFalse();
        (await context.Set<AuditEventOutbox>().CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task HandleAsync_ShouldPersistRoleChange_UnderProductionNoTrackingDefault()
    {
        // Regression: prod runs the DbContext with QueryTrackingBehavior.NoTracking
        // (DatabaseSetup). The handler must AsTracking() its target or the RoleId mutation is
        // silently dropped by SaveChanges — a role change that reports success but never lands.
        // Separate contexts over one shared store prove the new RoleId reaches the database
        // (the tracking-on tests above only assert the mutated in-memory navigation).
        var root = new InMemoryDatabaseRoot();
        var dbName = Guid.NewGuid().ToString();
        Guid targetId;

        await using (var seed = CreateNoTrackingContext(dbName, root))
        {
            await SeedRolesAsync(seed);
            var user = new User
            {
                Id = Guid.NewGuid(), PhoneNumber = "+380991234567", RoleId = SeedRoles.UserRoleId,
                IsActive = true, TokenVersion = 7, CreatedAtUtc = TestClock.UtcNow, UpdatedAtUtc = TestClock.UtcNow
            };
            seed.Users.Add(user);
            await seed.SaveChangesAsync();
            targetId = user.Id;
        }

        await using (var act = CreateNoTrackingContext(dbName, root))
        {
            var handler = CreateHandler(act);
            var result = await handler.HandleAsync(
                new SetUserRoleCommand(targetId, "Admin", Guid.NewGuid(), "Actor", "ProductOwner"),
                CancellationToken.None);
            result.Success.Should().BeTrue();
        }

        await using var verify = CreateNoTrackingContext(dbName, root);
        var persisted = await verify.Users.FirstAsync(u => u.Id == targetId);
        persisted.RoleId.Should().Be(SeedRoles.AdminRoleId, "the role change must survive a fresh read, not just mutate an in-memory copy");
    }

    // The handler now wraps the role change + revoke in a real transaction (atomicity fix). The
    // InMemory brand has no transactions and throws TransactionIgnoredWarning-as-error unless told
    // to ignore it; against real Postgres (see AdminRoleDemotionIntegrationTests) the transaction is
    // genuine. Ignoring the warning here makes BeginTransaction a no-op so these logic tests still run.
    private static ApplicationDbContext CreateContext() => new(
        new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)).Options, TestQrCipher.Instance);

    private static ApplicationDbContext CreateNoTrackingContext(string dbName, InMemoryDatabaseRoot root) => new(
        new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(dbName, root)
            .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking)
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)).Options, TestQrCipher.Instance);

    private static SetUserRoleCommandHandler CreateHandler(ApplicationDbContext context) => new(
        context,
        NullLogger<SetUserRoleCommandHandler>.Instance,
        new AuditEventService(context),
        new LogoutEverywhereCommandHandler(context, NullLogger<LogoutEverywhereCommandHandler>.Instance));

    private static async Task SeedRolesAsync(ApplicationDbContext context)
    {
        // Level is SECURITY-CRITICAL: the handler ranks old vs new role from the DB column
        // to decide whether a demotion happened. Seeding it as 0 would make every theory
        // below vacuously "not a demotion", so these must mirror what production actually
        // ships - migration 20260917192136_AddRoleLevelAndSeedProductOwnerManager writes
        // ProductOwner 100 / Admin 50 / Manager 10 / User 0, and AddQaRole writes QA 5.
        // AdminRoleDemotionIntegrationTests proves the same rule against real seeded rows.
        context.Roles.AddRange(
            new Role { Id = SeedRoles.ProductOwnerId, Name = SeedRoles.ProductOwnerName, Level = 100, CreatedAtUtc = TestClock.UtcNow },
            new Role { Id = SeedRoles.AdminRoleId, Name = SeedRoles.AdminName, Level = 50, CreatedAtUtc = TestClock.UtcNow },
            new Role { Id = SeedRoles.ManagerRoleId, Name = SeedRoles.ManagerName, Level = 10, CreatedAtUtc = TestClock.UtcNow },
            new Role { Id = SeedRoles.QaRoleId, Name = SeedRoles.QaName, Level = 5, CreatedAtUtc = TestClock.UtcNow },
            new Role { Id = SeedRoles.UserRoleId, Name = SeedRoles.UserName, Level = 0, CreatedAtUtc = TestClock.UtcNow });
        await context.SaveChangesAsync();
    }

    private static async Task<User> AddUserAsync(ApplicationDbContext context, string roleName, Guid? roleId = null)
    {
        var rid = roleId ?? RoleIdFor(roleName);
        var user = new User
        {
            Id = Guid.NewGuid(), PhoneNumber = "+380991234567", RoleId = rid,
            IsActive = true, TokenVersion = 7, CreatedAtUtc = TestClock.UtcNow, UpdatedAtUtc = TestClock.UtcNow
        };
        context.Users.Add(user);
        await context.SaveChangesAsync();
        // Attach the navigation so the handler's oldRoleName read matches without a reload.
        user.Role = await context.Roles.FirstAsync(r => r.Id == rid);
        return user;
    }

    // Every role listed explicitly, and anything unrecognised throws. A trailing `_ => UserRoleId`
    // used to make a mistyped or newly added role silently resolve to the User row, so a test
    // written against QA would assert against the wrong role and still pass.
    private static Guid RoleIdFor(string roleName) => roleName switch
    {
        SeedRoles.ProductOwnerName => SeedRoles.ProductOwnerId,
        SeedRoles.AdminName => SeedRoles.AdminRoleId,
        SeedRoles.ManagerName => SeedRoles.ManagerRoleId,
        SeedRoles.QaName => SeedRoles.QaRoleId,
        SeedRoles.UserName => SeedRoles.UserRoleId,
        _ => throw new ArgumentOutOfRangeException(nameof(roleName), roleName, "Unmapped role name"),
    };

    private static async Task<(RefreshToken, RefreshToken)> AddTwoActiveRefreshTokensAsync(
        ApplicationDbContext context, Guid userId)
    {
        var a = new RefreshToken
        {
            Id = Guid.NewGuid(), UserId = userId, FamilyId = Guid.NewGuid(), Token = Guid.NewGuid().ToString("N"),
            ExpiresAtUtc = TestClock.UtcNow.AddDays(7), CreatedAtUtc = TestClock.UtcNow, IsRevoked = false
        };
        var b = new RefreshToken
        {
            Id = Guid.NewGuid(), UserId = userId, FamilyId = Guid.NewGuid(), Token = Guid.NewGuid().ToString("N"),
            ExpiresAtUtc = TestClock.UtcNow.AddDays(7), CreatedAtUtc = TestClock.UtcNow, IsRevoked = false
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
            CreatedAt = TestClock.UtcNow, LastSeenAt = TestClock.UtcNow
        };
        context.Devices.Add(device);
        await context.SaveChangesAsync();
        return device;
    }
}
