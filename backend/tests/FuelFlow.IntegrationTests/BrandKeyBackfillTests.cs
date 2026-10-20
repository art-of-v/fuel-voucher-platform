using FluentAssertions;
using FuelFlow.Features.Orders.SharedModels;
using FuelFlow.Features.Vouchers;
using FuelFlow.Features.Vouchers.SharedModels;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Xunit;

namespace FuelFlow.IntegrationTests;

/// <summary>
/// The backfill of the brand key: a row's brand is the brand of the fuel it names.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why it is tested by migrating.</b> The backfill only ever runs once, on a database that
/// already has rows - so the only way to test it is to have such a database: insert the rows,
/// migrate back to the migration before, and migrate forward again. A test that inserted the
/// rows after the migration would prove nothing about the backfill at all.
/// </para>
/// <para>
/// The rows are inserted through the model, so the test cannot drift from the shape the
/// application writes: the brand name, and the fuel the row names.
/// </para>
/// </remarks>
[Collection("Integration Tests")]
public sealed class BrandKeyBackfillTests : IClassFixture<TestDatabaseFixture>
{
    /// <summary>The migration before the one that adds the key.</summary>
    private const string BeforeTheKey = "20261020164744_TheBrandCannotDisagreeWithItsFuel";

    private readonly TestDatabaseFixture _fixture;

    public BrandKeyBackfillTests(TestDatabaseFixture fixture) => _fixture = fixture;

    private ApplicationDbContext CreateContext()
        => new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(_fixture.ConnectionString)
            .Options, TestQrCipher.Instance);

    [Fact]
    public async Task The_Brand_Of_A_Row_Is_The_Brand_Of_Its_Fuel()
    {
        var userId = Guid.NewGuid();
        var fuelTypeId = Guid.NewGuid();
        var voucherId = Guid.NewGuid();
        var orderId = Guid.NewGuid();

        // The seeded network, used as it is: its slug is unique, so a second brand with the same
        // slug is refused.
        Guid brandId;
        await using (var lookup = CreateContext())
        {
            brandId = await lookup.Brands
                .Where(b => b.Slug == "okko")
                .Select(b => b.Id)
                .FirstAsync();
        }

        await using (var seed = CreateContext())
        {
            seed.FuelTypes.Add(new FuelTypeEntity
            {
                Id = fuelTypeId,
                Name = "Diesel " + Guid.NewGuid().ToString("N")[..8],
                BrandId = brandId,
                BasePrice = 50m,
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow
            });

            seed.Users.Add(new User
            {
                Id = userId,
                PhoneNumber = $"+38{Guid.NewGuid():N}"[..20],
                IsActive = true,
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow
            });

            seed.FuelVouchers.Add(new FuelVoucher
            {
                Id = voucherId,
                Brand = "okko",
                FuelTypeId = fuelTypeId,
                Liters = 50m,
                VoucherNumber = Guid.NewGuid().ToString("N"),
                QrPayload = Guid.NewGuid().ToString("N"),
                Status = VoucherStatus.Available,
                SupplierExpirationDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(90),
                CustomerExpirationDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(90),
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow
            });

            var order = new Order
            {
                Id = orderId,
                UserId = userId,
                Price = 100m,
                Status = OrderStatus.PendingFulfillment,
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow
            };

            order.LineItems.Add(new OrderLineItem
            {
                Id = Guid.NewGuid(),
                OrderId = orderId,
                Brand = "okko",
                FuelTypeId = fuelTypeId,
                Liters = 50m,
                Quantity = 1,
                UnitPrice = 100m,
                LineTotal = 100m
            });

            seed.Orders.Add(order);
            await seed.SaveChangesAsync();
        }

        await using var context = CreateContext();
        var migrator = context.Database.GetService<IMigrator>();

        // Back to before the key, then forward over it: the backfill runs against the rows that
        // are already there, which is the only case that matters.
        await migrator.MigrateAsync(BeforeTheKey);
        await migrator.MigrateAsync();

        var voucherBrand = await context.Database
            .SqlQueryRaw<Guid>(
                """
                SELECT brand_id AS "Value"
                FROM fuel_vouchers
                WHERE id = {0}
                """, voucherId)
            .ToListAsync();

        voucherBrand.Should().ContainSingle().Which.Should().Be(brandId,
            "the brand of the row is the brand of the fuel it names");

        var lineBrand = await context.Database
            .SqlQueryRaw<Guid>(
                """
                SELECT brand_id AS "Value"
                FROM order_line_items
                WHERE order_id = {0}
                """, orderId)
            .ToListAsync();

        lineBrand.Should().ContainSingle().Which.Should().Be(brandId);
    }
}
