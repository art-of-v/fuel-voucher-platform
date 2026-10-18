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
    /// <summary>One Brand and one fuel, carrying a real package and its QA twin.</summary>
    private const string Brand = "qa-pool-okko";
    private const string FuelType = "qa-pool-95";

    /// <summary>The live nominal.</summary>
    private const decimal RealLiters = 10m;

    /// <summary>
    /// The QA nominal is deliberately not the real one. Availability keys on
    /// (Brand, fuel type, nominal) and carries no name, so a QA package sharing 10 L with the real
    /// one would light the real package's availability flag from test stock — QA would be shown the
    /// live package as buyable because the QA package behind it existed. Distinct nominals keep the
    /// two pools' flags independent; the names stay 1:1.
    /// </summary>
    private const decimal QaLiters = 7m;

    private const string RealName = "А95";
    private const string QaName = "А95(QA)";

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

        if (!await context.FuelTypes.AnyAsync(f => f.Id == FuelType, CancellationToken.None))
        {
            context.FuelTypes.Add(new FuelTypeEntity
            {
                Id = FuelType,
                Name = RealName,
                BrandId = Brand,
                BasePrice = 50,
                DiscountPrice = 47,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            });
        }

        // Seeded as an offer plus a volume per pool. The storefront joins volumes to offers to read the
        // price, so a package left in fuel_packages would leave the catalogue empty and these tests
        // would assert against nothing.
        if (!await context.FuelOfferVolumes.AnyAsync(p => p.Id == "qa-pool-real-10", CancellationToken.None))
        {
            context.SeedPackage("qa-pool-real-10", Brand, FuelType, RealLiters, 470m * RealLiters,
                supplierPricePerLiter: 45m, marginUahPerLiter: 2m, finalPricePerLiter: 47m,
                isTestData: false, createdAtUtc: now, updatedAtUtc: now);
        }

        if (!await context.FuelOfferVolumes.AnyAsync(p => p.Id == "qa-pool-qa-7", CancellationToken.None))
        {
            context.SeedPackage("qa-pool-qa-7", Brand, FuelType, QaLiters, 470m * QaLiters,
                supplierPricePerLiter: 45m, marginUahPerLiter: 2m, finalPricePerLiter: 47m,
                isTestData: true, createdAtUtc: now, updatedAtUtc: now);
        }

        // One sellable voucher per pool, seeded once. Without both, "QA can see the QA package" would
        // pass on a row that is merely visible but dark, which is not what the reviewer needs.
        if (!await context.FuelVouchers.AnyAsync(
                v => v.FuelTypeId == FuelType && v.Liters == RealLiters && v.DataKind == DataKinds.Real, CancellationToken.None))
        {
            context.FuelVouchers.Add(SellableVoucher(RealLiters, isTestData: false));
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
        // Brand is the Brand id, compared case-insensitively - StockKey is
        // (Brand, fuel type, nominal), so a row keyed to the network name instead of the Brand
        // would never match its own package.
        Brand = Brand,
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

    private async Task<List<PublicPackageResponse>> PackagesAsync(HttpClient client, bool byBrand, string brand = Brand)
    {
        var route = byBrand ? $"/api/packages/{brand}" : "/api/packages";
        var response = await client.GetAsync(route);
        response.EnsureSuccessStatusCode();
        var packages = await response.Content.ReadFromJsonAsync<List<PublicPackageResponse>>();
        return packages!.Where(p => p.BrandId == Brand).ToList();
    }

    /// <summary>
    /// The anonymous reader is the case that was already safe and must stay that way: it sees the
    /// live catalogue, and not one row of the QA one.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AnAnonymousCaller_SeesTheRealCatalogueOnly(bool byBrand)
    {
        await SeedBothPoolsAsync();
        var client = _fixture.CreateClient();

        var packages = await PackagesAsync(client, byBrand, Brand);

        Assert.Contains(packages, p => p.Liters == RealLiters);
        Assert.DoesNotContain(packages, p => p.Liters == QaLiters);
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

        Assert.Contains(packages, p => p.Liters == RealLiters);
        Assert.DoesNotContain(packages, p => p.Liters == QaLiters);
    }

    /// <summary>
    /// The behaviour that did not exist before: QA sees its own catalogue, with the QA stock behind
    /// it, and the live package is not in the response at all — not greyed out, absent.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AQaCaller_SeesTheQaCatalogue_AndTheQaStockBehindIt(bool byBrand)
    {
        await SeedBothPoolsAsync();
        var client = await ClientForAsync("+380991007002", isQaAccount: true);

        var packages = await PackagesAsync(client, byBrand, Brand);

        var qaPackage = Assert.Single(packages, p => p.Liters == QaLiters);
        Assert.True(qaPackage.IsAvailable,
            "the QA package is visible and its stock is sellable, which is the whole point — a QA run " +
            "that could see the catalogue but not buy from it would be no better than reusing the live one");
        Assert.DoesNotContain(packages, p => p.Liters == RealLiters);
    }

    /// <summary>
    /// The two pools must not bleed through the availability flag either. Each caller's own package
    /// is lit and the other one is not, which is only true while the row filter and the stock filter
    /// read the same pool — the reason <c>PackageStockAvailability</c> resolves the caller once and
    /// uses it for both.
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
        // catalogue no longer renders its own "(QA)" label; the two pools are told apart by their
        // nominal, which is what actually differs. Anything identifying a pool by name here would have
        // been asserting the old duplicated column.
        Assert.Equal(RealName, Assert.Single(anonymous).FuelName);
        Assert.Equal(RealName, Assert.Single(qa).FuelName);
        Assert.Equal(RealLiters, Assert.Single(anonymous).Liters);
        Assert.Equal(QaLiters, Assert.Single(qa).Liters);
    }
}
