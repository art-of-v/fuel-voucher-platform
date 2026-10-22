using FuelFlow.Features.Brands.GetPublicFuelTypes;
using FuelFlow.Features.Vouchers.Availability;
using FuelFlow.Features.Vouchers.SharedModels;
using FuelFlow.Features.Vouchers;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using FuelFlow.Testing;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static FuelFlow.SharedKernel.Domain.FuelIdMapper;

namespace FuelFlow.IntegrationTests;

/// <summary>
/// The public fuel list carries three numbers, and which one of them is THE price depends on whether
/// the shop can sell the fuel.
/// </summary>
/// <remarks>
/// <para>
/// A fuel that can be bought is shown at the price we charge: the cost plus the margin, capped at the
/// pump price. A fuel that cannot be bought yet - an imported one, which nobody has costed - is shown
/// at the column price, because that is what the market charges and it is the only honest number to
/// show. Showing a zero, or a market price presented as ours, is a lie the customer pays for.
/// </para>
/// <para>
/// The availability is the caller's own pool, because a QA account and a customer can legitimately
/// see different answers for the same fuel.
/// </para>
/// </remarks>
[Collection("Integration Tests")]
public sealed class PublicFuelTypesIntegrationTests : IClassFixture<TestDatabaseFixture>
{
    private readonly TestDatabaseFixture _fixture;

    public PublicFuelTypesIntegrationTests(TestDatabaseFixture fixture) => _fixture = fixture;

    private ApplicationDbContext CreateContext()
        => new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(_fixture.ConnectionString)
            .Options, TestQrCipher.Instance);

    private async Task<(Guid BrandId, Guid FuelId, Guid OtherFuelId)> SeedAsync(
        decimal? cost,
        decimal? margin,
        bool withStock)
    {
        var brandId = Guid.NewGuid();
        var slug = $"slug-{Guid.NewGuid():N}"[..20];
        var fuelId = ToUuid($"priced-{Guid.NewGuid():N}");
        var otherFuelId = ToUuid($"unpriced-{Guid.NewGuid():N}");

        await using var context = CreateContext();
        context.Brands.Add(new Brand
        {
            Id = brandId,
            Slug = slug,
            Name = "TEST",
            LogoText = "TEST",
            Color = "#000",
            CreatedAtUtc = TestClock.UtcNow,
            UpdatedAtUtc = TestClock.UtcNow
        });

        context.FuelTypes.Add(new FuelTypeEntity
        {
            Id =   fuelId,
            BrandId = brandId,
            Name = "А-95",
            SupplierPricePerLiter = cost,
            MarginUahPerLiter = margin,
            PumpPricePerLiter = 92.90m,
            CreatedAtUtc = TestClock.UtcNow,
            UpdatedAtUtc = TestClock.UtcNow
        });

        context.FuelTypes.Add(new FuelTypeEntity
        {
            Id =   otherFuelId,
            BrandId = brandId,
            Name = "ДП",
            // No cost, no margin: the state an imported fuel is in until an operator prices it.
            PumpPricePerLiter = 101.90m,
            CreatedAtUtc = TestClock.UtcNow,
            UpdatedAtUtc = TestClock.UtcNow
        });

        if (withStock)
        {
            // The size, because stock is counted per (brand, fuel, SIZE) - a voucher for a size the
            // fuel is not sold in is not stock behind it.
            context.FuelNominals.Add(new FuelNominal
            {
                FuelTypeId =   fuelId,
                Liters = 10m,
                CreatedAtUtc = TestClock.UtcNow,
                UpdatedAtUtc = TestClock.UtcNow
            });

            context.FuelVouchers.Add(new FuelVoucher
            {
                   Id = Guid.NewGuid(),
                   // The brand's key is the network this test created - a runtime network with an
                   // id of its own, so the voucher's key is that id.
                   BrandId = brandId,
                   Brand = slug,
                FuelTypeId =   fuelId,
                Liters = 10m,
                Status = VoucherStatus.Available,
                DataKind = DataKinds.Real,
                // The stock query skips expired vouchers, and an unset date reads as the year one.
                SupplierExpirationDate = DateOnly.FromDateTime(TestClock.UtcNow.AddMonths(1)),
                VoucherNumber = $"V-{Guid.NewGuid():N}",
                QrPayload = $"9018$500$;{Guid.NewGuid():N}",
                CreatedAtUtc = TestClock.UtcNow,
                UpdatedAtUtc = TestClock.UtcNow
            });
        }

        await context.SaveChangesAsync();
        return (brandId, fuelId, otherFuelId);
    }

    private async Task<List<PublicFuelTypeResponse>> ListAsync()
    {
        await using var context = CreateContext();
        var stock = new PackageStockAvailability(
            context,
            new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build());
        var handler = new GetPublicFuelTypesQueryHandler(context, stock);
        return await handler.HandleAsync(new GetPublicFuelTypesQuery(null));
    }

    [Fact]
    public async Task The_Two_Retired_Names_Carry_The_Computed_Prices()
    {
        var (_, fuelId, _) = await SeedAsync(cost: 40m, margin: 3m, withStock: true);

        var fuel = (await ListAsync()).Single(f => f.Id == fuelId.ToString());

        // The columns behind these two names are gone from fuel_types, and the names stay: they are
        // what the app has always read. What they carry is the computed price - basePrice is the
        // COLUMN price the saving is measured against, and discountPrice is the price we charge.
        // The same two words in the table and in this contract meant two different facts, and the
        // table no longer has an opinion.
        fuel.BasePrice.Should().Be(92.90m, "the reference is the column price");
        fuel.DiscountPrice.Should().Be(43m, "the price we charge is the cost plus the margin");
    }

    [Fact]
    public async Task A_fuel_the_shop_can_sell_carries_our_price()
    {
        var (_, fuelId, _) = await SeedAsync(cost: 40m, margin: 3m, withStock: true);

        var fuel = (await ListAsync()).Single(f => f.Id == fuelId.ToString());

        // 40 + 3 = 43: our price is the cost plus the margin, and the column price (92.90) is the
        // reference it is shown against, not a cap.
        fuel.FinalPricePerLiter.Should().Be(43m);
        fuel.PumpPricePerLiter.Should().Be(92.90m);
        fuel.IsAvailable.Should().BeTrue("there is stock behind it");
    }

    [Fact]
    public async Task A_fuel_nobody_has_priced_carries_no_price_of_ours_and_is_not_available()
    {
        var (_, _, otherFuelId) = await SeedAsync(cost: 40m, margin: 3m, withStock: false);

        var fuel = (await ListAsync()).Single(f => f.Id == otherFuelId.ToString());

        // No cost and no margin, so there is no price of ours - null, not zero.
        fuel.FinalPricePerLiter.Should().BeNull();
        // The column price is still there, and it is what the row shows.
        fuel.PumpPricePerLiter.Should().Be(101.90m);
        fuel.IsAvailable.Should().BeFalse();
    }

    [Fact]
    public async Task A_fuel_with_stock_but_no_margin_is_available_and_has_no_price_of_ours()
    {
        // The awkward middle: the vouchers are there, but nobody has costed the fuel. It is buyable -
        // the stock is what makes that true - and it still has no price of ours, so the client falls
        // back to the column price.
        var (_, fuelId, _) = await SeedAsync(cost: null, margin: null, withStock: true);

        var fuel = (await ListAsync()).Single(f => f.Id == fuelId.ToString());

        fuel.IsAvailable.Should().BeTrue();
        fuel.FinalPricePerLiter.Should().BeNull();
        fuel.PumpPricePerLiter.Should().Be(92.90m);
    }
}
