using FluentAssertions;
using FuelFlow.Features.Brands;
using FuelFlow.Features.Feedback.SharedModels;
using FuelFlow.Features.Feedback.UpdateFeedbackStatus;
using FuelFlow.Persistence;
using FuelFlow.Testing;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.UnitTests.Feedback;

/// <summary>
/// The refusals of a status change.
/// </summary>
/// <remarks>
/// The write itself needs a real database - it is a bulk update - but the three refusals do not
/// touch it, and they are the ones that decide what a caller is told: a request with no ids, a
/// status the platform does not know, and a caller it cannot name are three different
/// situations, and the caller must be able to tell them apart.
/// </remarks>
public sealed class UpdateFeedbackStatusCommandHandlerTests : IDisposable
{
    private static readonly Guid StaffId = Guid.NewGuid();

    private readonly ApplicationDbContext _context;
    private readonly UpdateFeedbackStatusCommandHandler _handler;

    public UpdateFeedbackStatusCommandHandlerTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _context = new ApplicationDbContext(options, TestQrCipher.Instance);
        _handler = new UpdateFeedbackStatusCommandHandler(_context, new AuditEventService(_context));
    }

    public void Dispose()
    {
        _context.Database.EnsureDeleted();
        _context.Dispose();
    }

    [Fact]
    public async Task A_request_without_ids_is_refused()
    {
        var result = await _handler.HandleAsync(
            new UpdateFeedbackStatusCommand([], nameof(FeedbackStatus.Handled), StaffId, "Artem"),
            CancellationToken.None);

        result.Status.Should().Be("NoIds");
        result.Error.Should().Be("No feedback ids supplied");
        result.Affected.Should().Be(0);
    }

    [Fact]
    public async Task A_status_the_platform_does_not_know_is_refused_rather_than_stored()
    {
        var result = await _handler.HandleAsync(
            new UpdateFeedbackStatusCommand([Guid.NewGuid()], "Archived", StaffId, "Artem"),
            CancellationToken.None);

        result.Status.Should().Be("UnknownStatus");
        result.Error.Should().Be("Unknown status 'Archived'.");
        result.Affected.Should().Be(0);
    }

    [Fact]
    public async Task A_caller_the_platform_cannot_name_is_refused()
    {
        var result = await _handler.HandleAsync(
            new UpdateFeedbackStatusCommand([Guid.NewGuid()], nameof(FeedbackStatus.Handled), null, null),
            CancellationToken.None);

        result.Status.Should().Be("Unauthenticated");
        result.Affected.Should().Be(0);
    }
}
