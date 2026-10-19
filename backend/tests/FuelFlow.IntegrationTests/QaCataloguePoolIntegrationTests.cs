using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FuelFlow.Features.Brands;
using FuelFlow.Features.Vouchers;
using FuelFlow.Features.Vouchers.SharedModels;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FuelFlow.IntegrationTests;

/// <summary>
/// Who may see the QA catalogue. The pool split already existed on the stock side
/// (<c>QaTestPoolSegregationIntegrationTests</c> proves a QA account is only ever handed test fuel
/// and a real one never is), but the <b>catalogue</b> was filtered flatly: both anonymous storefront
/// routes hid every test package from everyone.
/// </summary>
/// <remarks>
/// <para>
/// That is safe in one direction and useless in the other. A non-QA caller cannot reach a test
/// package, so nothing leaked — but neither could QA, which is why every QA fixture to date had to
/// reuse the live catalogue and exercise real package ids at real prices. The App Store reviewer has
/// to sign in as QA and see something.
/// </para>
/// <para>
/// So the filter became pool-aware rather than absent, and this class pins both halves: each caller
/// sees its own pool and <b>not</b> the other one. Also the by-Brand route, which is a separate
/// endpoint reading the same table — a filter applied to one and not the other would leave the
/// stricter one decorative.
/// </para>
/// </remarks>
[Collection("Integration Tests")]
public class QaCataloguePoolIntegrationTests : IClassFixture<TestDatabaseFixture>
{
/// <summary>
    /// A brand of this test's own. It has to be one: the assertions below are <c>Assert.Single</c> on
    /// the packages a brand sells, and the seeded OKKO catalogue would answer that with rows this test
    /// never wrote. Fixed rather than generated so a second seeding finds the row the first left.
    /// </summary>
    private static readonly Guid Brand = new("9f3c1d20-7a54-4c1e-9b6d-2f0c8a1e5b71");

    /// <summary>The network's own code, and what a voucher's brand NAME is matched against.</summary>
    private const string BrandSlug = "qa-pool-okko";
    private const string FuelType = "qa-pool-95";

    /// <summary>
    /// The one nominal both pools sell. A size belongs to the fuel, not to a pool: <c>fuel_nominals</c>
    /// is keyed by <c>(fuel_type_id, liters)</c> and knows nothing about <c>data_kind</c>. So the two
    /// pools of one fuel list the same sizes and differ in what each size COSTS and whose stock is
    /// behind it. The tests below are built on that, and a distinct QA nominal is no longer
    /// expressible.
    /// </summary>
    private const decimal Liters = 10m;

    /// <summary>
    /// The size only the rehearsal pool sells. The two pools no longer differ in what a litre costs -
    /// one price per grade, since 2026-10-19 - so the size list is what still says "this is the other
    /// pool", and a test that proved separation through two prices would now be proving nothing.
    /// </summary>
    private const decimal QaLiters = 7m;

    private const decimal CostPerLiter = 45m;

    private const string RealName = "А95";

    private readonly TestDatabaseFixture _fixture;

    public QaCataloguePoolIntegrationTests(TestDatabaseFixture fixture) => _fixture = fixture;

    /// <summary>
    /// Seed the Brand, one fuel, both packages and one sellable voucher per pool. Idempotent: the
    /// fixture is one database per class and every test seeds, so a second call must find what the
    /// first left rather than collide with it.
    /// </summary>
    private async Task SeedBothPoolsAsync()
    {
        using var scope = _fixture.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var now = DateTime.UtcNow;

        // The public catalogue joins the brand row for the slug, so a package whose brand was never
        // seeded would silently vanish from the response instead of failing the test.
        if (!await context.Brands.AnyAsync(b => b.Id == Brand, CancellationToken.None))
        {
            context.Brands.Add(new Brand
            {
                Id = Brand,
                Slug = BrandSlug,
                Name = "QA pool",
                LogoText = "QA",
                Color = "#00ff80",
                SortOrder = 999,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            });
        }

        if (!await context.FuelTypes.AnyAsync(f => f.Id == FuelType, CancellationToken.None))
        {
            context.FuelTypes.Add(new FuelTypeEntity
            {
                Id = FuelType,
                Name = RealName,
                BrandId = Brand,
                BasePrice = 50,
                DiscountPrice = 47,
                SupplierPricePerLiter = CostPerLiter,
                MarginUahPerLiter = 2m,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            });
        }

        // One nominal per pool, sold at the same price. The pools no longer differ in what a litre
        // costs - one price per grade since 2026-10-19 - so the size list is what still says "this is
        // the other pool", and this test proves separation through it rather than through two prices.
        context.SeedPackage("qa-pool-real-10", Brand, FuelType, Liters, 470m * Liters,
            supplierPricePerLiter: CostPerLiter, marginUahPerLiter: 2m, finalPricePerLiter: 47m,
            isTestData: false, createdAtUtc: now, updatedAtUtc: now);

        context.SeedPackage("qa-pool-qa-7", Brand, FuelType, QaLiters, 470m * QaLiters,
            supplierPricePerLiter: CostPerLiter, marginUahPerLiter: 2m, finalPricePerLiter: 47m,
            isTestData: true, createdAtUtc: now, updatedAtUtc: now);

        // One sellable voucher per pool, seeded once. Without both, "QA can see the QA package" would
        // pass on a row that is merely visible but dark, which is not what the reviewer needs.
        if (!await context.FuelVouchers.AnyAsync(
            v => v.FuelTypeId == FuelType && v.Liters == Liters && v.DataKind == DataKinds.Real, CancellationToken.None))
        {
            context.FuelVouchers.Add(SellableVoucher(Liters, isTestData: false));
        }

        if (!await context.FuelVouchers.AnyAsync(
            v => v.FuelTypeId == FuelType && v.Liters == QaLiters && v.DataKind == DataKinds.Qa, CancellationToken.None))
        {
            context.FuelVouchers.Add(SellableVoucher(QaLiters, isTestData: true));
        }

        await context.SaveChangesAsync();
    }

