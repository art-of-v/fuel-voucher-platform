using FluentAssertions;
using FuelFlow.Features.Admin.QaCatalogue;
using FuelFlow.Features.Brands;
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
/// The reviewer's shelf, stocked from the live catalogue by <c>POST /api/admin/qa-catalogue/seed</c>.
/// </summary>
/// <remarks>
/// <para>
/// This replaced the SQL seeds, and the reason is a column type rather than taste: <c>qr_payload</c>
/// is AES-GCM ciphertext, so a hand-written INSERT can no longer produce a voucher the wallet can
/// read. The key lives in the API's configuration, which makes the API the only place a rehearsal
/// voucher can be written - and this test is what says the write produces a SELLABLE one, not merely
/// a row.
/// </para>
/// <para>
/// Three properties are pinned here. The stock it creates is invisible to the live pool (the seed is
/// the one write path that could leak it). A second run adds nothing (operators run this by hand, and
/// a non-idempotent seed doubles the shelf on a retry). And a package the reviewer emptied is topped
/// back up (a reviewer who bought every voucher would otherwise be told the seed is broken).
/// </para>
/// </remarks>
[Collection("Integration Tests")]
public sealed class QaCatalogueSeedIntegrationTests : IClassFixture<TestDatabaseFixture>
{
    /// <summary>
    /// A brand of this test's own: the handler mirrors every live package, and the seeded OKKO
    /// catalogue would make the counts below depend on rows this test never wrote.
    /// </summary>
    private static readonly Guid Brand = new("c4a71e58-2b93-4f60-8d17-5e6b90c2a834");

    private const string BrandSlug = "qa-seed-okko";
    private const string FuelType = "qa-seed-95";
    private const string FuelName = "А95";

    /// <summary>The live size, which is the one the seed has to mirror into the rehearsal pool.</summary>
    private const decimal Liters = 20m;

    private const decimal CostPerLiter = 45m;

    private readonly TestDatabaseFixture _fixture;

    public QaCatalogueSeedIntegrationTests(TestDatabaseFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Seed_MirrorsTheLivePackage_StocksItInTheRehearsalPool_AndOnlyThere()
    {
        await SeedLivePackageAsync();

        // The handler mirrors the WHOLE live catalogue - that is what a reviewer needs, not just this
        // test's package - so every assertion below is scoped to this test's brand and fuel. The one
        // catalogue-wide number this test can state is the retry: a second run that creates nothing
        // anywhere is exactly the idempotency the operator relies on.
        var first = await RunSeedAsync(3);
        first.VouchersCreated.Should().BeGreaterThanOrEqualTo(3, "the live package needs its shelf");

        using (var context = CreateContext())
        {
            await context.FuelNominals
                .SingleAsync(n => n.FuelTypeId == ToUuid(FuelType) && n.Liters == Liters && n.DataKind == DataKinds.Qa);

            var vouchers = await context.FuelVouchers
                .Where(v => v.FuelTypeId == ToUuid(FuelType) && v.DataKind == DataKinds.Qa)
                .ToListAsync();

            vouchers.Should().HaveCount(3);
            vouchers.Should().OnlyContain(v => v.Status == VoucherStatus.Available);
            vouchers.Should().OnlyContain(v => v.Brand == BrandSlug && v.Liters == Liters);
            vouchers.Should().OnlyContain(v => v.CostPerLiter == CostPerLiter,
                "the rehearsal package is priced off the fuel it mirrors");

            // The value converter is what makes these rows readable, and it is why the SQL seeds had
            // to go: the plaintext round-trips through the cipher, and the hash the unique index
            // needs is filled by the model, not by the caller.
            vouchers.Should().OnlyContain(v => v.QrPayload.StartsWith("QA-QR-"));
            vouchers.Should().OnlyContain(v => v.QrPayloadSha256 != null && v.QrPayloadSha256.Length == 32);
        }

        // Sellable to the QA caller, and only to them: the live pool has a nominal but no voucher
        // here, so a true answer for a real buyer would mean the seed leaked across the pools.
        var availability = CreateAvailability();
        (await availability.IsInStockAsync(true, BrandSlug, ToUuid(FuelType), Liters, CancellationToken.None))
            .Should().BeTrue("the whole point of the seed is a package the reviewer can buy");
        (await availability.IsInStockAsync(false, BrandSlug, ToUuid(FuelType), Liters, CancellationToken.None))
            .Should().BeFalse("rehearsal stock must never make a package buyable for a customer");

        // An operator retrying the call must not double the shelf.
        var second = await RunSeedAsync(3);
        second.NominalsCreated.Should().Be(0);
        second.VouchersCreated.Should().Be(0);

        // And a reviewer who emptied the shelf gets a new one, numbered past the spent series. The
        // status is Expired rather than Used only because `ck_voucher_held_has_order` will not accept
        // a held voucher without an order row - what the top-up reads is "not Available", which both
        // of them are.
        using (var context = CreateContext())
        {
            foreach (var voucher in await context.FuelVouchers
                         .Where(v => v.FuelTypeId == ToUuid(FuelType) && v.DataKind == DataKinds.Qa)
                         .ToListAsync())
            {
                voucher.Status = VoucherStatus.Expired;
            }

            await context.SaveChangesAsync();
        }

        await RunSeedAsync(5);

        using (var context = CreateContext())
        {
            var numbers = await context.FuelVouchers
                .Where(v => v.FuelTypeId == ToUuid(FuelType) && v.DataKind == DataKinds.Qa)
                .Select(v => v.VoucherNumber)
                .ToListAsync();

            numbers.Should().HaveCount(8);
            numbers.Should().OnlyHaveUniqueItems();
        }
    }

    private async Task<SeedQaCatalogueResult> RunSeedAsync(int vouchersPerPackage)
    {
        using var context = CreateContext();
        return await new SeedQaCatalogueCommandHandler(context)
            .HandleAsync(new SeedQaCatalogueCommand(vouchersPerPackage), CancellationToken.None);
    }

    private async Task SeedLivePackageAsync()
    {
        using var context = CreateContext();
        var now = TestClock.UtcNow;

        // The handler's query joins the brand for the slug and the fuel for its cost, so both rows
        // have to exist: a package whose brand was never seeded would vanish instead of failing.
        if (!await context.Brands.AnyAsync(b => b.Id == Brand, CancellationToken.None))
        {
            context.Brands.Add(new Brand
            {
                Id = Brand,
                Slug = BrandSlug,
                Name = "QA seed probe",
                LogoText = "qs",
                Color = "#00ff80",
                SortOrder = 998,
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
            });
        }

        if (!await context.FuelTypes.AnyAsync(f => f.Id == ToUuid(FuelType), CancellationToken.None))
        {
            context.FuelTypes.Add(new FuelTypeEntity
            {
                Id = ToUuid(FuelType),
                Name = FuelName,
                BrandId = Brand,
                SupplierPricePerLiter = CostPerLiter,
                MarginUahPerLiter = 2m,
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
            });
        }

        context.SeedPackage("qa-seed-real-20", Brand, FuelType, Liters, 940m,
            supplierPricePerLiter: CostPerLiter, marginUahPerLiter: 2m, finalPricePerLiter: 47m,
            isTestData: false, createdAtUtc: now, updatedAtUtc: now);

        await context.SaveChangesAsync();
    }

    private PackageStockAvailability CreateAvailability()
        => new(CreateContext(), new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["VoucherExpiration:Enabled"] = "true" })
            .Build());

    private ApplicationDbContext CreateContext()
        => new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(_fixture.ConnectionString)
            .Options, TestQrCipher.Instance);
}
