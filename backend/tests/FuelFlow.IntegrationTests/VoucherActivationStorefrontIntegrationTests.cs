using System.Net;
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
/// The operator workflow the whole out-of-stock feature rests on, proved end to end on a real
/// Postgres: <b>import → cost → activate → the package becomes purchasable</b>.
/// </summary>
/// <remarks>
/// <para>
/// The existing coverage stops one layer short. <c>VoucherAdminCommandHandlersTests</c> proves in
/// memory that the bulk-action handler moves a voucher's status, and the stock-gate integration test
/// seeds an <c>Available</c> row directly — bypassing the step that produces it. Nothing joined those
/// two: if activation had failed to flip the storefront flag, every unit test would still be green and
/// the demo scenario ("import A95 OKKO 10 L, that one package becomes buyable") would fail in front
/// of a customer with nothing pointing at the cause.
/// </para>
/// <para>
/// Driven through the real HTTP endpoints, so the controller's auth, the cost gate, the status flip and
/// the catalog projection are all in the path. Per [[package-stock-availability]] in the planning repo,
/// this is precondition #1 for the on-device campaign — the rest of that plan is a device run, but this
/// is the part that can be proved without one.
/// </para>
/// </remarks>
[Collection("Integration Tests")]
public class VoucherActivationStorefrontIntegrationTests : IClassFixture<TestDatabaseFixture>
{
    private readonly TestDatabaseFixture _fixture;
    private readonly HttpClient _client;
    private string? _accessToken;

    public VoucherActivationStorefrontIntegrationTests(TestDatabaseFixture fixture)
    {
        _fixture = fixture;
        _client = _fixture.CreateClient();
    }

