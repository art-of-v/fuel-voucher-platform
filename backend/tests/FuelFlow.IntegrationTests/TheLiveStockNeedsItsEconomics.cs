using FluentAssertions;
using FuelFlow.Features.Brands;
using FuelFlow.Features.Brands.AdminUpdateFuel;
using FuelFlow.Features.Pricing;
using FuelFlow.Features.Vouchers;
using FuelFlow.Features.Vouchers.BulkActionVouchers;
using FuelFlow.Features.Vouchers.GetImportBatches;
using FuelFlow.Features.Vouchers.SharedModels;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using FuelFlow.Testing;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static FuelFlow.SharedKernel.Domain.FuelIdMapper;

namespace FuelFlow.IntegrationTests;

/// <summary>
/// Planning #680: live stock cannot go on the shelf while its fuel has no economics of its own.
/// </summary>
/// <remarks>
/// <para>
/// The price of a litre is what we pay plus our margin, so a fuel without both has no price to sell
/// at. The pricing engine already answered null for that case and checkout refused the sale - but the
/// refusal arrived at the checkout, in front of a customer, on a shelf that said the fuel was there.
/// </para>
/// <para>
/// So the question is asked earlier, where the answer can still be acted on: before stock goes on the
/// shelf, and before an operator takes the economics away from a fuel that live stock already sits on.
/// These are the two doors; the rule behind them is <see cref="FuelEconomics"/>.
/// </para>
/// </remarks>
[Collection("Integration Tests")]
public sealed class TheLiveStockNeedsItsEconomics : IClassFixture<TestDatabaseFixture>, IAsyncLifetime
{
    private readonly TestDatabaseFixture _fixture;
    private decimal? _costBefore;
    private decimal? _marginBefore;

