using FluentAssertions;
using FuelFlow.Features.Vouchers;
using FuelFlow.Features.Vouchers.GetAdminVouchers;
using FuelFlow.Features.Vouchers.SharedModels;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using FuelFlow.Testing;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static FuelFlow.SharedKernel.Domain.FuelIdMapper;

namespace FuelFlow.IntegrationTests;

/// <summary>
/// The sorting added for #227 has to survive a real Postgres, and two of its properties only exist
/// there.
///
/// <b>Nulls last.</b> PostgreSQL sorts NULLs <i>first</i> on a descending sort (the opposite of SQL
/// Server). Written the obvious way, "most expensive first" would therefore open on the vouchers
/// nobody has priced yet — the rows an operator most needs to find, presented as the dearest in the
/// stock. The unit tests cannot catch this: the in-memory brand has no such rule.
/// </summary>
[Collection("Integration Tests")]
public sealed class VoucherListSortingIntegrationTests : IClassFixture<TestDatabaseFixture>
{
    private readonly TestDatabaseFixture _fixture;

    public VoucherListSortingIntegrationTests(TestDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task A_descending_cost_sort_leaves_the_unpriced_vouchers_last()
    {
        var seeded = await SeedAsync(costs: new decimal?[] { 94.90m, null, 50m });

        var rows = await PageAsync(seeded.Brand, sortBy: "costPerLiter", sortDirection: "desc");

        rows.Select(r => r.CostPerLiter).Should().ContainInOrder(
            new decimal?[] { 94.90m, 50m, null },
            "Postgres puts NULLs first on a descending sort unless the query says otherwise, and an unknown cost is not the dearest voucher in the stock");
    }

    [Fact]
    public async Task An_ascending_cost_sort_also_leaves_the_unpriced_vouchers_last()
    {
        var seeded = await SeedAsync(costs: new decimal?[] { 94.90m, null, 50m });

        var rows = await PageAsync(seeded.Brand, sortBy: "costPerLiter", sortDirection: "asc");

        rows.Select(r => r.CostPerLiter).Should().ContainInOrder(new decimal?[] { 50m, 94.90m, null });
    }

    [Fact]
    public async Task A_descending_supplier_sort_leaves_the_unsupplied_vouchers_last()
    {
        var seeded = await SeedAsync(costs: new decimal?[] { 50m, null, 50m });

        var rows = await PageAsync(seeded.Brand, sortBy: "supplier", sortDirection: "desc");

        rows.Should().HaveCount(3);
        rows[2].SupplierName.Should().BeNull("a voucher with no supplier is not alphabetically first");
        rows.Take(2).All(r => r.SupplierName != null).Should().BeTrue();
    }

    private async Task<List<AdminVoucherListItemDto>> PageAsync(string brand, string sortBy, string sortDirection)
    {
        using var ctx = CreateContext();
        var handler = new GetAdminVouchersQueryHandler(ctx);

        var response = await handler.HandleAsync(new GetAdminVouchersQuery(
            SortBy: sortBy,
            SortDirection: sortDirection,
            Brand: brand));

        return response.Data;
    }

    private sealed record Seeded(string Brand);

    /// <summary>
    /// Every row goes under its own brand, so a query filtered on it sees exactly what this test
    /// seeded and nothing another test left behind in the shared container.
    /// </summary>
    private async Task<Seeded> SeedAsync(decimal?[] costs)
    {
        var tag = Guid.NewGuid().ToString("N")[..12];

        var supplierA = Guid.NewGuid();
        var supplierB = Guid.NewGuid();
        var fuelTypeId = $"sort-{tag}";
        var brand = $"sort{tag}";
        var brandId = Guid.CreateVersion7();

        using var ctx = CreateContext();
        await ctx.Database.MigrateAsync();

        ctx.Suppliers.AddRange(
            new Supplier { Id = supplierA, Name = $"SORT-{tag}-Альфа", EdrIpn = TestEdrIpns.Next(), CreatedAtUtc = TestClock.UtcNow, UpdatedAtUtc = TestClock.UtcNow },
            new Supplier { Id = supplierB, Name = $"SORT-{tag}-Бета", EdrIpn = TestEdrIpns.Next(), CreatedAtUtc = TestClock.UtcNow, UpdatedAtUtc = TestClock.UtcNow });

        // A network of its own, so the brand on the vouchers is a brand that EXISTS and is the brand
        // of the fuel they claim. The database refuses a voucher whose brand is not its fuel's
        // brand (20261020164744_TheBrandCannotDisagreeWithItsFuel), and the name this test
        // invented is not a network anybody sells fuel under. Giving the test a real row keeps the
        // isolation it was after - the query is filtered on the brand - and makes the row true.
        ctx.Brands.Add(new Brand
        {
            Id = brandId,
            Slug = brand,
            Name = $"SORT-{tag}",
            LogoText = "SORT",
            CreatedAtUtc = TestClock.UtcNow,
            UpdatedAtUtc = TestClock.UtcNow
        });

        ctx.FuelTypes.Add(new FuelTypeEntity
        {
            Id =   ToUuid(fuelTypeId),
            Name = $"A-95 {tag}",
            BrandId = brandId,
            BasePrice = 55,
            DiscountPrice = 52,
            CreatedAtUtc = TestClock.UtcNow,
            UpdatedAtUtc = TestClock.UtcNow
        });

        foreach (var cost in costs)
        {
            // A null carries no supplier either, on purpose: "we did not record the cost" and "we did
            // not record who we bought it from" are independent facts, and the sort has to cope with
            // one without the other. The two priced vouchers go to different suppliers so the
            // descending order of the supplier column is actually observable.
            ctx.FuelVouchers.Add(new FuelVoucher
            {
                Id = Guid.NewGuid(),
                QrPayload = $"sort-{tag}-{Guid.NewGuid():N}",
                VoucherNumber = $"sort-{tag}-{Guid.NewGuid():N}",
                // The brand's key is the network this test created, not a derived one: the network
                // is a row of its own with an id of its own, so the voucher's key is that id.
                BrandId = brandId,
                Brand = brand,
                FuelTypeId =   ToUuid(fuelTypeId),
                Liters = 5m,
                SupplierExpirationDate = DateOnly.FromDateTime(TestClock.UtcNow.AddMonths(3)),
                CustomerExpirationDate = DateOnly.FromDateTime(TestClock.UtcNow.AddMonths(3)),
                Status = VoucherStatus.Available,
                CreatedAtUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                UpdatedAtUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                SupplierId = cost == null ? null : cost > 90m ? supplierA : supplierB,
                CostPerLiter = cost
            });
        }

        await ctx.SaveChangesAsync();
        return new Seeded(brand);
    }

    private ApplicationDbContext CreateContext()
        => new(
            new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseNpgsql(_fixture.ConnectionString)
                .Options,
            TestQrCipher.Instance
        );
}
