using FluentAssertions;
using FuelFlow.Features.Auth.DeleteUser;
using FuelFlow.Features.Auth.SharedModels;
using FuelFlow.Features.Brands;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using FuelFlow.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FuelFlow.UnitTests.Auth;

/// <summary>
/// Deleting an account is the most destructive staff action there is, and it was the only one the
/// hierarchy did not reach.
/// </summary>
/// <remarks>
/// The endpoint was gated on the <c>AdminOrOwner</c> policy and nothing inside it looked at who
/// was deleting whom: an Admin could delete another Admin, and a Manager could try to delete
/// anyone. <c>RoleHierarchy.CanDelete</c> already said what the answer should be — the owner for
/// anyone, an Admin for User/Manager accounts — and had **zero callers**.
/// <para>
/// The other half is the last owner. Demoting the last active ProductOwner is refused, banning the
/// last one is refused, and deleting them was the way around both.
/// </para>
/// </remarks>
public sealed class DeleteUserCommandHandlerTests
{
    [Fact]
    public async Task An_Admin_Cannot_Delete_Another_Admin()
    {
        await using var context = CreateContext();
        await SeedRolesAsync(context);
        var target = await AddUserAsync(context, SeedRoles.AdminName);

        var result = await CreateHandler(context).HandleAsync(
            new DeleteUserCommand(target.Id, Guid.NewGuid(), "Actor", SeedRoles.AdminName),
            CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Forbidden.Should().BeTrue("the refusal has to be a refusal, not a 'not found'");
        result.Error.Should().Contain("Admin");

        target.IsDeleted.Should().BeFalse("a refused delete must not have written anything");
    }

    [Fact]
    public async Task An_Admin_Can_Delete_A_User()
    {
        await using var context = CreateContext();
        await SeedRolesAsync(context);
        var target = await AddUserAsync(context, SeedRoles.UserName);

        var result = await CreateHandler(context).HandleAsync(
            new DeleteUserCommand(target.Id, Guid.NewGuid(), "Actor", SeedRoles.AdminName),
            CancellationToken.None);

        result.Success.Should().BeTrue();
        target.IsDeleted.Should().BeTrue();
        target.IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task A_Manager_Cannot_Delete_Anyone()
    {
        await using var context = CreateContext();
        await SeedRolesAsync(context);
        var target = await AddUserAsync(context, SeedRoles.UserName);

        var result = await CreateHandler(context).HandleAsync(
            new DeleteUserCommand(target.Id, Guid.NewGuid(), "Actor", SeedRoles.ManagerName),
            CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Forbidden.Should().BeTrue();
        target.IsDeleted.Should().BeFalse();
    }

    [Fact]
    public async Task The_Last_ProductOwner_Cannot_Be_Deleted()
    {
        await using var context = CreateContext();
        await SeedRolesAsync(context);
        var owner = await AddUserAsync(context, SeedRoles.ProductOwnerName);

        var result = await CreateHandler(context).HandleAsync(
            new DeleteUserCommand(owner.Id, Guid.NewGuid(), "Actor", SeedRoles.ProductOwnerName),
            CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Forbidden.Should().BeTrue();
        result.Error.Should().Contain("last active ProductOwner");
        owner.IsDeleted.Should().BeFalse("the guard must fire before the entity is mutated");
    }

    [Fact]
    public async Task A_ProductOwner_Can_Be_Deleted_When_Another_One_Exists()
    {
        await using var context = CreateContext();
        await SeedRolesAsync(context);
        var owner = await AddUserAsync(context, SeedRoles.ProductOwnerName);
        await AddUserAsync(context, SeedRoles.ProductOwnerName);

        var result = await CreateHandler(context).HandleAsync(
            new DeleteUserCommand(owner.Id, Guid.NewGuid(), "Actor", SeedRoles.ProductOwnerName),
            CancellationToken.None);

        result.Success.Should().BeTrue();
        owner.IsDeleted.Should().BeTrue();
    }

    [Fact]
    public async Task A_User_May_Delete_Their_Own_Account()
    {
        await using var context = CreateContext();
        await SeedRolesAsync(context);
        var target = await AddUserAsync(context, SeedRoles.UserName);

        // Not a staff action: the account is theirs to close, and the hierarchy has no say.
        var result = await CreateHandler(context).HandleAsync(
            new DeleteUserCommand(target.Id, target.Id),
            CancellationToken.None);

        result.Success.Should().BeTrue();
        target.IsDeleted.Should().BeTrue();
    }

    private static ApplicationDbContext CreateContext() => new(
        new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)).Options, TestQrCipher.Instance);

    private static DeleteUserCommandHandler CreateHandler(ApplicationDbContext context) => new(
        context,
        NullLogger<DeleteUserCommandHandler>.Instance,
        new AuditEventService(context));

    private static async Task SeedRolesAsync(ApplicationDbContext context)
    {
        // The levels mirror what production ships: migration 20260917192136 writes
        // ProductOwner 100 / Admin 50 / Manager 10 / User 0, and AddQaRole writes QA 5.
        context.Roles.AddRange(
            new Role { Id = SeedRoles.ProductOwnerId, Name = SeedRoles.ProductOwnerName, Level = 100, CreatedAtUtc = TestClock.UtcNow },
            new Role { Id = SeedRoles.AdminRoleId, Name = SeedRoles.AdminName, Level = 50, CreatedAtUtc = TestClock.UtcNow },
            new Role { Id = SeedRoles.ManagerRoleId, Name = SeedRoles.ManagerName, Level = 10, CreatedAtUtc = TestClock.UtcNow },
            new Role { Id = SeedRoles.QaRoleId, Name = SeedRoles.QaName, Level = 5, CreatedAtUtc = TestClock.UtcNow },
            new Role { Id = SeedRoles.UserRoleId, Name = SeedRoles.UserName, Level = 0, CreatedAtUtc = TestClock.UtcNow });
        await context.SaveChangesAsync();
    }

    private static async Task<User> AddUserAsync(ApplicationDbContext context, string roleName)
    {
        var rid = roleName switch
        {
            SeedRoles.ProductOwnerName => SeedRoles.ProductOwnerId,
            SeedRoles.AdminName => SeedRoles.AdminRoleId,
            SeedRoles.ManagerName => SeedRoles.ManagerRoleId,
            SeedRoles.QaName => SeedRoles.QaRoleId,
            SeedRoles.UserName => SeedRoles.UserRoleId,
            _ => throw new ArgumentOutOfRangeException(nameof(roleName), roleName, "Unmapped role name")
        };

        var user = new User
        {
            Id = Guid.NewGuid(), PhoneNumber = "+38099123456" + Random.Shared.Next(100),
            RoleId = rid, IsActive = true, TokenVersion = 1,
            CreatedAtUtc = TestClock.UtcNow, UpdatedAtUtc = TestClock.UtcNow
        };
        context.Users.Add(user);
        await context.SaveChangesAsync();
        return user;
    }
}
