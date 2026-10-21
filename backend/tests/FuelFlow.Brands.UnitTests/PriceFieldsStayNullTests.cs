using System.Text.Json;
using FuelFlow.Features.Brands;
using FuelFlow.Features.Orders.CreateCheckout;
using FuelFlow.SharedKernel.Domain;
using FluentAssertions;
using Xunit;

namespace FuelFlow.Brands.UnitTests;

/// <summary>
/// A blank cost must stay blank. It must not become a zero.
/// </summary>
/// <remarks>
/// <para>
/// The write path takes the whole admin card as its payload, so a fuel the catalogue import
/// created - which has no cost, because the import knows the column price and nothing about
/// what we pay - arrives with <c>supplierPricePerLiter: null</c>. The DTO's property was a
/// plain <c>decimal</c>, and the wire turns a JSON null into a zero before the value reaches
/// it. Two things followed from that, and both of them are money:
/// </para>
/// <list type="number">
/// <item>the customer price became <c>min(0 + margin, column - discount)</c> - the MARGIN, not the
/// price. A grade worth 92.90 became a 2.75 bargain, or 0.00 when the margin was blank too;</item>
/// <item>the below-cost guard went blind, because it only fires when the cost is greater than
/// zero: it reads "we paid nothing" as "nothing can be below what we paid".</item>
/// </list>
/// <para>
/// So the properties are nullable, and this is the test that says so. It cannot even be
/// WRITTEN against the old type: <c>decimal</c> cannot be null, so the state the old code
/// could not express is the state this test is about.
/// </para>
/// </remarks>
public sealed class PriceFieldsStayNullTests
{
    private const string JsonWithBlankCost = """
        {
          "id": "01a1557b-38b0-7ce5-bf96-e2aa5288ae66",
          "name": "A-95",
          "supplierPricePerLiter": null,
          "marginUahPerLiter": null,
          "finalPricePerLiter": null,
          "pumpPricePerLiter": 92.90,
          "minDiscountPerLiter": 0
        }
        """;

    [Fact]
    public void A_blank_cost_arrives_as_null_and_not_as_zero()
    {
        var fuel = JsonSerializer.Deserialize<BrandFuelDto>(JsonWithBlankCost)!;

        // The whole point: NOT 0. A zero here is a fuel that believes it cost nothing.
        fuel.SupplierPricePerLiter.Should().BeNull();
        fuel.MarginUahPerLiter.Should().BeNull();
    }

    [Fact]
    public void A_blank_cost_gives_no_price_of_ours()
    {
        var fuel = JsonSerializer.Deserialize<BrandFuelDto>(JsonWithBlankCost)!;

        // There is no price to publish, and the model says so rather than inventing one: the
        // storefront shows the column beside it and "not in stock" next to that.
        FuelPricing.FinalPerLiterOrNull(
            fuel.SupplierPricePerLiter, fuel.MarginUahPerLiter)
            .Should().BeNull();
    }

    [Fact]
    public void A_stated_cost_and_margin_still_price_the_fuel()
    {
        // The change must not break the ordinary case: a priced fuel keeps its price.
        FuelPricing.FinalPerLiterOrNull(40m, 3m, 92.90m, 0m).Should().Be(43m);
    }

    [Fact]
    public void A_fuel_with_no_cost_has_no_verdict_rather_than_a_free_one()
    {
        // The guard's own answer when it cannot compare. It is NOT "below cost" - it is
        // "cannot tell" - and a sale on a "cannot tell" is a sale nobody priced.
        var package = new SellablePackage
        {
            PackageId = "p-1",
            BrandId = Guid.NewGuid(),
            BrandSlug = "okko",
            FuelTypeId = Guid.NewGuid(),
            Liters = 10m,
            SupplierPricePerLiter = null,
            MarginUahPerLiter = null,
            PumpPricePerLiter = 92.90m,
        };

        ServerPricing.TryGetBelowCostVerdict(package, 0m)
            .Should().Be(ServerPricing.BelowCostVerdict.UnknownCost);
    }
}
