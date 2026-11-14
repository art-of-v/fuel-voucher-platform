using FluentAssertions;
using FuelFlow.Features.Vouchers;
using FuelFlow.Features.Vouchers.Availability;
using FuelFlow.Features.Vouchers.SharedModels;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using FuelFlow.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Xunit;
using static FuelFlow.SharedKernel.Domain.FuelIdMapper;

namespace FuelFlow.IntegrationTests;

/// <summary>
/// The stock summary and the vouchers it counts, against a real PostgreSQL.
/// </summary>
/// <remarks>
/// <para>
/// The summary exists so that two questions are answered from a few dozen rows instead of a scan
/// of the largest table in the schema: the operator's three dropdowns (F-2) and the storefront's
/// package list (F-3). What these tests hold is the property the summary exists for - <b>it is
/// the same answer as the vouchers</b> - and the trigger is what makes it so.
/// </para>
/// <para>
/// A trigger, and not the application: the fulfilment path claims a voucher with a raw UPDATE
/// (<c>TryAssignVoucherAsync</c>), so a summary maintained by the application would miss the one
/// write that matters most - the one that takes a voucher out of stock. The trigger sees every
/// write, from every path, in one place.
/// </para>
/// <para>
/// The unit test that pins the dropdown (<c>AdminVouchers_ListsFuelTypesPerProvider_SoTheOptionsCannotMix</c>)
/// seeds the summary by hand, because the in-memory provider has no trigger. These are the tests
/// that prove the hand-seeded rows are what the trigger would have written.
/// </para>
/// </remarks>
[Collection("Integration Tests")]
public sealed class StockSummaryFollowsTheVouchersTests : IClassFixture<TestDatabaseFixture>
{
    private readonly TestDatabaseFixture _fixture;

    public StockSummaryFollowsTheVouchersTests(TestDatabaseFixture fixture) => _fixture = fixture;

