using FluentAssertions;
using FuelFlow.Features.Providers;
using FuelFlow.Features.Vouchers;
using FuelFlow.Features.Vouchers.PurchaseBatchCost;
using FuelFlow.Features.Vouchers.SharedModels;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using FuelFlow.SharedKernel.Observability;
using FuelFlow.SharedKernel.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace FuelFlow.UnitTests.Vouchers;

public sealed class PurchaseBatchCostHandlerTests : IDisposable
{
    private static readonly Guid AdminId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");

    private readonly ApplicationDbContext _context;
    private readonly BlendedCostRecalculator _recalculator;
    private readonly ProviderEventService _eventService;
    private readonly SetBatchCostCommandHandler _setHandler;
    private readonly GetImportBatchCostsQueryHandler _getHandler;

    public PurchaseBatchCostHandlerTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        _context = new ApplicationDbContext(options);

        // okko-dp fuel with two nominals; margin 2 UAH/L, no pump (so final = blended cost + 2).
        _context.FuelTypes.Add(new FuelTypeEntity { Id = "okko-dp", Name = "ДП ЄВРО", StationId = "okko", BasePrice = 55, DiscountPrice = 52, CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow });
        _context.FuelPackages.AddRange(
            NewPackage("okko-dp", 10m),
            NewPackage("okko-dp", 20m));
        _context.SaveChanges();

