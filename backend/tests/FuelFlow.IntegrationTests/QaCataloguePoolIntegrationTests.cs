using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FuelFlow.Features.Brands;
using FuelFlow.Features.Vouchers;
using FuelFlow.Features.Vouchers.SharedModels;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using FuelFlow.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static FuelFlow.SharedKernel.Domain.FuelIdMapper;

namespace FuelFlow.IntegrationTests;

/// <summary>
/// The storefront shows the real catalogue to every caller. The QA account is not an exception:
/// it signs in with the fixed code, then browses and buys from the same shop as a customer.
/// </summary>
/// <remarks>
/// <para>
/// Rehearsal rows (<c>data_kind = 'qa'</c>) stay in the database for the admin screens that create
/// and clean them up, but neither storefront route offers them for sale. That is what keeps a
/// seeded test package from reaching a buyer, and it is also why the App Store review needs no
/// separate QA stock at all.
/// </para>
/// <para>
/// Both storefront routes are pinned here. They read the same table through separate handlers, so
/// a filter applied to one and not the other would leave the stricter one decorative.
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
    /// The size the real catalogue sells. <c>fuel_nominals</c> is keyed by
    /// <c>(fuel_type_id, data_kind, liters)</c>, so every size is stated per pool — and only the real
    /// row is ever offered for sale.
    /// </summary>
    private const decimal Liters = 10m;

    /// <summary>
    /// The size only the rehearsal pool sells. Its presence in the database is what proves the
    /// storefront dropped the QA filter rather than merely having nothing to show: the 10 L real
    /// package must be the single result and this 7 L size must not appear at all.
    /// </summary>
    private const decimal QaLiters = 7m;

    private const decimal CostPerLiter = 45m;

    private const string RealName = "А95";

    private readonly TestDatabaseFixture _fixture;

    public QaCataloguePoolIntegrationTests(TestDatabaseFixture fixture) => _fixture = fixture;

    /// <summary>
    /// Seed the Brand, one fuel, one nominal per pool and one sellable voucher per pool. Idempotent:
    /// the fixture is one database per class and every test seeds, so a second call must find what the
    /// first left rather than collide with it.
    /// </summary>
    private async Task SeedBothPoolsAsync()
    {
        using var scope = _fixture.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var now = TestClock.UtcNow;

        // The public catalogue joins the brand row for the slug, so a package whose brand was never
        // seeded would silently vanish from the response instead of failing the test.
        if (!await context.Brands.AnyAsync(b => b.Id == Brand, CancellationToken.None))
        {
            context.Brands.Add(new Brand
            {
                Id = Brand,
                Slug = BrandSlug,
                Name = "QA catalogue probe",
                LogoText = "qa",
                Color = "#00ff80",
                SortOrder = 999,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            });
        }

        if (!await context.FuelTypes.AnyAsync(f => f.Id ==  ToUuid(FuelType), CancellationToken.None))
        {
            context.FuelTypes.Add(new FuelTypeEntity
            {
                Id =   ToUuid(FuelType),
                Name = RealName,
                BrandId = Brand,
                SupplierPricePerLiter = CostPerLiter,
                MarginUahPerLiter = 2m,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            });
        }

        // One nominal per pool, sold at the same price. A rehearsal nominal is inert for sales; it
        // exists here so the tests can prove it is filtered out rather than simply absent.
        context.SeedPackage("qa-pool-real-10", Brand, FuelType, Liters, 470m * Liters,
            supplierPricePerLiter: CostPerLiter, marginUahPerLiter: 2m, finalPricePerLiter: 47m,
            isTestData: false, createdAtUtc: now, updatedAtUtc: now);

        context.SeedPackage("qa-pool-qa-7", Brand, FuelType, QaLiters, 470m * QaLiters,
            supplierPricePerLiter: CostPerLiter, marginUahPerLiter: 2m, finalPricePerLiter: 47m,
            isTestData: true, createdAtUtc: now, updatedAtUtc: now);

        // One sellable voucher per pool. The real one makes the 10 L package buyable for everyone;
        // the rehearsal one must make nothing buyable for anyone.
        if (!await context.FuelVouchers.AnyAsync(
            v => v.FuelTypeId == ToUuid(FuelType) && v.Liters == Liters && v.DataKind ==  DataKinds.Real, CancellationToken.None))
        {
            context.FuelVouchers.Add(SellableVoucher(Liters, isTestData: false));
        }

        if (!await context.FuelVouchers.AnyAsync(
            v => v.FuelTypeId == ToUuid(FuelType) && v.Liters == QaLiters && v.DataKind ==  DataKinds.Qa, CancellationToken.None))
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
           BrandId = Brand,
        FuelTypeId =   ToUuid(FuelType),
        Liters = liters,
        CostPerLiter = 45m,
        SupplierExpirationDate = DateOnly.FromDateTime(TestClock.UtcNow.AddMonths(6)),
        CustomerExpirationDate = DateOnly.FromDateTime(TestClock.UtcNow.AddMonths(6)),
        VoucherNumber = $"QA-POOL-{isTestData}-{liters:0.##}",
        QrPayload = Guid.NewGuid().ToString(),
        Status = VoucherStatus.Available,
        IsTestData = isTestData,
        CreatedAtUtc = TestClock.UtcNow,
        UpdatedAtUtc = TestClock.UtcNow
    };

    /// <summary>
    /// Sign in and return a client carrying the token. <c>IsQaAccount</c> is set through the
    /// database so the tests below can compare a flagged account with an ordinary one; it routes
    /// payment to the sandbox but deliberately does not change the catalogue.
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
            user.UpdatedAtUtc = TestClock.UtcNow;
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
    /// The QA account sees the real catalogue, not the rehearsal one: the 10 L real package is the
    /// single result, the 7 L rehearsal size is absent, and the real stock behind it makes it
    /// buyable. This is the App Store review path — sign in with the fixed code and use the shop.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AQaCaller_SeesTheRealCatalogue_AndRealStockBehindIt(bool byBrand)
    {
        await SeedBothPoolsAsync();
        var client = await ClientForAsync("+380991007002", isQaAccount: true);

        var packages = await PackagesAsync(client, byBrand, Brand);

        var package = Assert.Single(packages);
        Assert.Equal(Liters, package.Liters);
        Assert.Equal(47m, package.FinalPricePerLiter);
        Assert.True(package.IsAvailable,
            "the QA account buys from the real pool, so the real stock makes the package buyable");
    }

    /// <summary>
    /// A QA account and a customer get the same availability answer, because they read the same
    /// pool. The rehearsal voucher behind the 7 L size does not make anything available to either.
    /// </summary>
    [Fact]
    public async Task AQaCallerSeesTheSameAvailabilityAsACustomer()
    {
        await SeedBothPoolsAsync();

        var real = await PackagesAsync(await ClientForAsync("+380991007003", isQaAccount: false), byBrand: false);
        var qa = await PackagesAsync(await ClientForAsync("+380991007004", isQaAccount: true), byBrand: false);

        Assert.True(Assert.Single(real).IsAvailable, "the real package is backed by real stock");
        Assert.True(Assert.Single(qa).IsAvailable, "the QA account reads the same real stock");
    }

    /// <summary>
    /// The by-Brand route resolves the same real row as the whole-catalogue route for a QA caller:
    /// the two endpoints are separate handlers, so this pins them to one answer.
    /// </summary>
    [Fact]
    public async Task TheByBrandRoute_ResolvesTheSameRealCatalogueAsTheWholeCatalogue()
    {
        await SeedBothPoolsAsync();
        var anonymous = await PackagesAsync(_fixture.CreateClient(), byBrand: true);
        var qa = await PackagesAsync(await ClientForAsync("+380991007005", isQaAccount: true), byBrand: true);

        var anonymousPackage = Assert.Single(anonymous);
        var qaPackage = Assert.Single(qa);
        Assert.Equal(RealName, anonymousPackage.FuelName);
        Assert.Equal(RealName, qaPackage.FuelName);
        Assert.Equal(Liters, anonymousPackage.Liters);
        Assert.Equal(Liters, qaPackage.Liters);
        Assert.Equal(anonymousPackage.Id, qaPackage.Id);
        Assert.Equal(anonymousPackage.FinalPricePerLiter, qaPackage.FinalPricePerLiter);
        Assert.Equal(anonymousPackage.Price, qaPackage.Price);
    }
}
