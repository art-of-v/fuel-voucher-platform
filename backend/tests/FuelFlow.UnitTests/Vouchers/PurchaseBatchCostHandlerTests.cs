using FuelFlow.Features.Pricing;
using FluentAssertions;
using FuelFlow.Features.Brands;
using FuelFlow.Features.Vouchers;
using FuelFlow.Features.Vouchers.PurchaseBatchCost;
using FuelFlow.Features.Vouchers.SharedModels;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using FuelFlow.SharedKernel.Observability;
using FuelFlow.SharedKernel.Options;
using FuelFlow.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using static FuelFlow.SharedKernel.Domain.FuelIdMapper;

namespace FuelFlow.UnitTests.Vouchers;

public sealed class PurchaseBatchCostHandlerTests : IDisposable
{
    private static readonly Guid AdminId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");

    /// <summary>The counterparty every seeded voucher is stamped with — an import is one supplier's delivery.</summary>
    private readonly Guid SupplierId = Guid.Parse("11111111-2222-3333-4444-555555555555");

    private readonly ApplicationDbContext _context;
    private readonly BlendedCostRecalculator _recalculator;
    private readonly AuditEventService _eventService;
    private readonly SetBatchCostCommandHandler _setHandler;
    private readonly GetImportBatchCostsQueryHandler _getHandler;

    public PurchaseBatchCostHandlerTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        _context = new ApplicationDbContext(options, TestQrCipher.Instance);

        // okko-dp fuel with two nominals; margin 2 UAH/L, no pump (so final = blended cost + 2).
        _context.FuelTypes.Add(new FuelTypeEntity { Id = ToUuid("okko-dp"), Name = "ДП ЄВРО", BrandId = BrandIds.Okko, CreatedAtUtc = TestClock.UtcNow, UpdatedAtUtc = TestClock.UtcNow });
        _context.FuelNominals.AddRange(
            NewPackage("okko-dp", 10m),
            NewPackage("okko-dp", 20m));
        _context.Suppliers.Add(new Supplier
        {
            EdrIpn = "9000000019",
            Id = SupplierId,
            Name = "ТОВ Постачальник",
            IsActive = true,
            CreatedAtUtc = TestClock.UtcNow,
            UpdatedAtUtc = TestClock.UtcNow
        });
        _context.SaveChanges();

        _recalculator = new BlendedCostRecalculator(_context, new PriceHistoryRecorder(_context));
        _eventService = new AuditEventService(_context);
        _setHandler = new SetBatchCostCommandHandler(_context, _recalculator, _eventService, NotificationDispatcher.Disabled);
        _getHandler = new GetImportBatchCostsQueryHandler(_context, _recalculator);
    }

    /// <summary>
    /// Seeds a volume plus the offer behind it. Returns the volume rather than adding it, so the
    /// <c>AddRange</c> in the constructor keeps working.
    /// </summary>
    /// <remarks>
    /// The blended-cost recalculator reprices OFFERS now, so a fixture left in fuel_packages would let
    /// every repricing assertion read a zero cost and the fuel card report it healthy.
    /// </remarks>
