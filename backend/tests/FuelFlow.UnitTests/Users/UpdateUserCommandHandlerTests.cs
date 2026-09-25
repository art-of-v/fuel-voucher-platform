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
    public async Task UpdateUser_ShouldUpdateProfileFields_AndIgnoreEmail()
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

        var handler = CreateHandler();
        var command = new UpdateUserCommand(
            UserId.ToString(),
            "user@example.com",
            "John",
            "Doe",
            birthdate,
            "https://example.com/avatar.png",
            "https://api.test");

        var response = await handler.HandleAsync(command);

        // Non-email fields apply immediately.
        response.FirstName.Should().Be("John");
        response.LastName.Should().Be("Doe");
        response.Birthdate.Should().Be(birthdate);
        response.ProfileImageUrl.Should().Be("https://example.com/avatar.png");

        // Email is no longer handled on this path — it moved to the step-up-guarded
        // POST /api/users/email/change. The field is still accepted (older app builds send it) but
        // ignored: no active email is written and no pending change is staged.
        response.EmailChangePending.Should().BeFalse();
        response.Email.Should().BeNull();

        var user = await _context.Users.FindAsync(UserId);
        user!.Email.Should().BeNull();
        user.PendingEmail.Should().BeNull("email changes never start on the update path");
    }

    [Fact]
    public async Task UpdateUser_ShouldNotFlagEmail_WhenUnchanged()
    {
        _context.Users.Add(new User
        {
            Id = UserId,
            PhoneNumber = "+380991111111",
            Email = "same@example.com",
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        });
        await _context.SaveChangesAsync();

        var handler = CreateHandler();
        var response = await handler.HandleAsync(new UpdateUserCommand(
            UserId.ToString(), "same@example.com", "N", null, null, null, "https://api.test"));

        response.EmailChangePending.Should().BeFalse();
        response.Email.Should().Be("same@example.com");

        var user = await _context.Users.FindAsync(UserId);
        user!.PendingEmail.Should().BeNull();
    }

    [Fact]
    public async Task UpdateUser_ShouldThrow_WhenUserIdInvalid()
    {
        var handler = CreateHandler();
        var command = new UpdateUserCommand("not-a-guid", null, null, null, null, null);

        var act = () => handler.HandleAsync(command);

        await act.Should().ThrowAsync<ArgumentException>().WithMessage("Invalid user ID");
    }

    [Fact]
    public async Task UpdateUser_ShouldThrow_WhenUserNotFound()
    {
        var handler = CreateHandler();
        var command = new UpdateUserCommand(UserId.ToString(), null, null, null, null, null);

        var act = () => handler.HandleAsync(command);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("User not found");
    }

    private UpdateUserCommandHandler CreateHandler() => new(_context);
}
