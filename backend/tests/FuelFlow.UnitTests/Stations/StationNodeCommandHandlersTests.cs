using FluentAssertions;
using FuelFlow.Features.Stations;
using FuelFlow.Features.Stations.CreateStationNode;
using FuelFlow.Features.Stations.DeleteStationNode;
using FuelFlow.Features.Stations.GetAdminStationNodeById;
using FuelFlow.Features.Stations.GetAdminStationNodes;
using FuelFlow.Features.Stations.ImportStationNodes;
using FuelFlow.Features.Stations.UpdateStationNode;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.UnitTests.Stations;

public sealed class StationNodeCommandHandlersTests : IDisposable
{
    private readonly ApplicationDbContext _context;

    public StationNodeCommandHandlersTests()
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
    public async Task CreateStationNode_ShouldAdd()
    {
        await AddBrandAsync("okko");
        var node = Node("okko-1", "okko", "OKKO Центр", 50.45, 30.52);

        var result = await new CreateStationNodeCommandHandler(_context).HandleAsync(new CreateStationNodeCommand(node));

        result.Success.Should().BeTrue();
        result.Node.Should().BeSameAs(node);
        var persisted = await _context.StationNodes.FindAsync("okko-1");
        persisted.Should().NotBeNull();
        persisted!.CreatedAtUtc.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task CreateStationNode_ShouldReturnConflict_WhenIdExists()
    {
        await AddBrandAsync("okko");
        _context.StationNodes.Add(Node("okko-1", "okko", "Existing", 50.45, 30.52));
        await _context.SaveChangesAsync();

        var result = await new CreateStationNodeCommandHandler(_context)
            .HandleAsync(new CreateStationNodeCommand(Node("okko-1", "okko", "Dup", 50.45, 30.52)));

        result.Conflict.Should().BeTrue();
        result.Error.Should().Contain("already exists");
    }

    [Fact]
    public async Task CreateStationNode_ShouldReject_WhenBrandMissing()
    {
        var result = await new CreateStationNodeCommandHandler(_context)
            .HandleAsync(new CreateStationNodeCommand(Node("x-1", "ghost", "X", 50.45, 30.52)));

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("does not exist");
    }

    [Fact]
    public async Task CreateStationNode_ShouldReject_WhenCoordinatesInvalid()
    {
        await AddBrandAsync("okko");
        var result = await new CreateStationNodeCommandHandler(_context)
            .HandleAsync(new CreateStationNodeCommand(Node("okko-1", "okko", "X", 999, 30.52)));

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("Lat");
    }

    [Fact]
    public async Task UpdateStationNode_ShouldUpdateFields()
    {
        await AddBrandAsync("okko");
        await AddBrandAsync("wog");
        _context.StationNodes.Add(Node("n-1", "okko", "Old", 50.0, 30.0));
        await _context.SaveChangesAsync();

        var result = await new UpdateStationNodeCommandHandler(_context)
            .HandleAsync(new UpdateStationNodeCommand("n-1", Node("n-1", "wog", "New", 49.0, 24.0, city: "Львів")));

        result.Success.Should().BeTrue();
        var persisted = await _context.StationNodes.FindAsync("n-1");
        persisted!.StationId.Should().Be("wog");
        persisted.Name.Should().Be("New");
        persisted.City.Should().Be("Львів");
        persisted.Lat.Should().Be(49.0);
    }

    [Fact]
    public async Task UpdateStationNode_ShouldReturnNotFound()
    {
        await AddBrandAsync("okko");
        var result = await new UpdateStationNodeCommandHandler(_context)
            .HandleAsync(new UpdateStationNodeCommand("missing", Node("missing", "okko", "X", 50.0, 30.0)));

        result.NotFound.Should().BeTrue();
    }

    [Fact]
    public async Task UpdateStationNode_ShouldReject_WhenBrandMissing()
    {
        await AddBrandAsync("okko");
        _context.StationNodes.Add(Node("n-1", "okko", "Old", 50.0, 30.0));
        await _context.SaveChangesAsync();

        var result = await new UpdateStationNodeCommandHandler(_context)
            .HandleAsync(new UpdateStationNodeCommand("n-1", Node("n-1", "ghost", "New", 50.0, 30.0)));

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("does not exist");
    }

    [Fact]
    public async Task DeleteStationNode_ShouldRemove()
    {
        await AddBrandAsync("okko");
        _context.StationNodes.Add(Node("n-1", "okko", "X", 50.0, 30.0));
        await _context.SaveChangesAsync();

        var result = await new DeleteStationNodeCommandHandler(_context).HandleAsync(new DeleteStationNodeCommand("n-1"));

        result.Should().BeTrue();
        (await _context.StationNodes.FindAsync("n-1")).Should().BeNull();
    }

    [Fact]
    public async Task DeleteStationNode_ShouldReturnFalse_WhenNotFound()
    {
        var result = await new DeleteStationNodeCommandHandler(_context).HandleAsync(new DeleteStationNodeCommand("missing"));
        result.Should().BeFalse();
    }

    [Fact]
    public async Task GetAdminStationNodes_ShouldFilterByStationId()
    {
        await AddBrandAsync("okko");
        await AddBrandAsync("wog");
        _context.StationNodes.AddRange(
            Node("o-1", "okko", "A", 50.0, 30.0),
            Node("o-2", "okko", "B", 50.1, 30.1),
            Node("w-1", "wog", "C", 50.2, 30.2));
        await _context.SaveChangesAsync();

        var result = await new GetAdminStationNodesQueryHandler(_context)
            .HandleAsync(new GetAdminStationNodesQuery(1, 50, "okko"));

        result.TotalCount.Should().Be(2);
        result.Items.Should().OnlyContain(n => n.StationId == "okko");
    }

    [Fact]
    public async Task GetAdminStationNodeById_ShouldReturnNode_OrNull()
    {
        await AddBrandAsync("okko");
        _context.StationNodes.Add(Node("n-1", "okko", "X", 50.0, 30.0));
        await _context.SaveChangesAsync();

        var handler = new GetAdminStationNodeByIdQueryHandler(_context);
        (await handler.HandleAsync(new GetAdminStationNodeByIdQuery("n-1"))).Should().NotBeNull();
        (await handler.HandleAsync(new GetAdminStationNodeByIdQuery("missing"))).Should().BeNull();
    }

    [Fact]
    public async Task Import_ShouldAddNewNodes_FromJson()
    {
        await AddBrandAsync("okko");
        var json = """
        [
          { "id": "okko-1", "stationId": "okko", "name": "Центр", "lat": 50.45, "lng": 30.52 },
          { "stationId": "okko", "name": "Південь", "lat": 50.40, "lng": 30.50 }
        ]
        """;

        var result = await new ImportStationNodesCommandHandler(_context)
            .HandleAsync(new ImportStationNodesCommand(json, StationNodeImportFormat.Json));

        result.Added.Should().Be(2);
        result.Updated.Should().Be(0);
        result.Errors.Should().BeEmpty();
        // The id-less row got a coordinate-derived id.
        (await _context.StationNodes.FindAsync(StationNodeCoordinates.DeriveId("okko", 50.40, 30.50))).Should().NotBeNull();
    }

    [Fact]
    public async Task Import_ShouldUpdateExisting_PreservingCreatedAt()
    {
        await AddBrandAsync("okko");
        var original = Node("okko-1", "okko", "Old", 50.45, 30.52);
        original.CreatedAtUtc = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        _context.StationNodes.Add(original);
        await _context.SaveChangesAsync();

        var json = """[ { "id": "okko-1", "stationId": "okko", "name": "New Name", "lat": 50.45, "lng": 30.52 } ]""";
        var result = await new ImportStationNodesCommandHandler(_context)
            .HandleAsync(new ImportStationNodesCommand(json, StationNodeImportFormat.Json));

        result.Added.Should().Be(0);
        result.Updated.Should().Be(1);
        var persisted = await _context.StationNodes.FindAsync("okko-1");
        persisted!.Name.Should().Be("New Name");
        persisted.CreatedAtUtc.Should().Be(new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public async Task Import_ShouldReportPerRowErrors_ButKeepGoing()
    {
        await AddBrandAsync("okko");
        var json = """
        [
          { "stationId": "okko", "name": "Good", "lat": 50.45, "lng": 30.52 },
          { "stationId": "ghost", "name": "BadBrand", "lat": 50.4, "lng": 30.5 },
          { "stationId": "okko", "name": "BadCoords", "lat": 999, "lng": 30.5 },
          { "stationId": "okko", "lat": 50.4, "lng": 30.5 }
        ]
        """;

        var result = await new ImportStationNodesCommandHandler(_context)
            .HandleAsync(new ImportStationNodesCommand(json, StationNodeImportFormat.Json));

        result.Added.Should().Be(1);
        result.Errors.Should().HaveCount(3);
        result.Errors.Select(e => e.Line).Should().BeEquivalentTo(new[] { 2, 3, 4 });
    }

    [Fact]
    public async Task Import_ShouldDedupeWithinBatch_LastWins()
    {
        await AddBrandAsync("okko");
        var json = """
        [
          { "id": "okko-1", "stationId": "okko", "name": "First", "lat": 50.45, "lng": 30.52 },
          { "id": "okko-1", "stationId": "okko", "name": "Second", "lat": 50.45, "lng": 30.52 }
        ]
        """;

        var result = await new ImportStationNodesCommandHandler(_context)
            .HandleAsync(new ImportStationNodesCommand(json, StationNodeImportFormat.Json));

        result.Added.Should().Be(1);
        (await _context.StationNodes.FindAsync("okko-1"))!.Name.Should().Be("Second");
    }

    [Fact]
    public async Task Import_ShouldParseCsv_WithQuotedCommaInAddress()
    {
        await AddBrandAsync("okko");
        var csv = "id,stationId,name,address,lat,lng\n" +
                  "okko-1,okko,Центр,\"Проспект Перемоги, 98\",50.45,30.52\n";

        var result = await new ImportStationNodesCommandHandler(_context)
            .HandleAsync(new ImportStationNodesCommand(csv, StationNodeImportFormat.Csv));

        result.Added.Should().Be(1);
        (await _context.StationNodes.FindAsync("okko-1"))!.Address.Should().Be("Проспект Перемоги, 98");
    }

    [Fact]
    public void Parser_ShouldReportError_ForInvalidCsvCoordinate()
    {
        var csv = "id,stationId,name,lat,lng\nokko-1,okko,Центр,not-a-number,30.52\n";
        var parsed = StationNodeImportParser.Parse(csv, StationNodeImportFormat.Csv);

        parsed.Rows.Should().BeEmpty();
        parsed.Errors.Should().ContainSingle().Which.Line.Should().Be(2);
    }

    [Fact]
    public void Parser_ShouldReportError_ForMalformedJson()
    {
        var parsed = StationNodeImportParser.Parse("{ not json", StationNodeImportFormat.Json);
        parsed.Errors.Should().NotBeEmpty();
    }

    private async Task AddBrandAsync(string id)
    {
        _context.Stations.Add(new Station { Id = id, Name = id.ToUpperInvariant(), Color = "#000000", LogoText = id });
        await _context.SaveChangesAsync();
    }

    private static StationNode Node(string id, string stationId, string name, double lat, double lng, string? city = null) =>
        new()
        {
            Id = id,
            StationId = stationId,
            Name = name,
            Lat = lat,
            Lng = lng,
            City = city,
        };
}
