using FluentAssertions;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FuelFlow.UnitTests.Stations;

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
            StationId = "okko",
            BasePrice = 0m,
            DiscountPrice = 0m,
            MarginUahPerLiter = 2m,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };
        var offer = new FuelOffer
        {
            Id = "projection-fuel",
            StationId = "okko",
            FuelTypeId = fuel.Id,
            DataKind = DataKinds.Real,
            SupplierPricePerLiter = 49m,
            MarginUahPerLiter = 2m,
            MarginPercent = 4m,
            PumpPricePerLiter = 55m,
            MinDiscountPerLiter = 0.5m,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };
        var volume = new FuelOfferVolume
        {
            Id = "projection-vol",
            OfferId = offer.Id,
            Liters = 10m,
            PriceOverrideUah = null,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };

        _context.FuelTypes.Add(fuel);
        _context.FuelOffers.Add(offer);
        _context.FuelOfferVolumes.Add(volume);
        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();

        // Filtered by id: EnsureCreated also loads the HasData catalogue into this context, and this
        // test is about one pair of rows, not about whatever the seed brought with it.
        var projected = await _context.FuelOfferVolumes
            .Where(v => v.Id == volume.Id)
            .Project()
            .SingleAsync();
        var composed = SellablePackage.From(
            await _context.FuelOffers.SingleAsync(o => o.Id == offer.Id),
            await _context.FuelOfferVolumes.SingleAsync(v => v.Id == volume.Id),
            // Third of the join, for the same reason the projection takes it: the margin is the fuel's.
            await _context.FuelTypes.SingleAsync(f => f.Id == offer.FuelTypeId));

        projected.Should().BeEquivalentTo(composed);

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