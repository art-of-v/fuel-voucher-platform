using FluentAssertions;
using FuelFlow.Features.Brands;
using FuelFlow.Features.Brands.CreateBrandStation;
using FuelFlow.Features.Brands.DeleteBrandStation;
using FuelFlow.Features.Brands.GetAdminBrandStationById;
using FuelFlow.Features.Brands.GetAdminBrandStations;
using FuelFlow.Features.Brands.ImportBrandStations;
using FuelFlow.Features.Brands.UpdateBrandStation;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using FuelFlow.Testing;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.UnitTests.Brands;

public sealed class BrandStationCommandHandlersTests : IDisposable
{
    /// <summary>
    /// The networks and stations these fixtures use. Fixed Guids so an assertion can name one, and
    /// distinct slugs so two fixture brands cannot collide on the unique index.
    /// </summary>
    private static readonly Guid OkkoId = new("11111111-0000-4000-8000-00000000000a");
    private static readonly Guid WogId = new("22222222-0000-4000-8000-00000000000b");

    /// <summary>A brand id deliberately absent, so the "brand does not exist" paths have one to use.</summary>
    private static readonly Guid GhostBrandId = new("99999999-0000-4000-8000-000000000099");

    /// <summary>
    /// A station id for a label, stable across runs so an assertion can name it and a failure can be
    /// reproduced. Derived rather than hand-written per fixture so adding a case does not mean
    /// inventing another uuid.
    /// </summary>
    private static Guid NodeId(string label)
    {
        var bytes = System.Security.Cryptography.MD5.HashData(
            System.Text.Encoding.UTF8.GetBytes("ff-d21/" + label));
        return new Guid(bytes);
    }

    private readonly ApplicationDbContext _context;

    public BrandStationCommandHandlersTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _context = new ApplicationDbContext(options, TestQrCipher.Instance);
    }

    public void Dispose()
    {
        _context.Database.EnsureDeleted();
        _context.Dispose();
    }

    [Fact]
    public async Task CreateBrandStation_ShouldAdd()
    {
        await AddBrandAsync(OkkoId, "okko");
        var node = Node("okko-1", OkkoId, "OKKO Центр", 50.45, 30.52);

        var result = await new CreateBrandStationCommandHandler(_context).HandleAsync(new CreateBrandStationCommand(node));

        result.Success.Should().BeTrue();
        result.Node.Should().BeSameAs(node);
        var persisted = await ByExternalIdAsync("okko-1");
        persisted.Should().NotBeNull();
        persisted!.CreatedAtUtc.Should().BeCloseTo(TestClock.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task CreateBrandStation_ShouldReturnConflict_WhenIdExists()
    {
        await AddBrandAsync(OkkoId, "okko");
        _context.BrandStations.Add(Node("okko-1", OkkoId, "Existing", 50.45, 30.52));
        await _context.SaveChangesAsync();

        var result = await new CreateBrandStationCommandHandler(_context)
            .HandleAsync(new CreateBrandStationCommand(Node("okko-1", OkkoId, "Dup", 50.45, 30.52)));

        result.Conflict.Should().BeTrue();
        result.Error.Should().Contain("already exists");
        result.Code.Should().Be(BrandStationRefusalCodes.ExternalIdConflict);
    }

    [Fact]
    public async Task CreateBrandStation_ShouldReject_WhenBrandMissing()
    {
        var result = await new CreateBrandStationCommandHandler(_context)
            .HandleAsync(new CreateBrandStationCommand(Node("x-1", GhostBrandId, "X", 50.45, 30.52)));

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("does not exist");
        result.Code.Should().Be(BrandStationRefusalCodes.BrandMissing);
    }

    [Fact]
    public async Task CreateBrandStation_ShouldReject_WhenCoordinatesInvalid()
    {
        await AddBrandAsync(OkkoId, "okko");
        var result = await new CreateBrandStationCommandHandler(_context)
            .HandleAsync(new CreateBrandStationCommand(Node("okko-1", OkkoId, "X", 999, 30.52)));

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("Lat");
        result.Code.Should().Be(BrandStationRefusalCodes.CoordinatesInvalid);
    }

    [Fact]
    public async Task UpdateBrandStation_ShouldUpdateFields()
    {
        await AddBrandAsync(OkkoId, "okko");
        await AddBrandAsync(WogId, "wog");
        _context.BrandStations.Add(Node("n-1", OkkoId, "Old", 50.0, 30.0));
        await _context.SaveChangesAsync();

        var result = await new UpdateBrandStationCommandHandler(_context)
            .HandleAsync(new UpdateBrandStationCommand(NodeId("n-1"), Node("n-1", WogId, "New", 49.0, 24.0, city: "Львів")));

        result.Success.Should().BeTrue();
        var persisted = await ByExternalIdAsync("n-1");
        persisted!.BrandId.Should().Be(WogId);
        persisted.Name.Should().Be("New");
        persisted.City.Should().Be("Львів");
        persisted.Lat.Should().Be(49.0);
    }

    [Fact]
    public async Task UpdateBrandStation_ShouldReturnNotFound()
    {
        await AddBrandAsync(OkkoId, "okko");
        var result = await new UpdateBrandStationCommandHandler(_context)
            .HandleAsync(new UpdateBrandStationCommand(NodeId("missing"), Node("missing", OkkoId, "X", 50.0, 30.0)));

        result.NotFound.Should().BeTrue();
        result.Code.Should().Be(BrandStationRefusalCodes.NotFound);
    }

    [Fact]
    public async Task UpdateBrandStation_ShouldReject_WhenBrandMissing()
    {
        await AddBrandAsync(OkkoId, "okko");
        _context.BrandStations.Add(Node("n-1", OkkoId, "Old", 50.0, 30.0));
        await _context.SaveChangesAsync();

        var result = await new UpdateBrandStationCommandHandler(_context)
            .HandleAsync(new UpdateBrandStationCommand(NodeId("n-1"), Node("n-1", GhostBrandId, "New", 50.0, 30.0)));

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("does not exist");
        result.Code.Should().Be(BrandStationRefusalCodes.BrandMissing);
    }

    [Fact]
    public async Task DeleteBrandStation_ShouldRemove()
    {
        await AddBrandAsync(OkkoId, "okko");
        _context.BrandStations.Add(Node("n-1", OkkoId, "X", 50.0, 30.0));
        await _context.SaveChangesAsync();

        var result = await new DeleteBrandStationCommandHandler(_context).HandleAsync(new DeleteBrandStationCommand(NodeId("n-1")));

        result.Should().BeTrue();
        var archived = await _context.BrandStations.IgnoreQueryFilters().SingleAsync(n => n.Id == NodeId("n-1"));
           archived.IsDeleted.Should().BeTrue();
           archived.DeletedAtUtc.Should().NotBeNull();
           (await _context.BrandStations.CountAsync(n => n.Id == NodeId("n-1"))).Should().Be(0,
               "#217: a node is part of the Brand map other rows point at, so it is archived not removed");
    }

    [Fact]
    public async Task DeleteBrandStation_ShouldReturnFalse_WhenNotFound()
    {
        var result = await new DeleteBrandStationCommandHandler(_context).HandleAsync(new DeleteBrandStationCommand(NodeId("missing")));
        result.Should().BeFalse();
    }

    [Fact]
    public async Task GetAdminBrandStations_ShouldFilterByBrandId()
    {
        await AddBrandAsync(OkkoId, "okko");
        await AddBrandAsync(WogId, "wog");
        _context.BrandStations.AddRange(
            Node("o-1", OkkoId, "A", 50.0, 30.0),
            Node("o-2", OkkoId, "B", 50.1, 30.1),
            Node("w-1", WogId, "C", 50.2, 30.2));
        await _context.SaveChangesAsync();

        var result = await new GetAdminBrandStationsQueryHandler(_context)
            .HandleAsync(new GetAdminBrandStationsQuery(1, 50, OkkoId));

        result.TotalCount.Should().Be(2);
        result.Items.Should().OnlyContain(n => n.BrandId == OkkoId);
    }

    [Fact]
    public async Task GetAdminBrandStationById_ShouldReturnNode_OrNull()
    {
        await AddBrandAsync(OkkoId, "okko");
        _context.BrandStations.Add(Node("n-1", OkkoId, "X", 50.0, 30.0));
        await _context.SaveChangesAsync();

        var handler = new GetAdminBrandStationByIdQueryHandler(_context);
        (await handler.HandleAsync(new GetAdminBrandStationByIdQuery(NodeId("n-1")))).Should().NotBeNull();
        (await handler.HandleAsync(new GetAdminBrandStationByIdQuery(NodeId("missing")))).Should().BeNull();
    }

    [Fact]
    public async Task Import_ShouldAddNewNodes_FromJson()
    {
        await AddBrandAsync(OkkoId, "okko");
        var json = """
        [
          { "id": "okko-1", "BrandId": "okko", "name": "Центр", "lat": 50.45, "lng": 30.52 },
          { "BrandId": "okko", "name": "Південь", "lat": 50.40, "lng": 30.50 }
        ]
        """;

        var result = await new ImportBrandStationsCommandHandler(_context)
            .HandleAsync(new ImportBrandStationsCommand(json, BrandStationImportFormat.Json));

        result.Added.Should().Be(2);
        result.Updated.Should().Be(0);
        result.Errors.Should().BeEmpty();
        // The id-less row got a coordinate-derived id.
        (await ByExternalIdAsync(BrandStationCoordinates.DeriveId("okko", 50.40, 30.50))).Should().NotBeNull();
    }

    [Fact]
    public async Task Import_ShouldUpdateExisting_PreservingCreatedAt()
    {
        await AddBrandAsync(OkkoId, "okko");
        var original = Node("okko-1", OkkoId, "Old", 50.45, 30.52);
        original.CreatedAtUtc = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        _context.BrandStations.Add(original);
        await _context.SaveChangesAsync();

        var json = """[ { "id": "okko-1", "BrandId": "okko", "name": "New Name", "lat": 50.45, "lng": 30.52 } ]""";
        var result = await new ImportBrandStationsCommandHandler(_context)
            .HandleAsync(new ImportBrandStationsCommand(json, BrandStationImportFormat.Json));

        result.Added.Should().Be(0);
        result.Updated.Should().Be(1);
        var persisted = await ByExternalIdAsync("okko-1");
        persisted!.Name.Should().Be("New Name");
        persisted.CreatedAtUtc.Should().Be(new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public async Task Import_ShouldReportPerRowErrors_ButKeepGoing()
    {
        await AddBrandAsync(OkkoId, "okko");
        var json = """
        [
          { "BrandId": "okko", "name": "Good", "lat": 50.45, "lng": 30.52 },
          { "BrandId": "ghost", "name": "BadBrand", "lat": 50.4, "lng": 30.5 },
          { "BrandId": "okko", "name": "BadCoords", "lat": 999, "lng": 30.5 },
          { "BrandId": "okko", "lat": 50.4, "lng": 30.5 }
        ]
        """;

        var result = await new ImportBrandStationsCommandHandler(_context)
            .HandleAsync(new ImportBrandStationsCommand(json, BrandStationImportFormat.Json));

        result.Added.Should().Be(1);
        result.Errors.Should().HaveCount(3);
        result.Errors.Select(e => e.Line).Should().BeEquivalentTo(new[] { 2, 3, 4 });
    }

    [Fact]
    public async Task Import_ShouldDedupeWithinBatch_LastWins()
    {
        await AddBrandAsync(OkkoId, "okko");
        var json = """
        [
          { "id": "okko-1", "BrandId": "okko", "name": "First", "lat": 50.45, "lng": 30.52 },
          { "id": "okko-1", "BrandId": "okko", "name": "Second", "lat": 50.45, "lng": 30.52 }
        ]
        """;

        var result = await new ImportBrandStationsCommandHandler(_context)
            .HandleAsync(new ImportBrandStationsCommand(json, BrandStationImportFormat.Json));

        result.Added.Should().Be(1);
        (await ByExternalIdAsync("okko-1"))!.Name.Should().Be("Second");
    }

    [Fact]
    public async Task Import_ShouldParseCsv_WithQuotedCommaInAddress()
    {
        await AddBrandAsync(OkkoId, "okko");
        var csv = "id,BrandId,name,address,lat,lng\n" +
                  "okko-1,okko,Центр,\"Проспект Перемоги, 98\",50.45,30.52\n";

        var result = await new ImportBrandStationsCommandHandler(_context)
            .HandleAsync(new ImportBrandStationsCommand(csv, BrandStationImportFormat.Csv));

        result.Added.Should().Be(1);
        (await ByExternalIdAsync("okko-1"))!.Address.Should().Be("Проспект Перемоги, 98");
    }

    [Fact]
    public void Parser_ShouldReportError_ForInvalidCsvCoordinate()
    {
        var csv = "id,BrandId,name,lat,lng\nokko-1,okko,Центр,not-a-number,30.52\n";
        var parsed = BrandStationImportParser.Parse(csv, BrandStationImportFormat.Csv);

        parsed.Rows.Should().BeEmpty();
        parsed.Errors.Should().ContainSingle().Which.Line.Should().Be(2);
    }

    [Fact]
    public void Parser_ShouldReportError_ForMalformedJson()
    {
        var parsed = BrandStationImportParser.Parse("{ not json", BrandStationImportFormat.Json);
        parsed.Errors.Should().NotBeEmpty();
    }

    /// <summary>
    /// By the importer's key. The station's own uuid is the server's to assign, so a test that
    /// knows only the file's id has to ask for it by the column that carries that id.
    /// </summary>
    private Task<BrandStation?> ByExternalIdAsync(string externalId)
        => _context.BrandStations.SingleOrDefaultAsync(n => n.ExternalId == externalId);

    private async Task AddBrandAsync(Guid id, string slug)
    {
        _context.Brands.Add(new Brand
        {
            Id = id,
            // A brand row without a slug is refused by the database now, and the slug is also what
            // the import resolves - so the fixture states it rather than deriving one.
            Slug = slug,
            Name = slug.ToUpperInvariant(),
            Color = "#000000",
            LogoText = slug.ToUpperInvariant(),
        });
        await _context.SaveChangesAsync();
    }

    private static BrandStation Node(string id, Guid brandId, string name, double lat, double lng, string? city = null) =>
        new()
        {
            Id = NodeId(id),
            ExternalId = id,
            BrandId = brandId,
            Name = name,
            Lat = lat,
            Lng = lng,
            City = city,
        };
}
