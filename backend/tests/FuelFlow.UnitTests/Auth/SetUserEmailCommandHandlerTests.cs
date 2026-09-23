using System.Text.RegularExpressions;
using FluentAssertions;
using FuelFlow.Features.Auth.AdminUser.SetUserEmail;
using FuelFlow.Features.Auth.ConfirmEmailChange;
using FuelFlow.Features.Auth.SharedModels;
using FuelFlow.Features.Providers;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using FuelFlow.SharedKernel.Notifications.Email;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace FuelFlow.UnitTests.Auth;

public sealed class SetUserEmailCommandHandlerTests
{
    private const string ConfirmBaseUrl = "https://api.test";

    [Fact]
    public async Task AdminEmailChange_ShouldHoldPending_NotTouchActiveEmail_AndSendLink()
    {
        await using var context = CreateContext();
        await SeedRolesAsync(context);
        var target = await AddUserAsync(context, SeedRoles.UserName, SeedRoles.UserRoleId, email: "old@example.com");
        var (handler, sends) = CreateHandler(context);

        var result = await handler.HandleAsync(
            new SetUserEmailCommand(target.Id, "new@example.com", Guid.NewGuid(), "Actor", "ProductOwner", ConfirmBaseUrl),
            CancellationToken.None);

        result.Success.Should().BeTrue();
        result.PendingConfirmation.Should().BeTrue();

        var stored = await context.Users.AsNoTracking().FirstAsync(u => u.Id == target.Id);
        stored.Email.Should().Be("old@example.com", "the active email must not change until confirmed");
        stored.PendingEmail.Should().Be("new@example.com");
        stored.PendingEmailTokenHash.Should().NotBeNullOrEmpty();

        sends.Should().ContainSingle();
        sends[0].To.Should().Be("new@example.com", "the confirmation link goes to the NEW address");

        var audit = await context.Set<ProviderEventOutbox>().SingleAsync();
        audit.EventType.Should().Be("EmailChangeRequested");
    }

    [Fact]
    public async Task Confirm_ShouldPromotePendingToActive()
    {
        await using var context = CreateContext();
        await SeedRolesAsync(context);
        var target = await AddUserAsync(context, SeedRoles.UserName, SeedRoles.UserRoleId, email: "old@example.com");
        var (handler, sends) = CreateHandler(context);

        await handler.HandleAsync(
            new SetUserEmailCommand(target.Id, "new@example.com", Guid.NewGuid(), "Actor", "ProductOwner", ConfirmBaseUrl),
            CancellationToken.None);

        var token = Regex.Match(sends[0].Message.TextBody, @"token=([A-Fa-f0-9]+)").Groups[1].Value;
        token.Should().NotBeNullOrEmpty();

        var confirm = new ConfirmEmailChangeCommandHandler(
            context, EmailSenderMock().Object, new ProviderEventService(context),
            NullLogger<ConfirmEmailChangeCommandHandler>.Instance);

        var confirmResult = await confirm.HandleAsync(new ConfirmEmailChangeCommand(token), CancellationToken.None);

        confirmResult.Status.Should().Be(ConfirmEmailChangeStatus.Confirmed);
        var stored = await context.Users.AsNoTracking().FirstAsync(u => u.Id == target.Id);
        stored.Email.Should().Be("new@example.com");
        stored.PendingEmail.Should().BeNull();
        stored.PendingEmailTokenHash.Should().BeNull();
    }

    [Fact]
    public async Task Confirm_ShouldRejectUnknownToken()
    {
        await using var context = CreateContext();
        await SeedRolesAsync(context);
        var confirm = new ConfirmEmailChangeCommandHandler(
            context, EmailSenderMock().Object, new ProviderEventService(context),
            NullLogger<ConfirmEmailChangeCommandHandler>.Instance);

        var result = await confirm.HandleAsync(new ConfirmEmailChangeCommand("DEADBEEF"), CancellationToken.None);

        result.Status.Should().Be(ConfirmEmailChangeStatus.InvalidToken);
    }

