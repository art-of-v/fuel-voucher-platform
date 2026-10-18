using FluentAssertions;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FuelFlow.UnitTests.Brands;

/// <summary>
/// <see cref="SellablePackageProjection"/> and <see cref="SellablePackage.From"/> are the same shape
/// written twice — one as an EF <c>Select</c>, one as an object initialiser — because EF cannot serve
/// both a server-side projection and an in-memory composition from one definition. Both class remarks
/// have claimed a test pins them together; none existed, and this is it.
/// <para>
/// It matters more since 2026-10-17: the derived customer price moved out of the projection and into a
/// computed member, so the two lists are shorter and a forgotten <c>init</c> property would leave the
/// storefront reading a null that the in-memory path fills in.
/// </para>
/// </summary>
public sealed class SellablePackageProjectionTests : IDisposable
{
    private readonly ApplicationDbContext _context;

    public SellablePackageProjectionTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        _context = new ApplicationDbContext(options, TestQrCipher.Instance);
        _context.Database.EnsureCreated();
    }

    public void Dispose() => _context.Dispose();

    [Fact]
    public async Task TheProjection_AndFrom_AgreeOnEveryField()
    {
        // The margin is the fuel's, and this is the projection that proves both halves of the join agree -
        // so the fuel is a real row here rather than a value on the offer. An in-memory provider cannot
        // translate `Offer.FuelType.MarginUahPerLiter`, which is why this is one of the fixtures that
        // wants a real database.
        var fuel = new FuelTypeEntity
        {
            Id = "projection-fuel",
            Name = "А-95 (projection fixture)",
            BrandId = BrandIds.Okko,
            BasePrice = 0m,
            DiscountPrice = 0m,
            MarginUahPerLiter = 2m,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };
        var offer = new FuelOffer
        {
            Id = "projection-fuel",
            BrandId = BrandIds.Okko,
            FuelTypeId = fuel.Id,
            DataKind = DataKinds.Real,
            SupplierPricePerLiter = 49m,
            MarginPercent = 4m,
            PumpPricePerLiter = 55m,
            MinDiscountPerLiter = 0.5m,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };

        var nominal = new FuelNominal
        {
            FuelTypeId = fuel.Id,
            Liters = 10m
        };

        _context.FuelTypes.Add(fuel);
        _context.FuelOffers.Add(offer);
        _context.FuelNominals.Add(nominal);
        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();

        // Filtered by id: EnsureCreated also loads the HasData catalogue into this context, and this
        // test is about one offer, not about whatever the seed brought with it.
        var projected = await _context.FuelOffers
            .Where(o => o.Id == offer.Id)
            .Project(_context.FuelNominals.Where(n => n.FuelTypeId == fuel.Id))
            .SingleAsync();
        var composed = SellablePackage.From(
            await _context.FuelOffers.SingleAsync(o => o.Id == offer.Id),
            // Third of the join, for the same reason the projection takes it: the margin is the fuel's.
            await _context.FuelTypes.SingleAsync(f => f.Id == offer.FuelTypeId),
// The slug is the fourth half of the identity: it is what the storefront groups and labels
            // by, and what the stock key matches a voucher's brand NAME against.
            nominal.Liters, "okko");

        projected.Should().BeEquivalentTo(composed);

        // And the id both sides synthesise is the same string, or a package the shop advertised
        // could not be resolved at checkout.
        projected.PackageId.Should().Be(PackageIds.For(offer.Id, 10m));

        // The price both paths resolve: derived, and equal on both sides.
        projected.ComputedFinalPricePerLiter.Should().Be(51m);
        composed.ComputedFinalPricePerLiter.Should().Be(projected.ComputedFinalPricePerLiter);
    }

    [Fact]
    public void TheComputedPrice_IsTheFormula_NotAStoredColumn()
    {
        var package = new SellablePackage
        {
            SupplierPricePerLiter = 49m,
            MarginUahPerLiter = 2m,
            PumpPricePerLiter = 50m,
            MinDiscountPerLiter = 0m
        };

        // The ceiling binds, so the customer price is 50 and not the 51 a cost-plus reading would give.
        package.ComputedFinalPricePerLiter.Should().Be(50m);
    }
}
