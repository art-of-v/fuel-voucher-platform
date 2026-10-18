using FuelFlow.Features.Brands;
using FluentAssertions;
using FuelFlow.Features.Pricing;
using FuelFlow.Features.Pricing.ImportOkkoCatalogue;
using FuelFlow.Features.Pricing.ImportOkkoPumpPrices;
using FuelFlow.Features.Orders.SharedModels;
using FuelFlow.Features.Pricing.ScrapeOkkoPrices;
using FuelFlow.Features.Vouchers;
using FuelFlow.Features.Vouchers.SharedModels;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FuelFlow.IntegrationTests;

/// <summary>
/// The operator's archive rule, settled 2026-10-17: a fuel may be archived only when the brand no
/// longer publishes it <b>and</b> every one of its vouchers is spent or expired.
/// <para>
/// Both conditions are asserted, because either alone is a different bug. Absence from the sheet alone
/// would archive stock we have paid for — the failure #217 was opened for, where customers hold
/// vouchers pointing at a row the catalogue no longer shows. An idle fuel alone would retire a grade
/// the brand still sells, simply because we happen not to carry it today.
/// </para>
/// </summary>
[Collection("Integration Tests")]
public sealed class FuelArchiveRuleIntegrationTests : IClassFixture<TestDatabaseFixture>
{
    private readonly TestDatabaseFixture _fixture;

    public FuelArchiveRuleIntegrationTests(TestDatabaseFixture fixture) => _fixture = fixture;

