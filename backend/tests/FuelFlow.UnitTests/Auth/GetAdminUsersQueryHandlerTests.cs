using FluentAssertions;
using FuelFlow.Features.Auth.AdminUser.GetAdminUsers;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.UnitTests.Auth;

public sealed class GetAdminUsersQueryHandlerTests : IDisposable
{
    private readonly ApplicationDbContext _context;
    private readonly GetAdminUsersQueryHandler _handler;

    public GetAdminUsersQueryHandlerTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _context = new ApplicationDbContext(options);
        _handler = new GetAdminUsersQueryHandler(_context);
    }

    public void Dispose()
    {
        _context.Database.EnsureDeleted();
        _context.Dispose();
    }

    [Fact]
    public async Task GetAdminUsers_ShouldReturnAllUsers()
    {
        var role = new Role
        {
            Id = Guid.NewGuid(),
            Name = "Admin",
            CreatedAtUtc = DateTime.UtcNow
        };

        var userWithRole = new User
        {
            Id = Guid.NewGuid(),
            PhoneNumber = "+380992222222",
            Email = "admin@test.com",
            FirstName = "Anna",
            LastName = "Admin",
            Birthdate = new DateOnly(1990, 5, 1),
            ProfileImageUrl = "profile.png",
            ReferralCode = "REF1",
            ReferredBy = "REF0",
            BonusBalance = 100,
            IsActive = true,
            IsDeleted = false,
            TokenVersion = 1,
            CreatedAtUtc = new DateTime(2026, 7, 2, 0, 0, 0, DateTimeKind.Utc),
            UpdatedAtUtc = new DateTime(2026, 7, 2, 0, 0, 0, DateTimeKind.Utc),
            RoleId = role.Id,
            Role = role
        };

        var userWithoutRole = new User
        {
            Id = Guid.NewGuid(),
            PhoneNumber = "+380991111111",
            Email = "user@test.com",
            FirstName = "Bob",
            LastName = "User",
            BonusBalance = 0,
            IsActive = true,
            IsDeleted = false,
            TokenVersion = 1,
            CreatedAtUtc = new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc),
            UpdatedAtUtc = new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc)
        };

        _context.Roles.Add(role);
        _context.Users.AddRange(userWithRole, userWithoutRole);
        await _context.SaveChangesAsync();

        var result = await _handler.HandleAsync(new GetAdminUsersQuery(), CancellationToken.None);

        result.Should().HaveCount(2);

        var admin = result.Single(u => u.Id == userWithRole.Id.ToString());
        admin.Phone.Should().Be("+380992222222");
        admin.Email.Should().Be("admin@test.com");
        admin.FirstName.Should().Be("Anna");
        admin.LastName.Should().Be("Admin");
        admin.Birthdate.Should().Be("1990-05-01");
        admin.ProfileImageUrl.Should().Be("profile.png");
        admin.ReferralCode.Should().Be("REF1");
        admin.ReferredBy.Should().Be("REF0");
        admin.BonusBalance.Should().Be(100);
        admin.Role.Should().Be("Admin");
        admin.IsDeleted.Should().BeFalse();
        admin.CreatedAt.Should().Be(userWithRole.CreatedAtUtc.ToString("o"));

        var plainUser = result.Single(u => u.Id == userWithoutRole.Id.ToString());
        plainUser.Role.Should().BeNull();
        plainUser.Birthdate.Should().BeNull();
    }

    [Fact]
    public async Task GetAdminUsers_ShouldReturnEmpty_WhenNoUsers()
    {
        var result = await _handler.HandleAsync(new GetAdminUsersQuery(), CancellationToken.None);

        result.Should().BeEmpty();
    }
}
