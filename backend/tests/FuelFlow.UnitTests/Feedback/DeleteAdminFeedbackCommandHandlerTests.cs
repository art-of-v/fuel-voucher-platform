using FluentAssertions;
using FuelFlow.Features.Feedback.DeleteAdminFeedback;
using FuelFlow.Features.Feedback.SharedModels;
using FuelFlow.Persistence;
using FuelFlow.Testing;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.UnitTests.Feedback;

/// <summary>
/// The refusal of an unscoped delete.
/// </summary>
/// <remarks>
/// The delete itself needs a real database - it is a bulk delete - but the refusal does not touch
/// it, and it is the one that matters: an unscoped delete would empty the whole backlog on a
/// mistyped query string.
/// </remarks>
public sealed class DeleteAdminFeedbackCommandHandlerTests : IDisposable
{
    private readonly ApplicationDbContext _context;
    private readonly DeleteAdminFeedbackCommandHandler _handler;

    public DeleteAdminFeedbackCommandHandlerTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _context = new ApplicationDbContext(options, TestQrCipher.Instance);
        _handler = new DeleteAdminFeedbackCommandHandler(_context);
    }

    public void Dispose()
    {
        _context.Database.EnsureDeleted();
        _context.Dispose();
    }

    [Fact]
    public async Task A_delete_that_names_nothing_is_refused()
    {
        var result = await _handler.HandleAsync(
            new DeleteAdminFeedbackCommand(null, null),
            CancellationToken.None);

        result.Status.Should().Be("Refused");
        result.Error.Should().Be("Supply either id or verdict.");
        result.Affected.Should().Be(0);
    }
}