    [Fact]
    public async Task AdminEmailChange_ShouldForbid_WhenActorCannotManageTarget()
    {
        await using var context = CreateContext();
        await SeedRolesAsync(context);
        var admin = await AddUserAsync(context, SeedRoles.AdminName, SeedRoles.AdminRoleId, email: null);
        var (handler, _) = CreateHandler(context);

        // A Manager cannot manage an Admin.
        var result = await handler.HandleAsync(
            new SetUserEmailCommand(admin.Id, "x@example.com", Guid.NewGuid(), "Actor", "Manager", ConfirmBaseUrl),
            CancellationToken.None);

        result.Forbidden.Should().BeTrue();
    }

    [Fact]
    public async Task AdminEmailChange_ShouldRejectInvalidAddress()
    {
        await using var context = CreateContext();
        await SeedRolesAsync(context);
        var target = await AddUserAsync(context, SeedRoles.UserName, SeedRoles.UserRoleId, email: null);
        var (handler, _) = CreateHandler(context);

        var result = await handler.HandleAsync(
            new SetUserEmailCommand(target.Id, "not-an-email", Guid.NewGuid(), "Actor", "ProductOwner", ConfirmBaseUrl),
            CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("Invalid");
    }

    [Fact]
    public async Task AdminEmailChange_ShouldClearDirectly_WhenBlank()
    {
        await using var context = CreateContext();
        await SeedRolesAsync(context);
        var target = await AddUserAsync(context, SeedRoles.UserName, SeedRoles.UserRoleId, email: "old@example.com");
        var (handler, sends) = CreateHandler(context);

        var result = await handler.HandleAsync(
            new SetUserEmailCommand(target.Id, "  ", Guid.NewGuid(), "Actor", "ProductOwner", ConfirmBaseUrl),
            CancellationToken.None);

        result.Success.Should().BeTrue();
        result.PendingConfirmation.Should().BeFalse();
        sends.Should().BeEmpty("clearing needs no confirmation email");
        var stored = await context.Users.AsNoTracking().FirstAsync(u => u.Id == target.Id);
        stored.Email.Should().BeNull();
    }

    // --- Helpers ---

    private sealed record SentEmail(string To, EmailMessage Message);

    private static Mock<IEmailSender> EmailSenderMock()
    {
        var mock = new Mock<IEmailSender>();
        mock.SetupGet(e => e.IsConfigured).Returns(true);
        return mock;
    }

    private static (SetUserEmailCommandHandler Handler, List<SentEmail> Sends) CreateHandler(ApplicationDbContext context)
    {
        var sends = new List<SentEmail>();
        var mock = EmailSenderMock();
        mock.Setup(e => e.SendAsync(It.IsAny<string>(), It.IsAny<EmailMessage>(), It.IsAny<CancellationToken>()))
            .Callback<string, EmailMessage, CancellationToken>((to, message, _) => sends.Add(new SentEmail(to, message)))
            .Returns(Task.CompletedTask);

        var handler = new SetUserEmailCommandHandler(
            context, mock.Object, new ProviderEventService(context),
            NullLogger<SetUserEmailCommandHandler>.Instance);
        return (handler, sends);
    }

    private static ApplicationDbContext CreateContext() => new(
        new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static async Task SeedRolesAsync(ApplicationDbContext context)
    {
        context.Roles.AddRange(
            new Role { Id = SeedRoles.ProductOwnerId, Name = SeedRoles.ProductOwnerName, CreatedAtUtc = DateTime.UtcNow },
            new Role { Id = SeedRoles.AdminRoleId, Name = SeedRoles.AdminName, CreatedAtUtc = DateTime.UtcNow },
            new Role { Id = SeedRoles.ManagerRoleId, Name = SeedRoles.ManagerName, CreatedAtUtc = DateTime.UtcNow },
            new Role { Id = SeedRoles.UserRoleId, Name = SeedRoles.UserName, CreatedAtUtc = DateTime.UtcNow });
        await context.SaveChangesAsync();
    }

    private static async Task<User> AddUserAsync(ApplicationDbContext context, string roleName, Guid roleId, string? email)
    {
        var user = new User
        {
            Id = Guid.NewGuid(),
            PhoneNumber = "+380991234567",
            RoleId = roleId,
            Email = email,
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow,
        };
        context.Users.Add(user);
        await context.SaveChangesAsync();
        return user;
    }
}