private FuelNominal NewPackage(string fuelTypeId, decimal liters)
    {
    _context.SeedOffer(BrandIds.Okko, fuelTypeId, marginUahPerLiter: 2m);
    return new FuelNominal
    {
    FuelTypeId = ToUuid(fuelTypeId),
    Liters = liters,
    // The size is all a package is now. It carries no price of its own: the nominal follows its
    // fuel's rate, which is what this suite is about - the cost entry moves the offer's rate and
    // every size prices move with it.
    CreatedAtUtc = TestClock.UtcNow,
    UpdatedAtUtc = TestClock.UtcNow,
    };
    }

    private Guid SeedImport()
    {
        var id = Guid.NewGuid();
        _context.VoucherImports.Add(new VoucherImport
        {
            Id = id,
            FileName = "batch.pdf",
            CompletedAtUtc = TestClock.UtcNow,
            Status = "Completed",
            StartedAtUtc = TestClock.UtcNow
        });
        return id;
    }

    private void SeedVoucher(Guid importId, string fuelTypeId, decimal liters, VoucherStatus status = VoucherStatus.Imported, string brand = "okko", decimal? costPerLiter = null, Guid? supplierId = null)
    {
        _context.FuelVouchers.Add(new FuelVoucher
        {
            Id = Guid.NewGuid(),
            BrandId = BrandIds.ForSlug(brand),
            Brand = brand,
            FuelTypeId = ToUuid(fuelTypeId),
            Liters = liters,
            SupplierExpirationDate = DateOnly.FromDateTime(TestClock.UtcNow.AddMonths(1)),
            CustomerExpirationDate = DateOnly.FromDateTime(TestClock.UtcNow.AddMonths(1)),
            VoucherNumber = $"V-{Guid.NewGuid().ToString()[..8]}",
            QrPayload = Guid.NewGuid().ToString(),
            Status = status,
            ImportJobId = importId,
            CostPerLiter = costPerLiter,
            SupplierId = supplierId ?? SupplierId,
            CreatedAtUtc = TestClock.UtcNow,
            UpdatedAtUtc = TestClock.UtcNow
        });
    }

    private Guid SeedVoucherWithId(Guid importId, string fuelTypeId, decimal liters, decimal? costPerLiter = null)
    {
        var id = Guid.NewGuid();
        _context.FuelVouchers.Add(new FuelVoucher
        {
            Id = id,
            BrandId = BrandIds.ForSlug("okko"),
            Brand = "okko",
            FuelTypeId = ToUuid(fuelTypeId),
            Liters = liters,
            SupplierExpirationDate = DateOnly.FromDateTime(TestClock.UtcNow.AddMonths(1)),
            CustomerExpirationDate = DateOnly.FromDateTime(TestClock.UtcNow.AddMonths(1)),
            VoucherNumber = $"V-{id.ToString()[..8]}",
            QrPayload = Guid.NewGuid().ToString(),
            Status = VoucherStatus.Imported,
            ImportJobId = importId,
            CostPerLiter = costPerLiter,
            SupplierId = SupplierId,
            CreatedAtUtc = TestClock.UtcNow,
            UpdatedAtUtc = TestClock.UtcNow
        });
        return id;
    }

    public void Dispose()
    {
        _context.Database.EnsureDeleted();
        _context.Dispose();
    }

    private SetBatchCostCommand Cmd(Guid importId, string fuelTypeId, decimal cost) =>
        new(importId, ToUuid(fuelTypeId), cost, AdminId, "Admin User");

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
        batch.FuelTypeId.Should().Be(ToUuid("okko-dp"));
        batch.Brand.Should().Be("okko");
        batch.SupplierId.Should().Be(SupplierId);   // one PDF = one supplier's delivery
        batch.EnteredByUserId.Should().Be(AdminId);

        // Cost lives on the vouchers, not on the batch.
        var costed = await _context.FuelVouchers
            .Where(v => v.ImportJobId == import)
            .Select(v => v.CostPerLiter)
            .ToListAsync();
        costed.Should().AllBeEquivalentTo(25m);
        result.VouchersCosted.Should().Be(2);

        // Cost and final price are the FUEL's, written once; the per-package totals are the volumes'.
        // Asserting on the volumes alone would pass even if every per-litre figure stayed at zero,
        // which is precisely what this test exists to catch.
        var fuel = await _context.FuelTypes.FirstAsync(f => f.Id == ToUuid("okko-dp"));
        fuel.SupplierPricePerLiter.Should().Be(25m);
        fuel.FinalPerLiter().Should().Be(27m);   // 25 cost + 2 margin

        var sizes = await _context.FuelNominals
            .Where(v => v.FuelTypeId == ToUuid("okko-dp")).OrderBy(v => v.Liters).ToListAsync();
        // Nothing per size any more: the cost moved the fuel's rate, and every nominal follows it.
        sizes.Should().HaveCount(2);
        // The price the customer pays is COMPUTED from the knobs, so the recalculator writes the
        // cost and the margin and the price follows them - there is no stored copy left to keep in
        // step with anything.
        fuel.SupplierPricePerLiter.Should().Be(25);
        fuel.MarginUahPerLiter.Should().Be(2);
        fuel.FinalPerLiter().Should().Be(27);
        _context.AuditEventOutbox.Should().ContainSingle(e => e.EventType == "BatchCostEntered");
    }

    [Fact]
    public async Task SetCost_AppendsTheNewCost_ToThePriceHistory()
    {
        var import = SeedImport();
        SeedVoucher(import, "okko-dp", 100m);
        SeedVoucher(import, "okko-dp", 100m);
        await _context.SaveChangesAsync();

        await _setHandler.HandleAsync(Cmd(import, "okko-dp", 25m));

    var fuel = await _context.FuelTypes.FirstAsync(f => f.Id == ToUuid("okko-dp"));
    var history = await _context.FuelPriceHistory
        .Where(p => p.FuelTypeId == fuel.Id)
            .ToListAsync();

        // A batch cost is a price change like any other, and it arrives through a door of its own: an
        // exchange releasing expired stock moves the same cost through `exchange-release`. The history
        // row is the only thing that says which happened.
        history.Should().HaveCount(1);
        history[0].SupplierPricePerLiter.Should().Be(25m);
        history[0].Reason.Should().Be(PriceHistoryReasons.BatchCost);
        history[0].ChangedByUserId.Should().Be(AdminId);
    }

    [Fact]
    public async Task SetCost_RestampingWithoutOverwriteIsRefused()
    {
        var import = SeedImport();
        SeedVoucher(import, "okko-dp", 100m);
        await _context.SaveChangesAsync();

        await _setHandler.HandleAsync(Cmd(import, "okko-dp", 25m));
        var second = await _setHandler.HandleAsync(Cmd(import, "okko-dp", 30m));

        second.Success.Should().BeFalse();
        second.Error.Should().Contain("already costed");

        var voucher = await _context.FuelVouchers.FirstAsync(v => v.ImportJobId == import);
        voucher.CostPerLiter.Should().Be(25m);
    }

    [Fact]
    public async Task SetCost_RefusalExplainsTheRealReasonNotTheReversedOne()
    {
        var import = SeedImport();
        SeedVoucher(import, "okko-dp", 100m);
        await _context.SaveChangesAsync();

        await _setHandler.HandleAsync(Cmd(import, "okko-dp", 25m));
        var second = await _setHandler.HandleAsync(Cmd(import, "okko-dp", 30m));

        // What carries an already-costed voucher is the uplift an exchange applied. Customer payments
        // stopped moving cost in e56d5bee, so blaming them here tells the operator a falsehood about
        // their own batch, and sends them looking for customer money that is not in the price.
        second.Error.Should().Contain("exchange");
        second.Error.Should().NotContain("customers paid");
    }

    /// <summary>
    /// A second price for the same batch is refused, and the first one stands. This used to be the
    /// opposite test: it drove the <c>ForceOverwrite</c> flag and asserted that a second entry restamped
    /// the voucher and repriced the pool. The flag is gone — what a lot cost is a purchase fact, and a
    /// correction is a new recorded entry rather than a rewrite of vouchers already bought (CODEBASE.md
    /// `purchase_batches`, planning #360).
    /// </summary>
    [Fact]
    public async Task SetCost_ASecondPriceForTheSameBatch_IsRefusedAndChangesNothing()
    {
        var import = SeedImport();
        SeedVoucher(import, "okko-dp", 100m);
        await _context.SaveChangesAsync();

        await _setHandler.HandleAsync(Cmd(import, "okko-dp", 25m));
        var result = await _setHandler.HandleAsync(Cmd(import, "okko-dp", 30m));

        result.Success.Should().BeFalse("the batch is already costed, and its price is not ours to rewrite");
        result.VouchersCosted.Should().Be(0);

        var voucher = await _context.FuelVouchers.FirstAsync(v => v.ImportJobId == import);
        voucher.CostPerLiter.Should().Be(25m, "the bought price is exactly what it was");

        // Still one grouping row for the (import × fuel) pair.
        (await _context.PurchaseBatches.CountAsync()).Should().Be(1);
    }

    /// <summary>
    /// A price correction may restamp a voucher's cost, but it must not invent a value for one that
    /// already has one — that reduction is the only record that a customer paid for that fuel.
    /// </summary>
    [Fact]
    public async Task SetCost_LeavesAlreadyCostedVouchersAlone()
    {
        var import = SeedImport();
        var reduced = SeedVoucherWithId(import, "okko-dp", 100m, costPerLiter: 25m);
        var uncosted = SeedVoucherWithId(import, "okko-dp", 100m);
        await _context.SaveChangesAsync();

        var result = await _setHandler.HandleAsync(Cmd(import, "okko-dp", 30m));

        result.Success.Should().BeTrue();
        result.VouchersCosted.Should().Be(1);   // only the uncosted one

        var costed = await _context.FuelVouchers.Where(v => v.ImportJobId == import).ToDictionaryAsync(v => v.Id);
        costed[reduced].CostPerLiter.Should().Be(25m);
        costed[uncosted].CostPerLiter.Should().Be(30m);
        result.BlendedCostPerLiter.Should().Be(27.5m);   // (100×25 + 100×30) / 200
    }

    [Fact]
    public async Task SetCost_BlendsMovingAverageAcrossImports()
    {
        // import1 already costed at 20 (100 L in stock); now cost import2 at 30 (100 L in stock).
        var import1 = SeedImport();
        var import2 = SeedImport();
        SeedVoucher(import1, "okko-dp", 100m, costPerLiter: 20m);
        SeedVoucher(import2, "okko-dp", 100m);
        await _context.SaveChangesAsync();

        var result = await _setHandler.HandleAsync(Cmd(import2, "okko-dp", 30m));

        result.BlendedCostPerLiter.Should().Be(25m);   // (100×20 + 100×30) / 200
    var fuel = await _context.FuelTypes.FirstAsync(f => f.Id == ToUuid("okko-dp"));
    fuel.SupplierPricePerLiter.Should().Be(25m); // priced off blended, not the just-entered 30
    fuel.FinalPerLiter().Should().Be(27m);
    }

    /// <summary>
    /// Only stock counts. The in-stock voucher's litres are entered by this call, the Assigned
    /// (sold) one's were costed by an earlier one, and only the former may drive the average.
    /// <para>
    /// The in-stock voucher is seeded <em>uncosted</em> on purpose. This test used to drive
    /// <c>ForceOverwrite</c> to restamp it, which meant it only passed while a flag existed whose
    /// whole purpose was rewriting a bought price — and removing that flag left it asserting nothing
    /// about the pool it claims to test.
    /// </para>
    /// </summary>
    [Fact]
    public async Task SetCost_SoldStockLeavesThePool()
    {
        // 100 L still in stock @ import1, entered here at 20, + 100 L Assigned (sold) @ import2(30).
        // Only the in-stock 100 L @ 20 should drive blended — the sold voucher's liters have left it.
        var import1 = SeedImport();
        var import2 = SeedImport();
        SeedVoucher(import1, "okko-dp", 100m, VoucherStatus.Imported);
        SeedVoucher(import2, "okko-dp", 100m, VoucherStatus.Assigned, costPerLiter: 30m);
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
    [Fact]
    public async Task SetCost_AboveCost_DoesNotAlert()
    {
        // okko-dp packages have no column price → final = cost + margin, always above cost.
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
        row.FuelTypeId.Should().Be(ToUuid("okko-dp"));
        row.FuelTypeName.Should().Be("ДП ЄВРО");
        row.Brand.Should().Be("okko");
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

    /// <summary>
    /// The margin a fuel sells at, read from the fuel - the one place it lives.
    /// </summary>
    private async Task<decimal?> MarginFor(FuelTypeEntity fuel)
        => await _context.FuelTypes
            .Where(f => f.Id == fuel.Id)
            .Select(f => f.MarginUahPerLiter)
            .FirstOrDefaultAsync();

}