    private ApplicationDbContext CreateContext()
        => new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(_fixture.ConnectionString)
            .Options, TestQrCipher.Instance);

    /// <summary>
    /// Every grade the migrated catalogue carries, so the seeded rows are "still published" and only
    /// this test's fuel is ever a candidate for archiving.
    /// </summary>
    private static readonly OkkoPumpPrice[] FullSheet =
    [
        new("A-95", 92.90m, "А-95"),
        new("Pulls 95", 95.90m, "95 PULLS"),
        new("DP", 102.90m, "ДП ЄВРО"),
        new("Pulls Diesel", 105.90m, "ДП PULLS (idle)"),
        new("SPBT", 50.90m, "ГАЗ"),
    ];

    /// <summary>The catalogue import over a sheet, the way the job calls it.</summary>
    private static async Task<OkkoCatalogueApplyResult> ImportAsync(
        ApplicationDbContext context, IReadOnlyList<OkkoPumpPrice> sheet)
    {
        var events = new Features.Brands.AuditEventService(context);
        var handler = new ImportOkkoCatalogueCommandHandler(context, new BrandSlugResolver(context), events, new OfferPriceRecorder(context));
        return await handler.ApplyAsync(new ImportOkkoCatalogueCommand(sheet, DryRun: false), Guid.Empty, "test");
    }

    private static async Task SeedFuelWithVouchersAsync(
        ApplicationDbContext context,
        string suffix,
        string name,
        VoucherStatus status)
    {
        var now = DateTime.UtcNow;
// Under the real OKKO brand: the catalogue import only ever looks at that one, so a fuel parked
        // elsewhere would be invisible to it and the test would pass for the wrong reason.
        var brandId = BrandIds.Okko;
        var fuelId = $"arch-fuel-{suffix}";
        context.FuelTypes.Add(new FuelTypeEntity
        {
            Id = fuelId, Name = name, BrandId = brandId,
            // A code the sheet does not carry: the brand published it once and does not any more.
            // Without a code there is nothing saying this row was ever one of the brand's grades,
            // and absence from the sheet would prove nothing. Unique per test because the live
            // (brand, code) index allows one, and every test in this class seeds a dropped grade.
            BrandFuelCode = $"Dropped-{suffix}",
            CreatedAtUtc = now, UpdatedAtUtc = now,
        });
// A spent or blocked voucher must point at an order (ck_voucher_held_has_order), so the "used"
        // case needs one. The FK is to a real order row, so a stub id would only move the failure.
        var orderId = Guid.NewGuid();
        var buyerId = Guid.NewGuid();
        context.Users.Add(new User
        {
            Id = buyerId, PhoneNumber = $"+38000{DateTime.UtcNow:HHmmss}{suffix.GetHashCode() % 1000:000}",
            CreatedAtUtc = now,
        });
        context.Orders.Add(new Order
        {
            Id = orderId, UserId = buyerId, Price = 100m,
            Status = OrderStatus.Fulfilled, CreatedAtUtc = now, UpdatedAtUtc = now,
        });

        context.FuelVouchers.Add(new FuelVoucher
        {
            Id = Guid.CreateVersion7(),
            Brand = "okko",
            FuelTypeId = fuelId,
            Liters = 10m,
            VoucherNumber = $"ARCH-{suffix}",
            QrPayload = $"9018$500$;arch={suffix}",
            Status = status,
            // Only the held statuses need one; the constraint refuses a null for them.
            OrderId = status is VoucherStatus.Assigned or VoucherStatus.Used or VoucherStatus.Blocked
                ? orderId
                : null,
            DataKind = DataKinds.Real,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        });

        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
    }

    [Fact]
    public async Task AFuelWithLiveVouchers_IsNotArchived_EvenThoughTheBrandDroppedIt()
    {
        await using var context = CreateContext();
        await SeedFuelWithVouchersAsync(context, "live", "Архів з живим", VoucherStatus.Available);

        // The sheet publishes only one other grade, so our fuel is simply not on it any more.
        var result = await ImportAsync(context, FullSheet);

        var fuel = await context.FuelTypes.IgnoreQueryFilters()
            .SingleAsync(f => f.Id == "arch-fuel-live");

        fuel.IsDeleted.Should().BeFalse(
            "customers still hold vouchers for it, and hiding the fuel would leave them unpriceable");

        result.ArchivedFuels.Should().Contain(f => f.FuelTypeId == "arch-fuel-live" && !f.Archived);
        result.ArchivedFuels.Single(f => f.FuelTypeId == "arch-fuel-live").LiveVouchers.Should().Be(1,
            "the receipt says how many are holding it, so the reason is visible rather than inferred");
    }

    [Fact]
    public async Task AFuelWhoseVouchersAreAllSpent_IsArchived()
    {
        await using var context = CreateContext();
        await SeedFuelWithVouchersAsync(context, "spent", "Архів витрачений", VoucherStatus.Used);

        var result = await ImportAsync(context, FullSheet);

        var fuel = await context.FuelTypes.IgnoreQueryFilters()
            .SingleAsync(f => f.Id == "arch-fuel-spent");

        fuel.IsDeleted.Should().BeTrue(
            "the brand no longer publishes it and there is nothing left to sell");
        fuel.DeletedAtUtc.Should().NotBeNull();

        result.ArchivedFuels.Should().Contain(f => f.FuelTypeId == "arch-fuel-spent" && f.Archived);
    }

    [Fact]
    public async Task AnExpiredVoucherAlsoFreesTheFuelToBeArchived()
    {
        await using var context = CreateContext();
        await SeedFuelWithVouchersAsync(context, "expired", "Архів прострочений", VoucherStatus.Expired);

        await ImportAsync(context, FullSheet);

        (await context.FuelTypes.IgnoreQueryFilters().SingleAsync(f => f.Id == "arch-fuel-expired"))
            .IsDeleted.Should().BeTrue("an expired voucher is a loss, not a claim on the fuel");
    }

    [Fact]
    public async Task ABrandThatStillPublishesTheFuel_IsNeverArchived_HoweverIdleItIs()
    {
        await using var context = CreateContext();
        await SeedFuelWithVouchersAsync(context, "stilllisted", "ДП PULLS (idle)", VoucherStatus.Used);

        // Present on the sheet, under our own spelling — the grade matches even though the name does not.
        var result = await ImportAsync(context, FullSheet);

        (await context.FuelTypes.IgnoreQueryFilters().SingleAsync(f => f.Id == "arch-fuel-stilllisted"))
            .IsDeleted.Should().BeFalse(
            "an idle grade the brand still sells is simply one we happen not to carry today");

        result.ArchivedFuels.Should().NotContain(f => f.FuelTypeId == "arch-fuel-stilllisted");
    }


    [Fact]
    public void AVoucherThatIsSpentOrExpiredDoesNotBlockAnArchive()
    {
        VoucherStatuses.IsLive(VoucherStatus.Used).Should().BeFalse();
        VoucherStatuses.IsLive(VoucherStatus.Expired).Should().BeFalse();

        VoucherStatuses.IsLive(VoucherStatus.Imported).Should().BeTrue();
        VoucherStatuses.IsLive(VoucherStatus.Available).Should().BeTrue();
        VoucherStatuses.IsLive(VoucherStatus.Assigned).Should().BeTrue("a customer is holding it");

        // Deliberately not blocking: a voucher that failed verification or was switched off is not a
        // claim on the fuel, and counting it would make a fuel impossible to retire until somebody
        // cleaned up junk rows.
        VoucherStatuses.IsLive(VoucherStatus.VerificationFailed).Should().BeFalse();
        VoucherStatuses.IsLive(VoucherStatus.Deactivated).Should().BeFalse();
    }
}