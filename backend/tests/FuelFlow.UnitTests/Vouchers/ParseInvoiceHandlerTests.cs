using FluentAssertions;
using FuelFlow.Features.Vouchers;
using FuelFlow.Features.Vouchers.ParseInvoice;
using FuelFlow.Features.Vouchers.SharedModels;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.UnitTests.Vouchers;

public sealed class ParseInvoiceHandlerTests : IDisposable
{
    private readonly ApplicationDbContext _context;
    private readonly ParseInvoiceCommandHandler _handler;

    public ParseInvoiceHandlerTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        _context = new ApplicationDbContext(options);

        _context.FuelTypes.AddRange(
            new FuelTypeEntity { Id = "okko-dp", Name = "Дизельне паливо ОККО", StationId = "okko", CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow },
            new FuelTypeEntity { Id = "okko-a95", Name = "Бензин А-95 ОККО", StationId = "okko", CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow });
        _context.SaveChanges();

        _handler = new ParseInvoiceCommandHandler(_context);
    }

    public void Dispose()
    {
        _context.Database.EnsureDeleted();
        _context.Dispose();
    }

    private Guid SeedImport()
    {
        var id = Guid.NewGuid();
        _context.VoucherImports.Add(new VoucherImport
        {
            Id = id,
            FileName = "batch.pdf",
            Status = "Completed",
            StartedAtUtc = DateTime.UtcNow
        });
        return id;
    }

    private void SeedVoucher(Guid importId, string fuelTypeId, decimal liters, string provider = "OKKO")
    {
        _context.FuelVouchers.Add(new FuelVoucher
        {
            Id = Guid.NewGuid(),
            Provider = provider,
            FuelTypeId = fuelTypeId,
            Liters = liters,
            ProviderExpirationDate = DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(1)),
            CustomerExpirationDate = DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(1)),
            VoucherNumber = $"V-{Guid.NewGuid().ToString()[..8]}",
            QrPayload = Guid.NewGuid().ToString(),
            Status = VoucherStatus.Imported,
            ImportJobId = importId,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        });
    }

    private static InvoiceLine Line(string label, decimal? cost = null, decimal? liters = null, decimal? total = null, string? code = null, int row = 2) =>
        new() { Line = row, Label = label, Code = code, CostPerLiter = cost, Liters = liters, Total = total };

    private ParseInvoiceCommand Cmd(Guid importId, params InvoiceLine[] lines) =>
        new(importId, lines, []);

    [Fact]
    public async Task UnknownImport_ReturnsNotFound()
    {
        var result = await _handler.HandleAsync(Cmd(Guid.NewGuid(), Line("Дизель", cost: 48m)));

        result.NotFound.Should().BeTrue();
        result.Dto.Should().BeNull();
    }

    [Fact]
    public async Task MatchesByExplicitCode()
    {
        var import = SeedImport();
        SeedVoucher(import, "okko-dp", 5000m);
        await _context.SaveChangesAsync();

        var result = await _handler.HandleAsync(Cmd(import, Line("будь-що", cost: 48.5m, liters: 5000m, code: "okko-dp")));

        var line = result.Dto!.Lines.Should().ContainSingle().Subject;
        line.Matched.Should().BeTrue();
        line.FuelTypeId.Should().Be("okko-dp");
        line.FuelTypeName.Should().Be("Дизельне паливо ОККО");
        line.Provider.Should().Be("OKKO");
        line.CostPerLiter.Should().Be(48.5m);
        line.ImportLiters.Should().Be(5000m);
        line.Warning.Should().BeNull();
        result.Dto.UnmatchedImportFuels.Should().BeEmpty();
    }

    [Fact]
    public async Task MatchesByFuelName_WhenNoCode()
    {
        var import = SeedImport();
        SeedVoucher(import, "okko-a95", 3000m);
        await _context.SaveChangesAsync();

        // Invoice writes the fuel plainly; the batch name is "Бензин А-95 ОККО".
        var result = await _handler.HandleAsync(Cmd(import, Line("Бензин А-95", cost: 52.3m, liters: 3000m)));

        var line = result.Dto!.Lines.Should().ContainSingle().Subject;
        line.Matched.Should().BeTrue();
        line.FuelTypeId.Should().Be("okko-a95");
    }

    [Fact]
    public async Task DerivesCostFromTotalDividedByQuantity_WhenNoPrice()
    {
        var import = SeedImport();
        SeedVoucher(import, "okko-dp", 1000m);
        await _context.SaveChangesAsync();

        var result = await _handler.HandleAsync(Cmd(import, Line("Дизельне паливо", total: 50000m, liters: 1000m)));

        var line = result.Dto!.Lines.Should().ContainSingle().Subject;
        line.CostPerLiter.Should().Be(50m);
        line.Warning.Should().Contain("derived");
    }

    [Fact]
    public async Task UnmatchedInvoiceLine_IsFlagged()
    {
        var import = SeedImport();
        SeedVoucher(import, "okko-dp", 1000m);
        await _context.SaveChangesAsync();

        var result = await _handler.HandleAsync(Cmd(import, Line("Газ пропан", cost: 25m, liters: 500m)));

        var line = result.Dto!.Lines.Should().ContainSingle().Subject;
        line.Matched.Should().BeFalse();
        line.FuelTypeId.Should().BeNull();
        line.CostPerLiter.Should().Be(25m);      // still surfaced so the operator can map it by hand
        line.Warning.Should().Contain("no fuel");
    }

    [Fact]
    public async Task ListsImportFuelsWithNoInvoiceLine()
    {
        var import = SeedImport();
        SeedVoucher(import, "okko-dp", 1000m);
        SeedVoucher(import, "okko-a95", 2000m);
        await _context.SaveChangesAsync();

        // Invoice only covers the diesel line.
        var result = await _handler.HandleAsync(Cmd(import, Line("Дизельне паливо", cost: 48m, liters: 1000m)));

        result.Dto!.UnmatchedImportFuels.Should().ContainSingle()
            .Which.Should().Be("Бензин А-95 ОККО");
    }

    [Fact]
    public async Task QuantityMismatch_AddsWarning_ButStillMatches()
    {
        var import = SeedImport();
        SeedVoucher(import, "okko-dp", 1000m);
        await _context.SaveChangesAsync();

        // Invoice says 900 L but the import holds 1000 L of this fuel (>1% off).
        var result = await _handler.HandleAsync(Cmd(import, Line("Дизельне паливо", cost: 48m, liters: 900m)));

        var line = result.Dto!.Lines.Should().ContainSingle().Subject;
        line.Matched.Should().BeTrue();
        line.Warning.Should().Contain("≠");
    }

    [Fact]
    public async Task ParseErrors_ArePassedThrough()
    {
        var import = SeedImport();
        SeedVoucher(import, "okko-dp", 1000m);
        await _context.SaveChangesAsync();

        var cmd = new ParseInvoiceCommand(import,
            [Line("Дизельне паливо", cost: 48m, liters: 1000m)],
            [new ParseInvoiceIssue(7, "'Мастило': no price or total to read a cost from")]);

        var result = await _handler.HandleAsync(cmd);

        result.Dto!.Errors.Should().ContainSingle(e => e.Line == 7);
    }
}
