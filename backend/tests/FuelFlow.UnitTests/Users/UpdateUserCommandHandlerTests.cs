using FluentAssertions;
using FuelFlow.Features.Users.UpdateUser;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.UnitTests.Users;

public sealed class UpdateUserCommandHandlerTests : IDisposable
{
    private static readonly Guid UserId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");

    private readonly ApplicationDbContext _context;

    public UpdateUserCommandHandlerTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _context = new ApplicationDbContext(options);
    }

    public void Dispose()
    {
        _context.Database.EnsureDeleted();
        _context.Dispose();
    }

    [Fact]
    public async Task UpdateUser_ShouldUpdateFields()
    {
        var birthdate = new DateOnly(1990, 5, 15);

        _context.Users.Add(new User
        {
            Id = UserId,
            PhoneNumber = "+380991111111",
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        });
        await _context.SaveChangesAsync();

        var handler = new UpdateUserCommandHandler(_context);
        var command = new UpdateUserCommand(
            UserId.ToString(),
            "user@example.com",
            "John",
            "Doe",
            birthdate,
            "https://example.com/avatar.png");

        var response = await handler.HandleAsync(command);

        response.Id.Should().Be(UserId);
        response.Phone.Should().Be("+380991111111");
        response.Email.Should().Be("user@example.com");
        response.FirstName.Should().Be("John");
        response.LastName.Should().Be("Doe");
        response.Birthdate.Should().Be(birthdate);
        response.ProfileImageUrl.Should().Be("https://example.com/avatar.png");
        response.ReferralCode.Should().BeNull();
        response.BonusBalance.Should().Be(0);

        var user = await _context.Users.FindAsync(UserId);
        user!.Email.Should().Be("user@example.com");
        user.FirstName.Should().Be("John");
        user.LastName.Should().Be("Doe");
        user.Birthdate.Should().Be(birthdate);
        user.ProfileImageUrl.Should().Be("https://example.com/avatar.png");
    }

    [Fact]
    public async Task UpdateUser_ShouldThrow_WhenUserIdInvalid()
    {
        var handler = new UpdateUserCommandHandler(_context);
        var command = new UpdateUserCommand("not-a-guid", null, null, null, null, null);

        var act = () => handler.HandleAsync(command);

        await act.Should().ThrowAsync<ArgumentException>().WithMessage("Invalid user ID");
    }

    [Fact]
    public async Task UpdateUser_ShouldThrow_WhenUserNotFound()
    {
        var handler = new UpdateUserCommandHandler(_context);
        var command = new UpdateUserCommand(UserId.ToString(), null, null, null, null, null);

        var act = () => handler.HandleAsync(command);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("User not found");
    }
}