    private ApplicationDbContext CreateContext()
        => new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(_fixture.ConnectionString)
            .Options, TestQrCipher.Instance);

    [Fact]
    public async Task The_Summary_Counts_Exactly_The_Vouchers_It_Describes()
    {
        await using var context = CreateContext();
        await context.Database.MigrateAsync();
        await ResetAsync(context);

        await SeedVoucherAsync(context, brand: "okko", fuelTypeId: "okko-95", liters: 50m, status: VoucherStatus.Available);
        await SeedVoucherAsync(context, brand: "okko", fuelTypeId: "okko-95", liters: 50m, status: VoucherStatus.Available);
        await SeedVoucherAsync(context, brand: "okko", fuelTypeId: "okko-dp", liters: 10m, status: VoucherStatus.Available);
        await SeedVoucherAsync(context, brand: "wog", fuelTypeId: "wog-95", liters: 20m, status: VoucherStatus.Available);

        // The whole point of the table: a number that is the same as the count of the rows, for
        // every group, without counting the rows.
        var summary = await context.StockSummary.AsNoTracking().ToListAsync();

        var vouchers = await context.FuelVouchers.IgnoreQueryFilters().ToListAsync();
        vouchers.Should().HaveCount(4);
        summary.Sum(s => s.VoucherCount).Should().Be(vouchers.Count,
            "the summary counts the vouchers, once each");

        summary.Should().HaveCount(3, "three groups: two of okko-95 at 50 L, one of okko-dp, one of wog-95");
        summary.Single(s => s.Brand == "okko" && s.FuelTypeId == ToUuid("okko-95")).VoucherCount.Should().Be(2);
    }

    [Fact]
    public async Task A_Voucher_That_Changes_Place_Leaves_Its_Old_Group()
    {
        await using var context = CreateContext();
        await context.Database.MigrateAsync();
        await ResetAsync(context);

        var voucherId = await SeedVoucherAsync(context, brand: "okko", fuelTypeId: "okko-95", liters: 50m, status: VoucherStatus.Available);

        // The fulfilment path claims a voucher with a raw UPDATE - not through EF - so this is the
        // write a summary maintained by the application would miss.
        await using (var claim = CreateContext())
        {
            await claim.Database.ExecuteSqlRawAsync(
                """
                UPDATE fuel_vouchers SET status = 'Expired', updated_at_utc = NOW()
                WHERE id = {0}
                """, voucherId);
        }

        await using var verify = CreateContext();
        var summary = await verify.StockSummary.AsNoTracking().ToListAsync();

        summary.Should().HaveCount(1);
        summary[0].Status.Should().Be(VoucherStatus.Expired);
        summary[0].VoucherCount.Should().Be(1,
            "the voucher moved to the Assigned group; it is not in both");
    }

    [Fact]
    public async Task A_Deleted_Voucher_Leaves_The_Group_It_Was_In()
    {
        await using var context = CreateContext();
        await context.Database.MigrateAsync();
        await ResetAsync(context);

        var voucherId = await SeedVoucherAsync(context, brand: "okko", fuelTypeId: "okko-95", liters: 50m, status: VoucherStatus.Available);

        await using (var soft = CreateContext())
        {
            await soft.Database.ExecuteSqlRawAsync(
                "UPDATE fuel_vouchers SET is_deleted = true, updated_at_utc = NOW() WHERE id = {0}", voucherId);
        }

        await using var verify = CreateContext();
        var summary = await verify.StockSummary.AsNoTracking().ToListAsync();

        // The operator's list counts with IgnoreQueryFilters, so a soft-deleted voucher is still in
        // the inventory the dropdown offers - in its own group, with the deleted flag on it.
        summary.Should().HaveCount(1);
        summary[0].IsDeleted.Should().BeTrue();
        summary[0].VoucherCount.Should().Be(1);
    }

    [Fact]
    public async Task A_Voucher_That_Is_Hard_Deleted_Leaves_The_Summary()
    {
        await using var context = CreateContext();
        await context.Database.MigrateAsync();
        await ResetAsync(context);

        var voucherId = await SeedVoucherAsync(context, brand: "okko", fuelTypeId: "okko-95", liters: 50m, status: VoucherStatus.Available);

        await using (var hard = CreateContext())
        {
            await hard.Database.ExecuteSqlRawAsync("DELETE FROM fuel_vouchers WHERE id = {0}", voucherId);
        }

        await using var verify = CreateContext();
        (await verify.StockSummary.AsNoTracking().ToListAsync()).Should().BeEmpty(
            "a group that empties out is a row nobody will ever read");
    }

    [Fact]
    public async Task The_Summary_Answers_What_Is_Sellable_And_Nothing_Else()
    {
        await using var context = CreateContext();
        await context.Database.MigrateAsync();
        await ResetAsync(context);

        var today = DateOnly.FromDateTime(TestClock.UtcNow);

        await SeedVoucherAsync(context, brand: "okko", fuelTypeId: "okko-95", liters: 50m, status: VoucherStatus.Available, expiresOn: today.AddMonths(6));
        await SeedVoucherAsync(context, brand: "okko", fuelTypeId: "okko-dp", liters: 10m, status: VoucherStatus.Available, expiresOn: today.AddDays(-1));
        await SeedVoucherAsync(context, brand: "wog", fuelTypeId: "wog-95", liters: 20m, status: VoucherStatus.Expired, expiresOn: today.AddMonths(6));
        await SeedVoucherAsync(context, brand: "wog", fuelTypeId: "wog-dp", liters: 20m, status: VoucherStatus.Available, expiresOn: today.AddMonths(6), dataKind: DataKinds.Qa);

        var availability = new PackageStockAvailability(
            context,
            new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["VoucherExpiration:Enabled"] = "true"
            }).Build());

        var keys = await availability.AvailableKeysAsync(isTestAccount: false);

        // Available, real, and not expired - the same rule the fulfilment path applies.
        keys.Should().ContainSingle();
        keys.Single().Should().Be(new StockKey("okko", ToUuid("okko-95"), 50m));

        await using var verify = CreateContext();
        (await verify.StockSummary.AsNoTracking().ToListAsync()).Should().HaveCount(4,
            "the summary holds every group - the storefront asks it for the sellable one");
    }

    // ---- helpers --------------------------------------------------------------------------------

    private static async Task<Guid> SeedVoucherAsync(
        ApplicationDbContext context,
        string brand,
        string fuelTypeId,
        decimal liters,
        VoucherStatus status,
        DateOnly? expiresOn = null,
        string dataKind = DataKinds.Real)
    {
        var id = Guid.NewGuid();
        var brandId = BrandIds.ForSlug(brand);

        context.FuelVouchers.Add(new FuelVoucher
        {
            Id = id,
            BrandId = brandId,
            Brand = brand,
            FuelTypeId = ToUuid(fuelTypeId),
            Liters = liters,
            Status = status,
            DataKind = dataKind,
            SupplierExpirationDate = expiresOn ?? DateOnly.FromDateTime(TestClock.UtcNow.AddMonths(6)),
            CustomerExpirationDate = expiresOn ?? DateOnly.FromDateTime(TestClock.UtcNow.AddMonths(6)),
            VoucherNumber = $"SUM-{id:N}"[..20],
            QrPayload = $"sum-{id:N}",
            QrPayloadSha256 = System.Text.Encoding.UTF8.GetBytes(id.ToString()),
            CreatedAtUtc = TestClock.UtcNow,
            UpdatedAtUtc = TestClock.UtcNow
        });

        await context.SaveChangesAsync();
        return id;
    }

    private static async Task ResetAsync(ApplicationDbContext context)
    {
        await context.Database.ExecuteSqlRawAsync(
            """
            TRUNCATE TABLE "fuel_vouchers", "stock_summary" RESTART IDENTITY CASCADE
            """);
    }
}
