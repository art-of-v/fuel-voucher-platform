using FluentAssertions;
using FuelFlow.Features.Providers;
using FuelFlow.Features.Users.UpdateUser;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using FuelFlow.SharedKernel.Notifications.Email;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace FuelFlow.UnitTests.Users;

public sealed class UpdateUserCommandHandlerTests : IDisposable
{
    private static readonly Guid UserId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");

    private readonly ApplicationDbContext _context;
    private readonly List<(string To, EmailMessage Message)> _sends = new();

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
    public async Task UpdateUser_ShouldUpdateProfileFields_ButNotWriteEmailDirectly()
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

        // Email is a verified change: NOT written directly, held pending, confirmation sent.
        response.EmailChangePending.Should().BeTrue();
        response.Email.Should().BeNull("the active email must not change until confirmed");

        var user = await _context.Users.FindAsync(UserId);
        user!.Email.Should().BeNull();
        user.PendingEmail.Should().Be("user@example.com");
        _sends.Should().ContainSingle();
        _sends[0].To.Should().Be("user@example.com");
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
        _sends.Should().BeEmpty();
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

    private UpdateUserCommandHandler CreateHandler()
    {
        var email = new Mock<IEmailSender>();
        email.SetupGet(e => e.IsConfigured).Returns(true);
        email.Setup(e => e.SendAsync(It.IsAny<string>(), It.IsAny<EmailMessage>(), It.IsAny<CancellationToken>()))
            .Callback<string, EmailMessage, CancellationToken>((to, message, _) => _sends.Add((to, message)))
            .Returns(Task.CompletedTask);

        return new UpdateUserCommandHandler(
            _context, email.Object, new ProviderEventService(_context),
            NullLogger<UpdateUserCommandHandler>.Instance);
    }
}