        _recalculator = new BlendedCostRecalculator(_context);
        _eventService = new ProviderEventService(_context);
        _setHandler = new SetBatchCostCommandHandler(_context, _recalculator, _eventService, NotificationDispatcher.Disabled);
        _getHandler = new GetImportBatchCostsQueryHandler(_context, _recalculator);
    }

    private static FuelPackage NewPackage(string fuelTypeId, decimal liters) => new()
    {
        Id = Guid.NewGuid().ToString(),
        StationId = "okko",
        FuelTypeId = fuelTypeId,
        FuelName = "ДП ЄВРО",
        Liters = liters,
        Price = 0,
        OriginalPrice = 0,
        MarginUahPerLiter = 2m,
        CreatedAtUtc = DateTime.UtcNow,
        UpdatedAtUtc = DateTime.UtcNow
    };

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

    private void SeedVoucher(Guid importId, string fuelTypeId, decimal liters, VoucherStatus status = VoucherStatus.Imported, string provider = "OKKO")
    {
        _context.FuelVouchers.Add(new FuelVoucher
        {
            Id = Guid.NewGuid(),
            Provider = provider,
            FuelTypeId = fuelTypeId,
            Liters = liters,
            ExpirationDate = DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(1)),
            VoucherNumber = $"V-{Guid.NewGuid().ToString()[..8]}",
            QrPayload = Guid.NewGuid().ToString(),
            Status = status,
            ImportJobId = importId,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        });
    }

    public void Dispose()
    {
        _context.Database.EnsureDeleted();
        _context.Dispose();
    }

    private SetBatchCostCommand Cmd(Guid importId, string fuelTypeId, decimal cost) =>
        new(importId, fuelTypeId, cost, AdminId, "Admin User");

    // ── SetBatchCostCommandHandler ───────────────────────────────────────────

    [Fact]
    public async Task SetCost_CreatesBatch_RepricesPackages_AndRecordsEvent()
    {
        var import = SeedImport();
        SeedVoucher(import, "okko-dp", 100m);
        SeedVoucher(import, "okko-dp", 100m);
        await _context.SaveChangesAsync();

        var result = await _setHandler.HandleAsync(Cmd(import, "okko-dp", 25m));

        result.Success.Should().BeTrue();
        result.NotFound.Should().BeFalse();
        result.BlendedCostPerLiter.Should().Be(25m);   // (100×25 + 100×25) / 200
        result.PackagesRepriced.Should().Be(2);

        var batch = await _context.PurchaseBatches.SingleAsync();
        batch.ImportJobId.Should().Be(import);
        batch.FuelTypeId.Should().Be("okko-dp");
        batch.Provider.Should().Be("OKKO");
        batch.CostPerLiter.Should().Be(25m);
        batch.EnteredByUserId.Should().Be(AdminId);

        var packages = await _context.FuelPackages.Where(p => p.FuelTypeId == "okko-dp").OrderBy(p => p.Liters).ToListAsync();
        packages.Should().OnlyContain(p => p.SupplierPricePerLiter == 25m);
        packages.Should().OnlyContain(p => p.FinalPricePerLiter == 27m);   // 25 cost + 2 margin
        packages[0].Price.Should().Be(270);   // 27 × 10
        packages[1].Price.Should().Be(540);   // 27 × 20

        var fuel = await _context.FuelTypes.FirstAsync(f => f.Id == "okko-dp");
        fuel.DiscountPrice.Should().Be(27);   // final/L rounded
        fuel.BasePrice.Should().Be(27);       // pump null ⇒ final + minDiscount(0)

        _context.ProviderEventOutbox.Should().ContainSingle(e => e.EventType == "BatchCostEntered");
    }

    [Fact]
    public async Task SetCost_Upsert_UpdatesInPlace_NoDuplicateRow()
    {
        var import = SeedImport();
        SeedVoucher(import, "okko-dp", 100m);
        await _context.SaveChangesAsync();

        await _setHandler.HandleAsync(Cmd(import, "okko-dp", 25m));
        var result = await _setHandler.HandleAsync(Cmd(import, "okko-dp", 30m));

        result.Success.Should().BeTrue();
        result.BlendedCostPerLiter.Should().Be(30m);

        var batch = await _context.PurchaseBatches.SingleAsync();   // still one row for the (import × fuel) pair
        batch.CostPerLiter.Should().Be(30m);

        var package = await _context.FuelPackages.FirstAsync(p => p.FuelTypeId == "okko-dp");
        package.SupplierPricePerLiter.Should().Be(30m);
        package.FinalPricePerLiter.Should().Be(32m);
    }

    [Fact]
    public async Task SetCost_BlendsMovingAverageAcrossImports()
    {
        // import1 already costed at 20 (100 L in stock); now cost import2 at 30 (100 L in stock).
        var import1 = SeedImport();
        var import2 = SeedImport();
        SeedVoucher(import1, "okko-dp", 100m);
        SeedVoucher(import2, "okko-dp", 100m);
        _context.PurchaseBatches.Add(new PurchaseBatch
        {
            Id = Guid.NewGuid(),
            ImportJobId = import1,
            FuelTypeId = "okko-dp",
            Provider = "OKKO",
            CostPerLiter = 20m,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        });
        await _context.SaveChangesAsync();

        var result = await _setHandler.HandleAsync(Cmd(import2, "okko-dp", 30m));

        result.BlendedCostPerLiter.Should().Be(25m);   // (100×20 + 100×30) / 200
        var package = await _context.FuelPackages.FirstAsync(p => p.FuelTypeId == "okko-dp");
        package.SupplierPricePerLiter.Should().Be(25m); // priced off blended, not the just-entered 30
        package.FinalPricePerLiter.Should().Be(27m);
    }

    [Fact]
    public async Task SetCost_SoldStockLeavesThePool()
    {
        // 100 L still in stock @ import1(20) + 100 L Assigned (sold) @ import2(30). Only the in-stock
        // 100 L @ 20 should drive blended — the sold voucher's liters have left the pool.
        var import1 = SeedImport();
        var import2 = SeedImport();
        SeedVoucher(import1, "okko-dp", 100m, VoucherStatus.Imported);
        SeedVoucher(import2, "okko-dp", 100m, VoucherStatus.Assigned);
        _context.PurchaseBatches.Add(new PurchaseBatch
        {
            Id = Guid.NewGuid(),
            ImportJobId = import2,
            FuelTypeId = "okko-dp",
            Provider = "OKKO",
            CostPerLiter = 30m,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        });
        await _context.SaveChangesAsync();

        var result = await _setHandler.HandleAsync(Cmd(import1, "okko-dp", 20m));

        result.BlendedCostPerLiter.Should().Be(20m);
    }

    [Fact]
    public async Task SetCost_NonPositive_IsRejected()
    {
        var import = SeedImport();
        SeedVoucher(import, "okko-dp", 100m);
        await _context.SaveChangesAsync();

        var result = await _setHandler.HandleAsync(Cmd(import, "okko-dp", 0m));

        result.Success.Should().BeFalse();
        result.NotFound.Should().BeFalse();
        result.Error.Should().Contain("greater than zero");
        (await _context.PurchaseBatches.AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task SetCost_UnknownImport_ReturnsNotFound()
    {
        var result = await _setHandler.HandleAsync(Cmd(Guid.NewGuid(), "okko-dp", 25m));

        result.Success.Should().BeFalse();
        result.NotFound.Should().BeTrue();
    }

    [Fact]
    public async Task SetCost_FuelNotInImport_ReturnsNotFound()
    {
        var import = SeedImport();
        SeedVoucher(import, "okko-dp", 100m);
        await _context.SaveChangesAsync();

        var result = await _setHandler.HandleAsync(Cmd(import, "okko-95", 25m));

        result.Success.Should().BeFalse();
        result.NotFound.Should().BeTrue();
    }

    // ── Slice-3 emergent below-cost alert ────────────────────────────────────

    [Fact]
    public async Task SetCost_BelowCostNotOptedIn_FiresCriticalAlert()
    {
        // A pump ceiling of 49 − 0.5 = 48.5 binds below the entered cost 50, and the fuel is
        // NOT opted in → the reprice pushes it below cost, so an emergent (Critical) alert fires.
        _context.FuelTypes.Add(new FuelTypeEntity { Id = "okko-loss", Name = "ДП збиток", StationId = "okko", BasePrice = 49, DiscountPrice = 49, AllowBelowCost = false, CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow });
        _context.FuelPackages.Add(new FuelPackage
        {
            Id = Guid.NewGuid().ToString(), StationId = "okko", FuelTypeId = "okko-loss", FuelName = "ДП збиток",
            Liters = 10m, Price = 0, OriginalPrice = 0,
            MarginUahPerLiter = 2m, PumpPricePerLiter = 49m, MinDiscountPerLiter = 0.5m,
            CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow
        });
        _context.SaveChanges();

        var import = SeedImport();
        SeedVoucher(import, "okko-loss", 100m);
        await _context.SaveChangesAsync();

        var alerts = new Mock<IAlertNotifier>();
        var dispatcher = new NotificationDispatcher(
            alerts.Object,
            Options.Create(new TelegramOptions { Notifications = { NotifyOnBelowCost = true } }),
            NullLogger<NotificationDispatcher>.Instance);
        var handler = new SetBatchCostCommandHandler(_context, _recalculator, _eventService, dispatcher);

        var result = await handler.HandleAsync(Cmd(import, "okko-loss", 50m));

        result.Success.Should().BeTrue();
        alerts.Verify(a => a.SendAsync(
            AlertSeverity.Critical, It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<IReadOnlyDictionary<string, string>>(), false, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task SetCost_AboveCost_DoesNotAlert()
    {
        // okko-dp packages have no pump ceiling → final = cost + margin, always above cost.
        var alerts = new Mock<IAlertNotifier>();
        var dispatcher = new NotificationDispatcher(
            alerts.Object,
            Options.Create(new TelegramOptions { Notifications = { NotifyOnBelowCost = true } }),
            NullLogger<NotificationDispatcher>.Instance);
        var handler = new SetBatchCostCommandHandler(_context, _recalculator, _eventService, dispatcher);

        var import = SeedImport();
        SeedVoucher(import, "okko-dp", 100m);
        await _context.SaveChangesAsync();

        await handler.HandleAsync(Cmd(import, "okko-dp", 25m));

        alerts.Verify(a => a.SendAsync(
            It.IsAny<AlertSeverity>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<IReadOnlyDictionary<string, string>>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ── GetImportBatchCostsQueryHandler ──────────────────────────────────────

    [Fact]
    public async Task GetBatchCosts_RollsUpPerFuel_WithCostAndBlended()
    {
        var import = SeedImport();
        SeedVoucher(import, "okko-dp", 100m);
        SeedVoucher(import, "okko-dp", 50m);
        await _context.SaveChangesAsync();
        await _setHandler.HandleAsync(Cmd(import, "okko-dp", 25m));

        var rows = await _getHandler.HandleAsync(new GetImportBatchCostsQuery(import));

        var row = rows.Should().ContainSingle().Subject;
        row.FuelTypeId.Should().Be("okko-dp");
        row.FuelTypeName.Should().Be("ДП ЄВРО");
        row.Provider.Should().Be("OKKO");
        row.VoucherCount.Should().Be(2);
        row.TotalLiters.Should().Be(150m);
        row.CostPerLiter.Should().Be(25m);
        row.BlendedCostPerLiter.Should().Be(25m);
    }

    [Fact]
    public async Task GetBatchCosts_UncostedFuel_HasNullCost()
    {
        var import = SeedImport();
        SeedVoucher(import, "okko-dp", 100m);
        await _context.SaveChangesAsync();

        var rows = await _getHandler.HandleAsync(new GetImportBatchCostsQuery(import));

        var row = rows.Should().ContainSingle().Subject;
        row.CostPerLiter.Should().BeNull();
        row.BlendedCostPerLiter.Should().BeNull();   // nothing costed yet
    }
}
