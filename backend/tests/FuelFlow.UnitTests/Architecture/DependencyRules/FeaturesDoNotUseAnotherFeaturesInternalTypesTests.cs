using FluentAssertions;

namespace FuelFlow.UnitTests.Architecture.DependencyRules;

/// <summary>
/// Rule 3 of the dependency suite: a feature must not use another feature's internal types.
/// </summary>
/// <remarks>
/// <para>
/// A feature owns its internals: an <c>internal</c> type is the feature's own business, and no
/// other feature has a reason to know it. When one feature reaches into another's internals,
/// the second feature's internals stop being changeable - every change is a negotiation with a
/// caller that should not exist - and the first feature's behaviour depends on a detail that
/// was never part of its contract.
/// </para>
/// <para>
/// Sharing is still allowed, and the codebase already has the right shape for it: the types
/// under a feature's <c>SharedModels</c> and <c>SharedServices</c> namespaces are public on
/// purpose, and a feature may use those. What is not allowed is the <c>internal</c> type - the
/// one nobody was meant to reach for.
/// </para>
/// <para>
/// The check is on the compiled assembly, not on the source: a reference to an internal type is
/// a reference whatever the file is called and whatever namespace it claims to be in.
/// </para>
/// </remarks>
public sealed class FeaturesDoNotUseAnotherFeaturesInternalTypesTests
{
    [Fact]
    public void NoFeatureReachesIntoAnotherFeaturesInternals()
    {
        var internalTypes = ArchitectureRules.ApiTypes
            .Where(type => !ArchitectureRules.IsVisibleOutsideTheAssembly(type))
            .Where(type => ArchitectureRules.FeatureOf(type) is not null)
            .ToList();

        var violations = new List<string>();

        foreach (var consumer in ArchitectureRules.ApiTypes)
        {
            var consumerFeature = ArchitectureRules.FeatureOf(consumer);
            if (consumerFeature is null) continue;

            foreach (var reference in ArchitectureRules.DirectReferences(consumer))
            {
                if (ArchitectureRules.IsVisibleOutsideTheAssembly(reference)) continue;

                var ownerFeature = ArchitectureRules.FeatureOf(reference);
                if (ownerFeature is null || ownerFeature == consumerFeature) continue;

                violations.Add(
                    $"{consumer.FullName} uses the internal type {reference.FullName}, " +
                    $"which belongs to the feature '{ownerFeature}'.");
            }
        }

        violations.Should().BeEmpty(
            "an internal type is the feature's own business. Another feature that needs it has " +
            "two honest ways forward: the type becomes public on purpose, because the sharing is " +
            "real, or the caller moves to where the type lives. A third way - reaching in - is " +
            "what makes the owning feature unable to change.");
    }

    [Fact]
    public void TheRuleHasSomethingToCheck()
    {
        // A rule that finds nothing because the assembly has no internal types would pass
        // forever and mean nothing. The assembly has internal types; this pins that the rule
        // is looking at the right assembly.
        var internalTypes = ArchitectureRules.ApiTypes
            .Where(type => !ArchitectureRules.IsVisibleOutsideTheAssembly(type))
            .Where(type => ArchitectureRules.FeatureOf(type) is not null)
            .ToList();

        internalTypes.Should().NotBeEmpty(
            "the API declares internal types inside its features; if that ever stops being true, " +
            "this rule has nothing left to guard and should be retired with the types it guards.");
    }
}
