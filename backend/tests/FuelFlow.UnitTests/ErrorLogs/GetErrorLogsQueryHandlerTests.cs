using FluentAssertions;
using FuelFlow.Features.ErrorLogs;
using FuelFlow.Features.ErrorLogs.GetErrorLogs;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using FuelFlow.Testing;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.UnitTests.ErrorLogs;

/// <summary>
/// The error journal's list: which rows, and which page of them.
/// </summary>
/// <remarks>
/// The journal opens on what still needs attention, and a filter the platform does not
/// recognise must not reveal everything: it means "outstanding", the same default a caller who
/// passes nothing gets. The rule used to sit in the controller, where no unit test could reach
/// it.
/// </remarks>
public sealed class GetErrorLogsQueryHandlerTests : IDisposable
{
    private readonly ApplicationDbContext _context;
    private readonly GetErrorLogsQueryHandler _handler;

    public GetErrorLogsQueryHandlerTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _context = new ApplicationDbContext(options, TestQrCipher.Instance);
        _handler = new GetErrorLogsQueryHandler(_context);
    }

    public void Dispose()
    {
        _context.Database.EnsureDeleted();
        _context.Dispose();
    }

    [Fact]
    public async Task The_journal_opens_on_what_still_needs_attention()
    {
        await SeedAsync("outstanding one");
        await SeedAsync("already handled", resolved: true);

        var page = await _handler.HandleAsync(new GetErrorLogsQuery(), CancellationToken.None);

        page.Total.Should().Be(1);
        page.Items.Should().ContainSingle().Which.Message.Should().Be("outstanding one");
    }

    [Fact]
    public async Task A_filter_the_platform_does_not_recognise_does_not_reveal_everything()
    {
        await SeedAsync("outstanding one");
        await SeedAsync("already handled", resolved: true);

        // A number binds to no member of the enum, and it must mean "outstanding" - the same
        // default a caller who passes nothing gets.
        var page = await _handler.HandleAsync(
            new GetErrorLogsQuery(Resolved: (ResolutionFilter)99),
            CancellationToken.None);

        page.Total.Should().Be(1);
        page.Items.Should().ContainSingle().Which.Message.Should().Be("outstanding one");
    }

    [Fact]
    public async Task The_resolved_filter_reaches_the_handled_rows()
    {
        await SeedAsync("outstanding one");
        await SeedAsync("already handled", resolved: true);

        var page = await _handler.HandleAsync(
            new GetErrorLogsQuery(Resolved: ResolutionFilter.Resolved),
            CancellationToken.None);

        page.Items.Should().ContainSingle().Which.Message.Should().Be("already handled");
    }

    [Fact]
    public async Task The_search_reaches_the_trace_id()
    {
        var traceId = Guid.NewGuid().ToString();
        await SeedAsync("first record of the incident", traceId: traceId);
        await SeedAsync("an unrelated failure", traceId: Guid.NewGuid().ToString());

        var page = await _handler.HandleAsync(
            new GetErrorLogsQuery(Search: traceId),
            CancellationToken.None);

        page.Items.Should().ContainSingle().Which.Message.Should().Be("first record of the incident");
    }

    private async Task SeedAsync(string message, bool resolved = false, string? traceId = null)
    {
        _context.ErrorLogs.Add(new ErrorLog
        {
            Id = Guid.CreateVersion7(),
            LoggedAtUtc = TestClock.UtcNow,
            Level = "Error",
            Message = message,
            Source = "FuelFlow.Middleware.GlobalExceptionHandler",
            TraceId = traceId,
            ResolvedAtUtc = resolved ? TestClock.UtcNow.AddMinutes(-5) : null
        });

        await _context.SaveChangesAsync();
    }
}
