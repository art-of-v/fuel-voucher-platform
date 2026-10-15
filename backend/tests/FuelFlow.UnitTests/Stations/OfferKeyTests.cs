using FluentAssertions;
using FuelFlow.SharedKernel.Domain;
using Xunit;

namespace FuelFlow.UnitTests.Stations;

/// <summary>
/// The offer's natural key is <c>(station_id, fuel_type_id, data_kind)</c>, and its <c>id</c> is
/// derived from that triple by <see cref="OfferKeys"/>.
/// </summary>
/// <remarks>
/// The derivation exists twice — once in C#, once inline in the <c>SplitFuelPackagesIntoOffers</c>
/// backfill, because a migration cannot call into the assembly it migrates from. That makes the two
/// a single rule written down twice, which is exactly the kind of duplication that rots silently:
/// change one and the other keeps producing keys the unique index accepts but that nothing agrees
/// on. <c>OfferKeyBackfillTests</c> in the integration suite closes the loop against a real
/// database; these tests pin the C# half so the intent is stated where the code is.
/// </remarks>
public sealed class OfferKeyTests
{
    [Fact]
    public void TheRealPool_IsKeyedByStationAndFuelAlone()
    {
        // No suffix: the real pool is the default, so the commonest id stays the shortest and the
        // readable one.
        OfferKeys.For("okko", "okko-95", DataKinds.Real).Should().Be("okko-okko-95");
    }

    [Fact]
    public void TheQaPool_CarriesASuffix()
    {
        // The suffix is what keeps the rehearsal mirror of an offer from colliding with the live
        // one. Without it the unique index on (station, fuel, pool) would be satisfied by a row
        // that answers to the live offer's id.
        OfferKeys.For("okko", "okko-95", DataKinds.Qa).Should().Be("okko-okko-95-qa");
    }

    [Fact]
    public void TheTwoPools_NeverProduceTheSameKey()
    {
        // Stated as a property rather than through the two literals above, because this is the
        // invariant the storefront filter depends on.
        OfferKeys.For("okko", "okko-95", DataKinds.Real)
            .Should().NotBe(OfferKeys.For("okko", "okko-95", DataKinds.Qa));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void AnEmptyComponent_IsRejected(string? component)
    {
        // Otherwise the key would be something like "okko-" and the 204-character bound would never
        // notice.
        var act = () => OfferKeys.For(component!, "okko-95", DataKinds.Real);
        act.Should().Throw<ArgumentException>();

        var alsoAct = () => OfferKeys.For("okko", component!, DataKinds.Real);
        alsoAct.Should().Throw<ArgumentException>();

        // An empty pool is an empty component, not an unknown one: it fails the emptiness check
        // before the value-set check ever runs, and says so. An ArgumentOutOfRangeException here
        // would claim the pool was supplied and merely wrong.
        var poolAct = () => OfferKeys.For("okko", "okko-95", component!);
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
        var act = () => OfferKeys.For("okko", "okko-95", kind);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void TheKeyStaysWithinTheColumnsBound()
    {
        // ck_fuel_offers_id_length exists precisely so a legal offer cannot fail its own CHECK at
        // write time, so the bound has to be derived rather than guessed. The key composes a station
        // id and a fuel type id - each already bounded at 100 by their own CHECK - plus a separator
        // and the pool suffix.
        //
        // This test is the reason the bound is 204 and not the round 200 it started as: at the two
        // extremes the key is 204 characters, so a 200 bound would reject a perfectly legal offer.
        // OfferKeyBoundsTests asserts the same number against the migrated schema.
        var longest = OfferKeys.For(new string('s', 100), new string('f', 100), DataKinds.Qa);

        longest.Length.Should().Be(204, "100 + 1 + 100 + the '-qa' suffix");
    }
}