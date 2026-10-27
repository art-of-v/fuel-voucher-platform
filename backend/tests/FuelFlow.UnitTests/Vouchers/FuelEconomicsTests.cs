using FluentAssertions;
using FuelFlow.SharedKernel.Domain;
using Xunit;

namespace FuelFlow.UnitTests.Vouchers;

/// <summary>
/// The rule behind the shelf gate and the fuel card's refusals (planning #680), on its own.
/// </summary>
public sealed class FuelEconomicsTests
{
    [Fact]
    public void A_fuel_with_no_cost_and_no_margin_has_no_price()
    {
        var state = FuelEconomics.State(null, null);

        state.IsPriced.Should().BeFalse();
        FuelEconomics.Missing(state).Should().Be("no supplier cost and no margin");
    }

    [Fact]
    public void A_cost_of_zero_is_not_a_cost()
    {
        // A litre we paid nothing for has no cost to guard a sale against - and the below-cost guard
        // reads a zero cost as unknown cost too.
        FuelEconomics.State(0m, 5m).HasCost.Should().BeFalse();
        FuelEconomics.State(0m, 5m).IsPriced.Should().BeFalse();
    }

    [Fact]
    public void A_margin_of_zero_is_a_decision_and_not_a_gap()
    {
        // The fuel is sold at cost. That is a decision, and the database accepts it
        // (ck_fuel_types_margin_signs pins the margin at zero or more).
        var state = FuelEconomics.State(50m, 0m);

        state.HasMargin.Should().BeTrue();
        state.IsPriced.Should().BeTrue();
    }

    [Fact]
    public void A_priced_fuel_produces_no_refusal()
    {
        FuelEconomics.ActivationRefusal([("A-95", FuelEconomics.State(50m, 5m))])
            .Should().BeEmpty();
    }

    [Fact]
    public void The_refusal_names_every_fuel_and_what_each_of_them_is_missing()
    {
        // The operator's next two actions are different for each gap: the cost arrives with the
        // batch, the margin is set on the fuel's card.
        var refusal = FuelEconomics.ActivationRefusal(
        [
            ("A-95", FuelEconomics.State(50m, null)),
            ("ДП ЄВРО", FuelEconomics.State(null, null))
        ]);

        refusal.Should().Contain("A-95 (no margin)");
        refusal.Should().Contain("ДП ЄВРО (no supplier cost and no margin)");
    }

    [Fact]
    public void The_unpriced_fuels_carry_the_token_the_admin_translates()
    {
        // The English sentence is for the log. What the operator reads is written by the admin, in the
        // language the panel speaks, and it can only do that from a token — not from prose.
        FuelEconomics.Unpriced([
            ("A-95", FuelEconomics.State(50m, null)),
            ("ДП", FuelEconomics.State(null, null)),
            ("Паливо без ціни", FuelEconomics.State(null, 5m)),
            ("Продане", FuelEconomics.State(50m, 5m))
        ]).Should().Equal(
            new UnpricedFuel("A-95", "margin"),
            new UnpricedFuel("ДП", "cost_and_margin"),
            new UnpricedFuel("Паливо без ціни", "cost"));
    }

    [Fact]
    public void A_zero_margin_is_a_decision_and_carries_no_gap()
    {
        FuelEconomics.State(50m, 0m).Gap.Should().BeNull();
        FuelEconomics.Unpriced([("A-95", FuelEconomics.State(50m, 0m))]).Should().BeEmpty();
    }

    [Fact]
    public void A_zero_cost_is_a_fault()
    {
        var fault = FuelEconomics.Fault(0m, 5m, allowBelowCost: false);

        fault.Should().NotBeNull();
        fault!.Value.Field.Should().Be("SupplierPricePerLiter");
    }

    [Fact]
    public void A_negative_margin_is_a_fault()
    {
        var fault = FuelEconomics.Fault(50m, -1m, allowBelowCost: false);

        fault.Should().NotBeNull();
        fault!.Value.Field.Should().Be("MarginUahPerLiter");
    }

    [Fact]
    public void The_below_cost_opt_in_without_a_cost_is_a_fault()
    {
        var fault = FuelEconomics.Fault(null, 5m, allowBelowCost: true);

        fault.Should().NotBeNull();
        fault!.Value.Field.Should().Be("AllowBelowCost");
    }

    [Fact]
    public void Sound_numbers_have_no_fault()
    {
        FuelEconomics.Fault(50m, 0m, allowBelowCost: true).Should().BeNull();
        FuelEconomics.Fault(null, null, allowBelowCost: false).Should().BeNull(
            "a fuel that states neither number yet is not a fault - it is a fuel nobody has priced");
    }
}
