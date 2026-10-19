using FluentAssertions;
using FuelFlow.API.Migrations;
using FuelFlow.SharedKernel.Domain;
using Xunit;

namespace FuelFlow.UnitTests.SharedKernel;

/// <summary>
/// The ids of the four seeded networks, and the derivation they come from.
/// </summary>
/// <remarks>
/// The golden values below are the point of this file. The derivation is only safe because the
/// namespace it uses is frozen: production's rows carry the ids the current namespace produces,
/// and a namespace changed in the code would leave the database quietly disagreeing with it. A
/// test that only checked the derivation against itself would not notice that, and the migration
/// that re-points production's rows would move them to ids nothing else knows.
/// </remarks>
public sealed class BrandIdsTests
{
    [Fact]
    public void The_seeded_ids_are_the_ones_the_derivation_produces()
    {
        BrandIds.Okko.Should().Be(BrandIds.ForSlug("okko"));
        BrandIds.Wog.Should().Be(BrandIds.ForSlug("wog"));
        BrandIds.Upg.Should().Be(BrandIds.ForSlug("upg"));
        BrandIds.Klo.Should().Be(BrandIds.ForSlug("klo"));
    }

    [Fact]
    public void The_seeded_ids_do_not_change()
    {
        // The four ids the migration 20261019201500_SeededBrandIdsBecomeDerived gives production.
        // If this fails, the namespace changed: that is a data migration, not an edit.
        BrandIds.Okko.Should().Be(new Guid("89a11944-bdb3-5ed4-b55c-edae31335a6e"));
        BrandIds.Wog.Should().Be(new Guid("0b9d7319-8ce3-50c9-a6e0-58e850217ffa"));
        BrandIds.Upg.Should().Be(new Guid("080a78c0-c80b-53f1-97cd-0fedb0ea953b"));
        BrandIds.Klo.Should().Be(new Guid("48652f56-ca2d-590e-acfd-e8d9f970433b"));
    }

    [Fact]
    public void The_seeded_ids_are_not_the_constants_they_replaced()
    {
        // The hand-written ids this derivation replaced. They are still worth naming: a row
        // carrying one of them is a row the migration has not reached.
        BrandIds.Okko.Should().NotBe(new Guid("11111111-1111-4111-8111-111111111111"));
        BrandIds.Wog.Should().NotBe(new Guid("22222222-2222-4222-8222-222222222222"));
        BrandIds.Upg.Should().NotBe(new Guid("33333333-3333-4333-8333-333333333333"));
        BrandIds.Klo.Should().NotBe(new Guid("44444444-4444-4444-8444-444444444444"));
    }

    [Fact]
    public void The_migration_moves_production_to_the_ids_the_code_computes()
    {
        // The migration cannot call into the assembly it migrates from, so it states the four
        // values itself. This is the check that the two statements of the same fact agree.
        foreach (var (slug, oldId, newId) in SeededBrandIdsBecomeDerived.SeededBrandIds)
        {
            newId.Should().Be(
                BrandIds.ForSlug(slug),
                $"the migration must move {slug} to the id the code hands out");
            oldId.Should().NotBe(newId);
        }
    }

    [Fact]
    public void Two_networks_never_share_an_id()
    {
        var ids = new[] { BrandIds.Okko, BrandIds.Wog, BrandIds.Upg, BrandIds.Klo };

        ids.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void The_derivation_refuses_a_slug_that_is_not_one()
    {
        var act = () => BrandIds.ForSlug("  ");

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void The_sentinel_for_a_missing_brand_is_not_a_network()
    {
        // The id tests reach for when they mean "no such brand". It must not be one of the four,
        // or a test that expects nothing to match would match a network.
        var ids = new[] { BrandIds.Okko, BrandIds.Wog, BrandIds.Upg, BrandIds.Klo };

        ids.Should().NotContain(BrandIds.NotFound);
    }
}
