using FluentAssertions;
using FuelFlow.Features.Brands.DeleteFuelType;
using FuelFlow.Features.Vouchers;
using FuelFlow.Features.Vouchers.SharedModels;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FuelFlow.UnitTests.Brands;

/// <summary>
/// Deleting a fuel type used to REMOVE the row, and <c>fuel_vouchers.fuel_type_id</c> CASCADEs from
/// it - so deleting a fuel silently destroyed every voucher sold against it, including paid-for paper
/// a customer is holding. That is the history loss #217 exists to prevent.
/// </summary>
public sealed class DeleteFuelTypeSoftDeleteTests : IDisposable
{
    private readonly ApplicationDbContext _context;

    public DeleteFuelTypeSoftDeleteTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"fueltype-{Guid.NewGuid():N}")
            .Options;
        _context = new ApplicationDbContext(options, TestQrCipher.Instance);
    }

    public void Dispose() => _context.Dispose();

    private async Task<(FuelTypeEntity FuelType, List<FuelVoucher> Vouchers)> SeedFuelWithVouchersAsync()
    {
        var fuel = new FuelTypeEntity
        {
            Id = "okko-a95",
            Name = "А95",
            BrandId = "okko",
            BasePrice = 50m,
            DiscountPrice = 45m,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow,
        };
        _context.FuelTypes.Add(fuel);

        var vouchers = new List<FuelVoucher>();
        for (var i = 0; i < 3; i++)
        {
            var voucher = new FuelVoucher
            {
                Id = Guid.NewGuid(),
                Brand = "OKKO",
                FuelTypeId = fuel.Id,
                Liters = 10m,
                VoucherNumber = $"V-PAID-{i}",
                QrPayload = Guid.NewGuid().ToString(),
                Status = VoucherStatus.Assigned,
                SupplierExpirationDate = DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(3)),
                CustomerExpirationDate = DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(3)),
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow,
            };
            _context.FuelVouchers.Add(voucher);
            vouchers.Add(voucher);
        }

        await _context.SaveChangesAsync();
        return (fuel, vouchers);
    }

    [Fact]
    public async Task Deleting_A_Fuel_Type_Archives_It_And_Leaves_Its_Vouchers_Alive()
    {
        var (fuel, vouchers) = await SeedFuelWithVouchersAsync();
        var handler = new DeleteFuelTypeCommandHandler(_context);

        (await handler.HandleAsync(new DeleteFuelTypeCommand(fuel.Id))).Should().BeTrue();

        // The fuel leaves the catalog...
        (await _context.FuelTypes.AnyAsync(f => f.Id == fuel.Id)).Should().BeFalse(
            "the !IsDeleted query filter must hide it");

        // ...but the row itself survives, because historical rows point at it.
        var archived = await _context.FuelTypes.IgnoreQueryFilters().SingleAsync(f => f.Id == fuel.Id);
        archived.IsDeleted.Should().BeTrue();
        archived.DeletedAtUtc.Should().NotBeNull();

        // The part that matters: no paid-for paper was destroyed.
        (await _context.FuelVouchers.IgnoreQueryFilters().CountAsync(v => vouchers.Contains(v)))
            .Should().Be(3, "deleting a fuel must never take the vouchers sold against it");
    }

    [Fact]
    public async Task An_Archived_Fuel_Is_Not_Deletable_Twice()
    {
        var (fuel, _) = await SeedFuelWithVouchersAsync();
        var handler = new DeleteFuelTypeCommandHandler(_context);

        (await handler.HandleAsync(new DeleteFuelTypeCommand(fuel.Id))).Should().BeTrue();
        (await handler.HandleAsync(new DeleteFuelTypeCommand(fuel.Id))).Should().BeFalse(
            "the query filter already hides it, so a second delete finds nothing");
    }
}