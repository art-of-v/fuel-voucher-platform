using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.UnitTests.Architecture.DependencyRules;

/// <summary>
/// Rule 1 of the dependency suite: a controller must not talk to the database.
/// </summary>
/// <remarks>
/// <para>
/// The README describes the architecture as "Controllers → Command/Query handlers → DbContext
/// (CQRS-lite)". A convention is advisory; a test is a gate. This is the gate: a controller that
/// holds a database context has reached past the handler layer, and the rules that live in the
/// handler are no longer the rules that run.
/// </para>
/// <para>
/// It is not a style preference. A controller that writes the order status directly is a
/// controller that can skip the rule that says an order may only enter <c>PendingFulfillment</c>
/// against a paid order - which is how finding S-1 was possible.
/// </para>
/// <para>
/// The test reads the COMPILED assembly rather than the source text. A text scanner cannot see a
/// constructor parameter that arrives through a base class or an injected wrapper; reflection
/// can. It looks at every way a controller can hold the context: as a constructor parameter, as a
/// property, as a field, and in the signature of one of its own methods.
/// </para>
/// <para>
/// The rule came with the work that moved the thirteen controllers into handlers (planning
/// finding C-4, issue #534, app PR #1485). It used to live in
/// <c>Architecture/ControllerLayeringTests.cs</c>; it moved here when the dependency suite
/// grew around it, and it forbids exactly what it forbade before.
/// </para>
/// </remarks>
public sealed class ControllersDoNotTouchTheDatabaseTests
{
    [Fact]
    public void NoControllerHoldsTheDatabase()
    {
        var offenders = ArchitectureRules.ApiTypes
            .Where(ArchitectureRules.IsController)
            .Select(controller => new
            {
                Controller = controller,
                Dependencies = DatabaseDependencies(controller),
            })
            .Where(found => found.Dependencies.Count > 0)
            .Select(found => $"{found.Controller.FullName} depends on {string.Join(", ", found.Dependencies)}")
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        offenders.Should().BeEmpty(
            "a controller that holds the DbContext is a controller that does its own reads and " +
            "writes, and the rules inside it are then untested by the unit suite. The data " +
            "access belongs in a command/query handler, which the unit suite can drive without " +
            "a database. Planning finding C-4 (issue #534).");
    }

    /// <summary>
    /// How the controller reaches the database, if it does.
    /// </summary>
    private static List<string> DatabaseDependencies(Type controller)
    {
        var found = new List<string>();

        foreach (var reference in ArchitectureRules.DirectReferences(controller))
        {
            if (IsDatabaseType(reference))
            {
                found.Add($"the database context '{reference.Name}'");
            }
        }

        return found;
    }

    /// <summary>
    /// Is this type a way to reach the database? Any <see cref="DbContext"/> and anything derived
    /// from it - the application context is one, and so would be any other context the API might
    /// grow. A controller that holds one is holding the database, whatever it is called.
    /// </summary>
    private static bool IsDatabaseType(Type type) => typeof(DbContext).IsAssignableFrom(type);
}
