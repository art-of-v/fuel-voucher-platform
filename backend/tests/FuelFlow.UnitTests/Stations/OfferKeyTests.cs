using FluentAssertions;
using FuelFlow.SharedKernel.Domain;
using Xunit;

namespace FuelFlow.UnitTests.Brands;

/// <summary>
/// The offer's key is its fuel type id, with <c>-qa</c> for the rehearsal pool, derived by
/// <see cref="OfferKeys"/>.
/// </summary>
/// <remarks>
/// <para>
/// The rule is written down three times — here in C#, and inline in SQL in the two migrations that
/// have to produce or repair these ids, because a migration cannot call into the assembly it migrates
/// from. That makes them one rule stated three times, which is exactly the kind of duplication that rots
/// silently: change one and the others keep producing keys the unique index accepts but that nothing
/// agrees on. <c>OfferBackfillIntegrationTests</c> closes the loop against a real database, both by
/// asserting the ids a fully migrated database holds are the ones this method computes, and by
/// asserting the premise that lets the Brand stay out of the key.
/// </para>
/// <para>
/// It used to be <c>brand_id + "-" + fuel_type_id</c>, which read <c>okko-okko-95</c> for a seeded
/// fuel and <c>okko-3808d410-b9ae-…</c> for an operator-created one — two shapes in one column,
/// depending on which button made the row.
/// </para>
/// </remarks>
public sealed class OfferKeyTests
{
    [Fact]
    public void TheRealPool_IsKeyedByTheFuelAlone()
    {
        // No suffix and no Brand: the fuel type id already says which fuel this is, and the fuel
        // belongs to exactly one Brand - which is the premise the Brand column's absence rests on.
        OfferKeys.For("okko-95", DataKinds.Real).Should().Be("okko-95");
    }

    [Fact]
    public void AFuelWithAGeneratedId_KeysToThatId()
    {
        // The shape the write path actually produces (BrandsController.AddFuel). Before the Brand
        // prefix was dropped this arrived at a customer as 'okko-3808d410-b9ae-4511-9dfc-d6564fa2e1ad',
        // the brand spelled once in the key and again inside the id it wrapped.
        OfferKeys.For("3808d410-b9ae-4511-9dfc-d6564fa2e1ad", DataKinds.Real)
            .Should().Be("3808d410-b9ae-4511-9dfc-d6564fa2e1ad");
    }

    [Fact]
    public void TheQaPool_CarriesASuffix()
    {
        // The suffix is what keeps the rehearsal mirror of an offer from colliding with the live
        // one. Without it the unique index on (Brand, fuel, pool) would be satisfied by a row
        // that answers to the live offer's id.
        OfferKeys.For("okko-95", DataKinds.Qa).Should().Be("okko-95-qa");
    }

    [Fact]
    public void TheTwoPools_NeverProduceTheSameKey()
    {
        // Stated as a property rather than through the two literals above, because this is the
        // invariant the storefront filter depends on.
        OfferKeys.For("okko-95", DataKinds.Real)
            .Should().NotBe(OfferKeys.For("okko-95", DataKinds.Qa));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void AnEmptyFuel_IsRejected(string? fuel)
    {
        // Otherwise the key would be empty or '-qa', and the 103-character bound would never notice.
        var act = () => OfferKeys.For(fuel!, DataKinds.Real);
        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void AnEmptyPool_IsRejected(string? pool)
    {
        // An empty pool is an empty component, not an unknown one: it fails the emptiness check
        // before the value-set check ever runs, and says so. An ArgumentOutOfRangeException here
        // would claim the pool was supplied and merely wrong.
        var poolAct = () => OfferKeys.For("okko-95", pool!);
        poolAct.Should().Throw<ArgumentException>()
            .Which.Should().NotBeOfType<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData("Real")]
    [InlineData("QA")]
    [InlineData("sandbox")]
    public void AnUnknownPool_IsRejectedRatherThanSilentlyKeyed(string kind)
    {
        // Silently producing a key for a value the CHECK rejects would mean the failure surfaces at
        // the INSERT, far from the call that made the decision.
        var act = () => OfferKeys.For("okko-95", kind);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void TheKeyStaysWithinTheColumnsBound()
    {
        // ck_fuel_offers_id_length exists precisely so a legal offer cannot fail its own CHECK at
        // write time, so the bound has to be derived rather than guessed. The key is the fuel id —
        // already bounded at 100 by ck_fuel_types_id_length — plus the pool suffix. It used to compose
        // two 100-character components, which is why it was 204; the arithmetic is restated here so a
        // tightened column bound cannot silently start rejecting a legal offer at write time.
        var longest = OfferKeys.For(new string('f', 100), DataKinds.Qa);

        longest.Length.Should().Be(103, "100 plus the '-qa' suffix");
    }

    [Fact]
    public void TheKeyNeverRepeatsTheBrand()
    {
        // The reason the method takes no Brand: a seeded fuel id already begins with its brand, so
        // keying on both spelled the brand twice. Stated as a property rather than against one
        // literal, because the failure this guards is a shape ('okko-okko-…') and not a value.
        OfferKeys.For("okko-okko-95", DataKinds.Real)
            .Should().NotBe(OfferKeys.For("okko-95", DataKinds.Real));

        OfferKeys.For("okko-dp", DataKinds.Real).Should().NotStartWith("okko-okko");
    }
}