    /// <summary>
    /// Staff session, because activation is an operator action. Same OTP dance as the purchase tests,
    /// plus the Admin role the bulk-action route is behind.
    /// </summary>
    private async Task AuthenticateStaffAsync()
    {
        if (_accessToken is not null)
            return;

        const string phone = "+380991009900";
        (await _client.PostAsJsonAsync("/api/auth/send-code", new { phoneNumber = phone }))
            .EnsureSuccessStatusCode();
        (await _client.PostAsJsonAsync("/api/auth/verify", new { phoneNumber = phone, code = "000000" }))
            .EnsureSuccessStatusCode();

        using (var scope = _fixture.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var role = await context.Roles.FirstOrDefaultAsync(r => r.Name == "Admin", CancellationToken.None);
            if (role is null)
            {
                role = new Role { Id = Guid.NewGuid(), Name = "Admin", CreatedAtUtc = TestClock.UtcNow };
                context.Roles.Add(role);
                await context.SaveChangesAsync();
            }

            var user = await context.Users.FirstAsync(u => u.PhoneNumber == phone, CancellationToken.None);
            user.RoleId = role.Id;
            // OTP verify registers a user as inactive; payment and staff routes both refuse those.
            user.IsActive = true;
            user.UpdatedAtUtc = TestClock.UtcNow;
            await context.SaveChangesAsync();
        }

        // Re-login so the token carries the role it was just granted.
        (await _client.PostAsJsonAsync("/api/auth/send-code", new { phoneNumber = phone }))
            .EnsureSuccessStatusCode();
        var verify = await _client.PostAsJsonAsync("/api/auth/verify", new { phoneNumber = phone, code = "000000" });
        verify.EnsureSuccessStatusCode();
        var body = await verify.Content.ReadFromJsonAsync<JsonElement>();
        _accessToken = body.GetProperty("accessToken").GetString();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);
    }

    /// <summary>
    /// A fuel and a package to hang stock off. Both rows are required, not optional dressing:
    /// <c>fuel_vouchers.fuel_type_id</c> is a real FK (Restrict), and without a package the storefront
    /// has nothing to report availability for.
    /// </summary>
    private async Task SeedFuelAndPackageAsync(Guid BrandId, string fuelTypeId, decimal liters)
    {
        using var scope = _fixture.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var now = TestClock.UtcNow;

        if (!await context.FuelTypes.AnyAsync(f => f.Id ==  ToUuid(fuelTypeId), CancellationToken.None))
        {
            context.FuelTypes.Add(new FuelTypeEntity
            {
                Id =   ToUuid(fuelTypeId),
                Name = fuelTypeId,
                BrandId = BrandId,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            });
        }

        // Seeded as an offer plus the size it is sold in: the storefront resolves its package
        // through the offer and the fuel's nominal list, so seeding neither would leave the shop dark
        // and every "activating turns it on" assertion would pass against a package the app cannot see.
        if (!await context.FuelNominals.AnyAsync(
                n => n.FuelTypeId == ToUuid(fuelTypeId) && n.Liters ==    liters,
                CancellationToken.None))
        {
            context.SeedPackage(
                id: $"{fuelTypeId}-{liters:0.##}".Replace('.', '_'),
                BrandId: BrandId,
                fuelTypeId: fuelTypeId,
                liters: liters,
                price: 470m * liters,
                supplierPricePerLiter: 45m,
                marginUahPerLiter: 2m,
                finalPricePerLiter: 47m,
                createdAtUtc: now,
                updatedAtUtc: now);
        }

        await context.SaveChangesAsync();
    }

    /// <summary>A freshly imported voucher: not sellable, and carries no cost until an operator enters one.</summary>
    private static FuelVoucher ImportedVoucher(string brand, string fuelTypeId, decimal liters, decimal? costPerLiter)
        => new()
        {
            Id = Guid.CreateVersion7(),
            BrandId = BrandIds.ForSlug(brand),
            Brand = brand,
            FuelTypeId =   ToUuid(fuelTypeId),
            Liters = liters,
            CostPerLiter = costPerLiter,
            SupplierExpirationDate = DateOnly.FromDateTime(TestClock.UtcNow.AddMonths(6)),
            CustomerExpirationDate = DateOnly.FromDateTime(TestClock.UtcNow.AddMonths(6)),
            VoucherNumber = $"IMPORT-{Guid.NewGuid().ToString()[..10]}",
            QrPayload = Guid.NewGuid().ToString(),
            Status = VoucherStatus.Imported,
            CreatedAtUtc = TestClock.UtcNow,
            UpdatedAtUtc = TestClock.UtcNow
        };

    private async Task<bool> IsPackageAvailableAsync(Guid BrandId, string fuelTypeId, decimal liters)
    {
        var response = await _client.GetAsync("/api/packages");
        response.EnsureSuccessStatusCode();
        var packages = await response.Content.ReadFromJsonAsync<List<PublicPackageResponse>>();
        return packages!.Single(p =>
            p.BrandId == BrandId && p.FuelTypeId == ToUuid(fuelTypeId) && p.Liters ==  liters).IsAvailable;
    }

    private async Task<(HttpStatusCode Status, string Body)> ActivateAsync(params Guid[] ids)
    {
        var response = await _client.PostAsJsonAsync("/api/admin/vouchers/bulk-action", new
        {
            action = "activate",
            ids = ids
        });
        return (response.StatusCode, await response.Content.ReadAsStringAsync());
    }

    /// <summary>
    /// The one the demo rests on: a package nobody can buy becomes buyable the moment an operator
    /// activates its stock — and only that one. Proves the import ≠ available gate (the package is
    /// unavailable while the voucher sits in <c>Imported</c>), the activation itself, and that a sibling
    /// nominal of the same fuel is untouched.
    /// </summary>
    [Fact]
    public async Task ActivatingAnImportedBatch_TurnsThatPackageOn_AndLeavesItsSiblingsOff()
    {
        await AuthenticateStaffAsync();
        await SeedFuelAndPackageAsync(BrandIds.Okko, "act-95", 10m);
        await SeedFuelAndPackageAsync(BrandIds.Okko, "act-95", 20m);

        using (var scope = _fixture.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            context.FuelVouchers.AddRange(
                ImportedVoucher("okko", "act-95", 10m, costPerLiter: 45m),
                ImportedVoucher("okko", "act-95", 20m, costPerLiter: 45m));
            await context.SaveChangesAsync();
        }

        // Before activation: both nominals sit on the shelf and neither can be bought. This is the
        // precondition the campaign assumes and, before this change, was silently untrue.
        Assert.False(await IsPackageAvailableAsync(BrandIds.Okko, "act-95", 10m));
        Assert.False(await IsPackageAvailableAsync(BrandIds.Okko, "act-95", 20m));

        // Activate ONLY the 10 L voucher — the same single-package act the demo performs.
        Guid tenLitre;
        using (var scope = _fixture.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            tenLitre = await context.FuelVouchers
                .Where(v => v.FuelTypeId == ToUuid("act-95") && v.Liters == 10m)
                .Select(v => v.Id)
                .FirstAsync();
        }

        var (status, body) = await ActivateAsync(tenLitre);
        Assert.Equal(HttpStatusCode.OK, status);
        Assert.DoesNotContain("Cannot activate", body);

        // The whole point: activation is what turns the storefront on. Without this, "import A95 OKKO
        // 10 L and only that package is buyable" would not happen and no other test would say so.
        Assert.True(await IsPackageAvailableAsync(BrandIds.Okko, "act-95", 10m));

        // Its sibling stays dark. Availability is per (brand, fuel, nominal), not per fuel.
        Assert.False(await IsPackageAvailableAsync(BrandIds.Okko, "act-95", 20m));
    }

    /// <summary>
    /// The cost gate, which is why "imported" and "buyable" are two different things: a voucher with
    /// no cost of its own cannot be put on sale, so its package must stay unavailable. Without this the
    /// operator could put on the shelf stock the pricing engine has nothing to price.
    /// </summary>
    [Fact]
    public async Task AnUncostedBatch_CannotBeActivated_AndSoLeavesThePackageDark()
    {
        await AuthenticateStaffAsync();
        await SeedFuelAndPackageAsync(BrandIds.Okko, "act-uncosted", 5m);

        using (var scope = _fixture.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            context.FuelVouchers.Add(ImportedVoucher("okko", "act-uncosted", 5m, costPerLiter: null));
            await context.SaveChangesAsync();
        }

        Assert.False(await IsPackageAvailableAsync(BrandIds.Okko, "act-uncosted", 5m));

        Guid id;
        using (var scope = _fixture.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            id = await context.FuelVouchers.Where(v => v.FuelTypeId == ToUuid("act-uncosted")).Select(v => v.Id).FirstAsync();
        }

        var (_, body) = await ActivateAsync(id);

        Assert.Contains("Cannot activate", body);
        Assert.Contains("cost", body, StringComparison.OrdinalIgnoreCase);
        Assert.False(await IsPackageAvailableAsync(BrandIds.Okko, "act-uncosted", 5m),
            "a batch that could not be activated must not have lit the storefront");
    }

    /// <summary>
    /// The refusal an operator reads is written by the admin, not by this API: the body carries a
    /// machine code and the fuel it refused over, so a panel set to Ukrainian says it in Ukrainian
    /// instead of pasting the English sentence onto a Ukrainian screen. The sentence stays in
    /// <c>detail</c> — for the logs, and for a client that has not learnt the code yet.
    /// </summary>
    [Fact]
    public async Task AFuelWithNoPriceOfItsOwn_RefusesActivationWithACodeAndTheFuel()
    {
        await AuthenticateStaffAsync();

        // The state an imported grade is in until somebody prices it: the card has neither a
        // supplier cost nor a margin, so the price of a litre cannot be computed at all.
        await SeedFuelAndPackageAsync(BrandIds.Okko, "act-unpriced", 3m);

        Guid id;
        using (var scope = _fixture.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var voucher = ImportedVoucher("okko", "act-unpriced", 3m, costPerLiter: 45m);
            id = voucher.Id;
            context.FuelVouchers.Add(voucher);
            await context.SaveChangesAsync();
        }

        var (status, body) = await ActivateAsync(id);

        Assert.Equal(HttpStatusCode.BadRequest, status);
        using var problem = JsonDocument.Parse(body);
        Assert.Equal("fuel_not_priced", problem.RootElement.GetProperty("code").GetString());

        var fuel = problem.RootElement.GetProperty("unpricedFuels")[0];
        Assert.Equal("act-unpriced", fuel.GetProperty("name").GetString());
        Assert.Equal("cost_and_margin", fuel.GetProperty("gap").GetString());
        Assert.Contains("Cannot activate", problem.RootElement.GetProperty("detail").GetString());
    }

    /// <summary>
    /// Activating is the on-switch, so deactivating has to be the off-switch — otherwise the demo's
    /// first step (activate one package) could never be undone without a database edit.
    /// </summary>
    [Fact]
    public async Task DeactivatingAPackage_PutsItBackOutOfStock()
    {
        await AuthenticateStaffAsync();
        await SeedFuelAndPackageAsync(BrandIds.Okko, "act-toggle", 10m);

        using (var scope = _fixture.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            context.FuelVouchers.Add(ImportedVoucher("okko", "act-toggle", 10m, costPerLiter: 45m));
            await context.SaveChangesAsync();
        }

        Guid id;
        using (var scope = _fixture.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            id = await context.FuelVouchers.Where(v => v.FuelTypeId == ToUuid("act-toggle")).Select(v => v.Id).FirstAsync();
        }

        Assert.Equal(HttpStatusCode.OK, (await ActivateAsync(id)).Status);
        Assert.True(await IsPackageAvailableAsync(BrandIds.Okko, "act-toggle", 10m));

        var off = await _client.PostAsJsonAsync("/api/admin/vouchers/bulk-action", new
        {
            action = "deactivate",
            ids = new[] { id }
        });
        Assert.Equal(HttpStatusCode.OK, off.StatusCode);

        Assert.False(await IsPackageAvailableAsync(BrandIds.Okko, "act-toggle", 10m));
    }
}