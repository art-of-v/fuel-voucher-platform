using FluentAssertions;
using FuelFlow.Features.Audit.GetAuditEvents;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using FuelFlow.Testing;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.UnitTests.Audit;

/// <summary>
/// The audit journal's list: which rows, and which page of them.
/// </summary>
/// <remarks>
/// The filters used to be the controller's business, and that is exactly why nothing in the unit
/// suite could pin them down. A filter that matches nothing must show nothing - a filter that
/// quietly widens turns a narrow question into a dump of the table.
/// </remarks>
public sealed class GetAuditEventsQueryHandlerTests : IDisposable
{
    private readonly ApplicationDbContext _context;
    private readonly GetAuditEventsQueryHandler _handler;

    public GetAuditEventsQueryHandlerTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _context = new ApplicationDbContext(options, TestQrCipher.Instance);
        _handler = new GetAuditEventsQueryHandler(_context);
    }

    public void Dispose()
    {
        _context.Database.EnsureDeleted();
        _context.Dispose();
    }

    [Fact]
    public async Task A_filter_that_matches_nothing_shows_nothing()
    {
        await SeedAsync("BrandUpdated", "Brand", "okko", "Artem", "the brand was renamed");

        var page = await _handler.HandleAsync(
            new GetAuditEventsQuery(EventType: "OrderCreated"),
            CancellationToken.None);

        page.Total.Should().Be(0);
        page.Events.Should().BeEmpty();
    }

    [Fact]
    public async Task The_count_is_of_the_filtered_journal()
    {
        await SeedAsync("BrandUpdated", "Brand", "okko", "Artem", "the brand was renamed");
        await SeedAsync("OrderCreated", "Order", "order-1", "Artem", "an order arrived");
        await SeedAsync("OrderCreated", "Order", "order-2", "Artem", "another order arrived");

        var page = await _handler.HandleAsync(
            new GetAuditEventsQuery(EventType: "OrderCreated"),
            CancellationToken.None);

        page.Total.Should().Be(2);
        page.Events.Should().HaveCount(2);
    }

    [Fact]
    public async Task The_free_text_search_reaches_the_summary_the_aggregate_and_the_author()
    {
        await SeedAsync("BrandUpdated", "Brand", "okko", "Artem", "the brand was renamed");
        await SeedAsync("OrderCreated", "Order", "order-42", "Taras", "an order arrived");

        var byAggregate = await _handler.HandleAsync(
            new GetAuditEventsQuery(Search: "order-42"), CancellationToken.None);
        byAggregate.Events.Should().ContainSingle().Which.AggregateId.Should().Be("order-42");

        var byAuthor = await _handler.HandleAsync(
            new GetAuditEventsQuery(Search: "Taras"), CancellationToken.None);
        byAuthor.Events.Should().ContainSingle().Which.ChangedByUserName.Should().Be("Taras");

        var bySummary = await _handler.HandleAsync(
            new GetAuditEventsQuery(Search: "renamed"), CancellationToken.None);
        bySummary.Events.Should().ContainSingle().Which.EventType.Should().Be("BrandUpdated");
    }

    [Fact]
    public async Task The_date_range_narrows_the_journal()
    {
        await SeedAsync("OrderCreated", "Order", "order-1", "Artem", "an order arrived");
        await SeedAsync(
            "OrderCreated",
            "Order",
            "order-2",
            "Artem",
            "an older order arrived",
            TestClock.UtcNow.AddHours(-2));

        var page = await _handler.HandleAsync(
            new GetAuditEventsQuery(From: TestClock.UtcNow.AddMinutes(-1)),
            CancellationToken.None);

        page.Total.Should().Be(1);
        page.Events.Should().ContainSingle().Which.AggregateId.Should().Be("order-1");
    }

    private async Task SeedAsync(
        string eventType,
        string aggregateType,
        string aggregateId,
        string? changedBy,
        string summary,
        DateTime? changedAtUtc = null)
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
            Summary = summary,
            ChangedAtUtc = changedAtUtc ?? TestClock.UtcNow
        });

        await _context.SaveChangesAsync();
    }
}