    private static FuelVoucher SellableVoucher(decimal liters, bool isTestData) => new()
    {
        Id = Guid.CreateVersion7(),
        // Brand is the network's NAME, compared case-insensitively against the slug -
        // StockKey is (brand, fuel type, nominal), so a row keyed to the brand's id instead would
        // never match its own package. See StockKey for why those two have always been the same string.
        Brand = BrandSlug,
        FuelTypeId = FuelType,
        Liters = liters,
        CostPerLiter = 45m,
        SupplierExpirationDate = DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(6)),
        CustomerExpirationDate = DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(6)),
        VoucherNumber = $"QA-POOL-{isTestData}-{liters:0.##}",
        QrPayload = Guid.NewGuid().ToString(),
        Status = VoucherStatus.Available,
        IsTestData = isTestData,
        CreatedAtUtc = DateTime.UtcNow,
        UpdatedAtUtc = DateTime.UtcNow
    };

    /// <summary>
    /// Sign in and return a client carrying the token. <c>IsQaAccount</c> is then set through the
    /// database, which takes effect on the next request without a re-login:
    /// <c>PackageStockAvailability.IsTestAccountAsync</c> reads the column, not a claim.
    /// </summary>
    private async Task<HttpClient> ClientForAsync(string phone, bool isQaAccount)
    {
        var client = _fixture.CreateClient();

        (await client.PostAsJsonAsync("/api/auth/send-code", new { phoneNumber = phone }))
            .EnsureSuccessStatusCode();
        var verify = await client.PostAsJsonAsync("/api/auth/verify", new { phoneNumber = phone, code = "000000" });
        verify.EnsureSuccessStatusCode();
        var body = await verify.Content.ReadFromJsonAsync<JsonElement>();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", body.GetProperty("accessToken").GetString());

        using (var scope = _fixture.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var user = await context.Users.SingleAsync(u => u.PhoneNumber == phone, CancellationToken.None);
            user.IsQaAccount = isQaAccount;
            user.IsActive = true;
            user.UpdatedAtUtc = DateTime.UtcNow;
            await context.SaveChangesAsync();
        }

        return client;
    }

    private async Task<List<PublicPackageResponse>> PackagesAsync(HttpClient client, bool byBrand, Guid? brand = null)
    {
        // `brand` defaults to null rather than to `Brand` because a static readonly Guid is not a
        // compile-time constant and cannot be a default parameter value; the route uses whichever it
        // was handed, and the filter below is what these tests actually assert on.
        var route = byBrand ? $"/api/packages/{brand ?? Brand}" : "/api/packages";
        var response = await client.GetAsync(route);
        response.EnsureSuccessStatusCode();
        var packages = await response.Content.ReadFromJsonAsync<List<PublicPackageResponse>>();
        return packages!.Where(p => p.BrandId == Brand).ToList();
    }

    /// <summary>
    /// The anonymous reader is the case that was already safe and must stay that way: it sees the
    /// live catalogue, priced from the live offer.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AnAnonymousCaller_SeesTheRealCatalogueOnly(bool byBrand)
    {
        await SeedBothPoolsAsync();
        var client = _fixture.CreateClient();

        var packages = await PackagesAsync(client, byBrand, Brand);

        var package = Assert.Single(packages);
        Assert.Equal(Liters, package.Liters);
        // The live offer's cost plus the fuel's margin. A rehearsal price here would mean the pool
        // filter had stopped filtering, which is the outage this whole directory of tests exists for.
        Assert.Equal(47m, package.FinalPricePerLiter);
    }

    /// <summary>
    /// Signed in, but not QA. The pool is a property of the account, not of the token carrying it,
    /// so a real customer with a valid token still gets the real catalogue.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AnAuthenticatedNonQaCaller_SeesTheRealCatalogueOnly(bool byBrand)
    {
        await SeedBothPoolsAsync();
        var client = await ClientForAsync("+380991007001", isQaAccount: false);

        var packages = await PackagesAsync(client, byBrand, Brand);

        Assert.Equal(47m, Assert.Single(packages).FinalPricePerLiter);
    }

    /// <summary>
    /// The behaviour that did not exist before: QA reads its own offer, with the QA stock behind it.
    /// The size is the same one the live catalogue sells, because a size belongs to the fuel — what
    /// differs is the price it is offered at and the pool the stock comes from.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AQaCaller_SeesTheQaCatalogue_AndTheQaStockBehindIt(bool byBrand)
    {
        await SeedBothPoolsAsync();
        var client = await ClientForAsync("+380991007002", isQaAccount: true);

        var packages = await PackagesAsync(client, byBrand, Brand);

        var qaPackage = Assert.Single(packages);
        // The size is the rehearsal one, and that is what proves the pool: the live pool's 10 L is
        // not in this list. The price is the same number the live caller sees, because there is one
        // price per grade - the two pools cannot be told apart by it any more.
        Assert.Equal(QaLiters, qaPackage.Liters);
        Assert.Equal(47m, qaPackage.FinalPricePerLiter);
        Assert.True(qaPackage.IsAvailable,
            "the QA package is visible and its stock is sellable, which is the whole point — a QA run " +
            "that could see the catalogue but not buy from it would be no better than reusing the live one");
    }

    /// <summary>
    /// The two pools must not bleed through the availability flag either. Each caller is backed by
    /// its own pool's stock, which is only true while the row filter and the stock filter read the
    /// same pool — the reason <c>PackageStockAvailability</c> resolves the caller once and uses it
    /// for both.
    /// </summary>
    [Fact]
    public async Task AvailabilityFollowsTheCallersOwnPool()
    {
        await SeedBothPoolsAsync();

        var real = await PackagesAsync(await ClientForAsync("+380991007003", isQaAccount: false), byBrand: false);
        var qa = await PackagesAsync(await ClientForAsync("+380991007004", isQaAccount: true), byBrand: false);

        Assert.True(Assert.Single(real).IsAvailable, "the real package is backed by real stock");
        Assert.True(Assert.Single(qa).IsAvailable, "the QA package is backed by test stock");
    }

    /// <summary>
    /// A QA caller and an anonymous one reading the same Brand, side by side: the rows are
    /// genuinely different responses, not one response with a flag on it. This is what the by-Brand
    /// route looked like when only the whole-catalogue route was pool-aware.
    /// </summary>
    [Fact]
    public async Task TheByBrandRoute_ResolvesTheSamePoolAsTheWholeCatalogue()
    {
        await SeedBothPoolsAsync();
        var anonymous = await PackagesAsync(_fixture.CreateClient(), byBrand: true);
        var qa = await PackagesAsync(await ClientForAsync("+380991007005", isQaAccount: true), byBrand: true);

// The display name is the fuel's, and the fuel is shared by both pools - it comes from
    // fuel_types now, not from a string each package row carried and could contradict. So the QA
    // catalogue no longer renders its own "(QA)" label; the two pools are told apart by the SIZE they
    // sell, which is what actually differs. Anything identifying a pool by name here would have
    // been asserting the old duplicated column.
    var anonymousPackage = Assert.Single(anonymous);
    var qaPackage = Assert.Single(qa);
    Assert.Equal(RealName, anonymousPackage.FuelName);
    Assert.Equal(RealName, qaPackage.FuelName);
    Assert.Equal(Liters, anonymousPackage.Liters);
    Assert.Equal(QaLiters, qaPackage.Liters);
    Assert.NotEqual(anonymousPackage.Id, qaPackage.Id);
    // The rate is the same number for both, and that is the shape of the model now: one price per
    // grade. It used to be asserted NOT equal, which was the old behaviour this branch removes. The
    // package TOTALS still differ, because the two pools sell different sizes.
    Assert.Equal(anonymousPackage.FinalPricePerLiter, qaPackage.FinalPricePerLiter);
    Assert.NotEqual(anonymousPackage.Price, qaPackage.Price);
    }
}
