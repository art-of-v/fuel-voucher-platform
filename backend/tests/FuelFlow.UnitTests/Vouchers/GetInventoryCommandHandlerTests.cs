using FluentAssertions;
using FuelFlow.Features.Vouchers;
using FuelFlow.Features.Vouchers.GetInventory;
using FuelFlow.Features.Vouchers.SharedModels;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using FuelFlow.Testing;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.UnitTests.Vouchers;

/// <summary>
/// The inventory screen groups the voucher table by (brand, fuel, litres) and counts the statuses
/// in each group.
///
/// It used to load EVERY voucher into the process and group the list in memory - the wrong
/// question asked of the table that grows with every voucher the platform ever sells. These pin
/// the answer the database now gives: one row per group, with the counts of each status.
/// </summary>
public sealed class GetInventoryCommandHandlerTests : IDisposable
{
    private readonly ApplicationDbContext _context;
    private readonly GetInventoryCommandHandler _handler;

    public GetInventoryCommandHandlerTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _context = new ApplicationDbContext(options, TestQrCipher.Instance);
        _handler = new GetInventoryCommandHandler(_context);
    }

    public void Dispose()
    {
        _context.Database.EnsureDeleted();
        _context.Dispose();
    }

    [Fact]
    public async Task The_inventory_counts_each_status_inside_its_group()
    {
        var fuelTypeId = Guid.NewGuid();
        _context.FuelTypes.Add(new FuelTypeEntity
        {
            Id = fuelTypeId,
            Name = "Diesel",
            BrandId = Guid.NewGuid(),
            CreatedAtUtc = TestClock.UtcNow,
            UpdatedAtUtc = TestClock.UtcNow
        });

        _context.FuelVouchers.AddRange(
            Voucher(fuelTypeId, VoucherStatus.Available),
            Voucher(fuelTypeId, VoucherStatus.Available),
            Voucher(fuelTypeId, VoucherStatus.Assigned),
            Voucher(fuelTypeId, VoucherStatus.Used),
            Voucher(fuelTypeId, VoucherStatus.Expired));
        await _context.SaveChangesAsync();

        var result = await _handler.HandleAsync(new GetInventoryCommand(), CancellationToken.None);

        var item = result.Inventory.Should().ContainSingle().Subject;
        item.Brand.Should().Be("okko");
        item.FuelTypeId.Should().Be(fuelTypeId);
        item.FuelTypeName.Should().Be("Diesel");
        item.Liters.Should().Be(50);
        item.Available.Should().Be(2);
        item.Assigned.Should().Be(1);
        item.Used.Should().Be(1);
        item.Expired.Should().Be(1);
        item.Total.Should().Be(5);
    }

    [Fact]
    public async Task Vouchers_of_different_fuels_are_separate_groups()
    {
        var firstFuel = Guid.NewGuid();
        var secondFuel = Guid.NewGuid();
        _context.FuelTypes.AddRange(
            new FuelTypeEntity
            {
                Id = firstFuel,
                Name = "Diesel",
                BrandId = Guid.NewGuid(),
                CreatedAtUtc = TestClock.UtcNow,
                UpdatedAtUtc = TestClock.UtcNow
            },
            new FuelTypeEntity
            {
                Id = secondFuel,
                Name = "Petrol",
                BrandId = Guid.NewGuid(),
                CreatedAtUtc = TestClock.UtcNow,
                UpdatedAtUtc = TestClock.UtcNow
            });

        _context.FuelVouchers.AddRange(
            Voucher(firstFuel, VoucherStatus.Available),
            Voucher(secondFuel, VoucherStatus.Available));
        await _context.SaveChangesAsync();

        var result = await _handler.HandleAsync(new GetInventoryCommand(), CancellationToken.None);

        result.Inventory.Should().HaveCount(2);
        result.Inventory.Should().Contain(i => i.FuelTypeId == firstFuel && i.Total == 1);
        result.Inventory.Should().Contain(i => i.FuelTypeId == secondFuel && i.Total == 1);
    }

    [Fact]
    public async Task A_voucher_whose_fuel_type_is_gone_falls_back_to_the_id()
    {
        // The fuel type row is missing, so there is no name to show. The id is shown instead of a
        // blank cell, which is what the navigation-based read used to do.
        var missingFuelType = Guid.NewGuid();
        _context.FuelVouchers.Add(Voucher(missingFuelType, VoucherStatus.Available));
        await _context.SaveChangesAsync();

        var result = await _handler.HandleAsync(new GetInventoryCommand(), CancellationToken.None);

        var item = result.Inventory.Should().ContainSingle().Subject;
        item.FuelTypeName.Should().Be(missingFuelType.ToString());
    }

    private static FuelVoucher Voucher(Guid fuelTypeId, VoucherStatus status) => new()
    {
        Id = Guid.NewGuid(),
        Brand = "okko",
        BrandId = BrandIds.ForSlug("okko"),
        FuelTypeId = fuelTypeId,
        Liters = 50,
        VoucherNumber = Guid.NewGuid().ToString("N"),
        QrPayload = Guid.NewGuid().ToString("N"),
        SupplierExpirationDate = new DateOnly(2027, 1, 1),
        CustomerExpirationDate = new DateOnly(2027, 1, 1),
        Status = status,
        CreatedAtUtc = TestClock.UtcNow,
        UpdatedAtUtc = TestClock.UtcNow
    };
}
