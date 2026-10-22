using FluentAssertions;
using FuelFlow.Features.Brands;
using FuelFlow.Features.ErrorLogs.ResolveErrorLogs;
using FuelFlow.Persistence;
using FuelFlow.Testing;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.UnitTests.ErrorLogs;

/// <summary>
/// The refusals of a resolve call.
/// </summary>
/// <remarks>
/// The write itself needs a real database - it is a bulk update - but the two refusals do not
/// touch it, and they are the ones that decide what a caller is told: a request with no ids and
/// a caller the platform cannot name are different situations, and the caller must be able to
/// tell them apart.
/// </remarks>
public sealed class ResolveErrorLogsCommandHandlerTests : IDisposable
{
    private static readonly Guid StaffId = Guid.NewGuid();

    private readonly ApplicationDbContext _context;
    private readonly ResolveErrorLogsCommandHandler _handler;

    public ResolveErrorLogsCommandHandlerTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _context = new ApplicationDbContext(options, TestQrCipher.Instance);
        _handler = new ResolveErrorLogsCommandHandler(_context, new AuditEventService(_context));
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
            new ResolveErrorLogsCommand([], true, StaffId, "Artem"),
            CancellationToken.None);

        result.Status.Should().Be("NoIds");
        result.Error.Should().Be("No error ids supplied");
        result.Changed.Should().Be(0);
    }

    [Fact]
    public async Task A_caller_the_platform_cannot_name_is_refused()
    {
        var result = await _handler.HandleAsync(
            new ResolveErrorLogsCommand([Guid.NewGuid()], true, null, null),
            CancellationToken.None);

        result.Status.Should().Be("Unauthenticated");
        result.Changed.Should().Be(0);
    }
}