    public TheLiveStockNeedsItsEconomics(TestDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    /// <summary>
    /// The seeded fuel's own economics, kept so every test can put them back. The table is not
    /// truncated between tests, so a test that left the fuel priced would decide the outcome of the
    /// test that ran after it. The stock is cleared for the same reason: a test that asks about a
    /// fuel with nothing on the shelf must find a fuel with nothing on the shelf.
    /// </summary>
    public async Task InitializeAsync()
    {
        using var ctx = CreateContext();
        var fuel = await ctx.FuelTypes.AsNoTracking().FirstAsync(f => f.Id == ToUuid("okko-95"));
        _costBefore = fuel.SupplierPricePerLiter;
        _marginBefore = fuel.MarginUahPerLiter;

        await ResetDataAsync(ctx);

        ctx.Users.Add(new User
        {
            Id = SeededUserId,
            PhoneNumber = "+380991234567",
            FirstName = "Op",
            LastName = "Erator",
            IsActive = true,
            CreatedAtUtc = TestClock.UtcNow,
            UpdatedAtUtc = TestClock.UtcNow
        });
        await ctx.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        await SetFuelEconomicsAsync(_costBefore, _marginBefore);
    }

    // ---------------------------------------------------------------------------------------------
    // The shelf
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public async Task Live_stock_cannot_be_activated_while_its_fuel_has_no_margin()
    {
        await SetFuelEconomicsAsync(50m, null);
        var voucherId = Guid.NewGuid();
        await SeedStockAsync(voucherId, DataKinds.Real, costPerLiter: 50m);

        using var ctx = CreateContext();
        var result = await BulkHandler(ctx)
            .HandleAsync(new BulkActionVouchersCommand("activate", [voucherId], null));

        result.Success.Should().BeFalse("a fuel with no margin has no price to sell at");
        result.Error.Should().Contain("no margin");

        using var verify = CreateContext();
        (await verify.FuelVouchers.AsNoTracking().FirstAsync(v => v.Id == voucherId)).Status
            .Should().Be(VoucherStatus.Imported, "the refused stock did not go on sale");
    }

    [Fact]
    public async Task Live_stock_cannot_be_activated_while_its_fuel_has_no_cost()
    {
        await SetFuelEconomicsAsync(null, 5m);
        var voucherId = Guid.NewGuid();
        await SeedStockAsync(voucherId, DataKinds.Real, costPerLiter: 50m);

        using var ctx = CreateContext();
        var result = await BulkHandler(ctx)
            .HandleAsync(new BulkActionVouchersCommand("activate", [voucherId], null));

        result.Success.Should().BeFalse("a fuel with no cost has no price to sell at");
        result.Error.Should().Contain("no supplier cost");

        using var verify = CreateContext();
        (await verify.FuelVouchers.AsNoTracking().FirstAsync(v => v.Id == voucherId)).Status
            .Should().Be(VoucherStatus.Imported, "the refused stock did not go on sale");
    }

    [Fact]
    public async Task Live_stock_goes_on_sale_once_its_fuel_has_a_cost_and_a_margin()
    {
        await SetFuelEconomicsAsync(50m, 5m);
        var voucherId = Guid.NewGuid();
        await SeedStockAsync(voucherId, DataKinds.Real, costPerLiter: 50m);

        using var ctx = CreateContext();
        var result = await BulkHandler(ctx)
            .HandleAsync(new BulkActionVouchersCommand("activate", [voucherId], null));

        result.Success.Should().BeTrue(result.Error);

        using var verify = CreateContext();
        (await verify.FuelVouchers.AsNoTracking().FirstAsync(v => v.Id == voucherId)).Status
            .Should().Be(VoucherStatus.Available);
    }

    [Fact]
    public async Task Rehearsal_stock_is_not_blocked_by_the_fuel_economics()
    {
        // The rehearsal pool is never sold to a real customer, so a rehearsal fuel is allowed to have
        // no economics at all. What must not happen is a LIVE fuel slipping through the same hole.
        await SetFuelEconomicsAsync(null, null);
        var voucherId = Guid.NewGuid();
        await SeedStockAsync(voucherId, DataKinds.Qa, costPerLiter: 50m);

        using var ctx = CreateContext();
        var result = await BulkHandler(ctx)
            .HandleAsync(new BulkActionVouchersCommand("activate", [voucherId], null));

        result.Success.Should().BeTrue(result.Error);

        using var verify = CreateContext();
        (await verify.FuelVouchers.AsNoTracking().FirstAsync(v => v.Id == voucherId)).Status
            .Should().Be(VoucherStatus.Available);
    }

    [Fact]
    public async Task The_whole_batch_activation_refuses_a_fuel_with_no_economics()
    {
        // The other door. The admin's "activate the whole batch" button is its own handler, and a
        // gate that stood in one place only would leave this one open.
        await SetFuelEconomicsAsync(50m, null);
        var importId = Guid.NewGuid();
        var voucherId = Guid.NewGuid();
        await SeedStockAsync(voucherId, DataKinds.Real, costPerLiter: 50m, importId: importId);

        using var ctx = CreateContext();
        var result = await new ImportBatchBulkActionQueryHandler(ctx)
            .HandleAsync(new ImportBatchBulkActionQuery(importId, "activate"));

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("no margin");
        result.Affected.Should().Be(0);

        using var verify = CreateContext();
        (await verify.FuelVouchers.AsNoTracking().FirstAsync(v => v.Id == voucherId)).Status
            .Should().Be(VoucherStatus.Imported, "the refused batch did not go on sale");
    }

    // ---------------------------------------------------------------------------------------------
    // The fuel card
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public async Task The_admin_cannot_take_the_economics_away_from_a_fuel_with_live_stock()
    {
        await SetFuelEconomicsAsync(50m, 5m);
        var voucherId = Guid.NewGuid();
        await SeedStockAsync(voucherId, DataKinds.Real, costPerLiter: 50m, status: VoucherStatus.Available);

        using var ctx = CreateContext();
        var result = await UpdateHandler(ctx)
            .HandleAsync(new AdminUpdateFuelCommand(
                ToUuid("okko-95"),
                new BrandFuelDto
                {
                    Id = ToUuid("okko-95").ToString(),
                    Name = "A-95",
                    SupplierPricePerLiter = null,
                    MarginUahPerLiter = null,
                    IsTestData = false,
                    AllowBelowCost = false
                },
                SeededUserId,
                "Admin"));

        result.Success.Should().BeFalse("the stock on the shelf would have had no price");
        result.Error.Should().Contain("live stock");

        using var verify = CreateContext();
        var fuel = await verify.FuelTypes.AsNoTracking().FirstAsync(f => f.Id == ToUuid("okko-95"));
        fuel.SupplierPricePerLiter.Should().Be(50m, "the refused edit wrote nothing");
        fuel.MarginUahPerLiter.Should().Be(5m, "the refused edit wrote nothing");
    }

    [Fact]
    public async Task The_admin_may_leave_a_fuel_without_economics_while_no_stock_sits_on_it()
    {
        // A fuel card with no stock behind it may be unpriced: it is a card nobody buys yet, and the
        // operator is often halfway through pricing it (the cost arrives with the batch, the margin
        // is set on the card).
        await SetFuelEconomicsAsync(50m, 5m);

        using var ctx = CreateContext();
        var result = await UpdateHandler(ctx)
            .HandleAsync(new AdminUpdateFuelCommand(
                ToUuid("okko-95"),
                new BrandFuelDto
                {
                    Id = ToUuid("okko-95").ToString(),
                    Name = "A-95",
                    SupplierPricePerLiter = null,
                    MarginUahPerLiter = null,
                    IsTestData = false,
                    AllowBelowCost = false
                },
                SeededUserId,
                "Admin"));

        result.Success.Should().BeTrue(result.Error);

        using var verify = CreateContext();
        var fuel = await verify.FuelTypes.AsNoTracking().FirstAsync(f => f.Id == ToUuid("okko-95"));
        fuel.SupplierPricePerLiter.Should().BeNull();
        fuel.MarginUahPerLiter.Should().BeNull();
    }

    [Fact]
    public async Task The_admin_is_refused_a_cost_of_zero()
    {
        // A cost of zero is not a cost: it is a litre we paid nothing for, and the below-cost guard
        // reads it as unknown cost - which would wave a sale through.
        await SetFuelEconomicsAsync(50m, 5m);

        using var ctx = CreateContext();
        var result = await UpdateHandler(ctx)
            .HandleAsync(new AdminUpdateFuelCommand(
                ToUuid("okko-95"),
                new BrandFuelDto
                {
                    Id = ToUuid("okko-95").ToString(),
                    Name = "A-95",
                    SupplierPricePerLiter = 0m,
                    MarginUahPerLiter = 5m,
                    IsTestData = false,
                    AllowBelowCost = false
                },
                SeededUserId,
                "Admin"));

        result.Success.Should().BeFalse();
        result.Field.Should().Be("SupplierPricePerLiter");
        result.Error.Should().Contain("greater than zero");
    }

    [Fact]
    public async Task The_admin_is_refused_a_negative_margin()
    {
        await SetFuelEconomicsAsync(50m, 5m);

        using var ctx = CreateContext();
        var result = await UpdateHandler(ctx)
            .HandleAsync(new AdminUpdateFuelCommand(
                ToUuid("okko-95"),
                new BrandFuelDto
                {
                    Id = ToUuid("okko-95").ToString(),
                    Name = "A-95",
                    SupplierPricePerLiter = 50m,
                    MarginUahPerLiter = -1m,
                    IsTestData = false,
                    AllowBelowCost = false
                },
                SeededUserId,
                "Admin"));

        result.Success.Should().BeFalse();
        result.Field.Should().Be("MarginUahPerLiter");
        result.Error.Should().Contain("negative");
    }

    [Fact]
    public async Task The_admin_is_refused_the_below_cost_opt_in_without_a_cost()
    {
        // The opt-in says a sale may go below what we paid. Without a cost there is no what-we-paid to
        // go below, so the opt-in is meaningless - and the database says so too
        // (ck_fuel_types_allow_below_cost_needs_a_cost).
        await SetFuelEconomicsAsync(50m, 5m);

        using var ctx = CreateContext();
        var result = await UpdateHandler(ctx)
            .HandleAsync(new AdminUpdateFuelCommand(
                ToUuid("okko-95"),
                new BrandFuelDto
                {
                    Id = ToUuid("okko-95").ToString(),
                    Name = "A-95",
                    SupplierPricePerLiter = null,
                    MarginUahPerLiter = 5m,
                    IsTestData = false,
                    AllowBelowCost = true
                },
                SeededUserId,
                "Admin"));

        result.Success.Should().BeFalse();
        result.Field.Should().Be("AllowBelowCost");
        result.Error.Should().Contain("cost");
    }

    // ---------------------------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------------------------

    private static readonly Guid SeededUserId = Guid.Parse("0199aaaa-bbbb-cccc-dddd-eeeeeeeeeeee");

    private static BulkActionVouchersCommandHandler BulkHandler(ApplicationDbContext ctx)
        => new(ctx, new RecordingJobClient(), new AuditEventService(ctx));

    private static AdminUpdateFuelCommandHandler UpdateHandler(ApplicationDbContext ctx)
        => new(ctx, new AuditEventService(ctx), new PriceHistoryRecorder(ctx));

    /// <summary>
    /// The fuel's own cost and margin, written the way the operator's card writes them.
    /// </summary>
    private async Task SetFuelEconomicsAsync(decimal? cost, decimal? margin)
    {
        using var ctx = CreateContext();
        await ctx.FuelTypes
            .Where(f => f.Id == ToUuid("okko-95"))
            .ExecuteUpdateAsync(s => s
                .SetProperty(f => f.SupplierPricePerLiter, cost)
                .SetProperty(f => f.MarginUahPerLiter, margin));
    }

    /// <summary>
    /// Stock waiting to be put on sale: costed, in the pool the test names, and - where the test says
    /// so - belonging to an import.
    /// </summary>
    private async Task SeedStockAsync(
        Guid id,
        string dataKind,
        decimal? costPerLiter,
        VoucherStatus status = VoucherStatus.Imported,
        Guid? importId = null)
    {
        using var ctx = CreateContext();
        await ctx.Database.MigrateAsync();

        if (importId is { } import)
        {
            ctx.VoucherImports.Add(new VoucherImport
            {
                Id = import,
                FileName = $"import-{import:N}.pdf",
                Status = "Completed",
                StartedAtUtc = TestClock.UtcNow,
                CompletedAtUtc = TestClock.UtcNow
            });
        }

        ctx.FuelVouchers.Add(new FuelVoucher
        {
            Id = id,
            BrandId = BrandIds.ForSlug("okko"),
            Brand = "okko",
            FuelTypeId = ToUuid("okko-95"),
            Liters = 10m,
            SupplierExpirationDate = DateOnly.FromDateTime(TestClock.UtcNow.AddMonths(6)),
            CustomerExpirationDate = DateOnly.FromDateTime(TestClock.UtcNow.AddMonths(6)),
            VoucherNumber = $"STK-{id:N}"[..16],
            QrPayload = $"qr-{id:N}",
            Status = status,
            DataKind = dataKind,
            ImportJobId = importId,
            CostPerLiter = costPerLiter,
            CreatedAtUtc = TestClock.UtcNow,
            UpdatedAtUtc = TestClock.UtcNow
        });

        await ctx.SaveChangesAsync();
    }

    private static async Task ResetDataAsync(ApplicationDbContext context)
    {
        await context.Database.ExecuteSqlRawAsync(
            """TRUNCATE TABLE "voucher_imports", "fuel_vouchers", "users", "provider_event_outbox" RESTART IDENTITY CASCADE""");
    }

    private ApplicationDbContext CreateContext()
        => new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(_fixture.ConnectionString)
            .Options, TestQrCipher.Instance);
}
