using FluentAssertions;
using FuelFlow.Features.Audit.GetAuditFacets;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using FuelFlow.Testing;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.UnitTests.Audit;

/// <summary>
/// The values the admin's audit filters offer.
/// </summary>
/// <remarks>
/// The dropdowns list what the journal holds, not what the code knows about - and the author
/// list holds people, so a row a machine wrote (which has no author) contributes no empty entry
/// to the "who changed this" dropdown.
/// </remarks>
public sealed class GetAuditFacetsQueryHandlerTests : IDisposable
{
    private readonly ApplicationDbContext _context;
    private readonly GetAuditFacetsQueryHandler _handler;

    public GetAuditFacetsQueryHandlerTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _context = new ApplicationDbContext(options, TestQrCipher.Instance);
        _handler = new GetAuditFacetsQueryHandler(_context);
    }

    public void Dispose()
    {
        _context.Database.EnsureDeleted();
        _context.Dispose();
    }

    [Fact]
    public async Task The_facets_are_the_distinct_values_the_journal_holds()
    {
        await SeedAsync("BrandUpdated", "Brand", "okko", "Artem");
        await SeedAsync("BrandUpdated", "Brand", "shell", "Artem");
        await SeedAsync("OrderCreated", "Order", "order-1", "Taras");
        await SeedAsync("OrderCreated", "Order", "order-2", null);

        var facets = await _handler.HandleAsync(new GetAuditFacetsQuery(), CancellationToken.None);

        facets.EventTypes.Should().BeEquivalentTo(["BrandUpdated", "OrderCreated"]);
        facets.AggregateTypes.Should().BeEquivalentTo(["Brand", "Order"]);
        facets.Users.Should().BeEquivalentTo(["Artem", "Taras"]);
    }

    private async Task SeedAsync(
        string eventType,
        string aggregateType,
        string aggregateId,
        string? changedBy)
    {
        _context.Set<AuditEventOutbox>().Add(new AuditEventOutbox
        {
            Id = Guid.CreateVersion7(),
            AggregateType = aggregateType,
            AggregateId = aggregateId,
            ScopeId = "scope",
            EventType = eventType,
            NewValue = "new",
            ChangedByUserName = changedBy,
            Summary = "something happened",
            ChangedAtUtc = TestClock.UtcNow
        });

        await _context.SaveChangesAsync();
    }
}
