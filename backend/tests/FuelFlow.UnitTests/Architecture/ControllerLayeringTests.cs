using System.Reflection;
using FuelFlow.Persistence;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.UnitTests.Architecture;

/// <summary>
/// The layering rule, as a gate.
/// </summary>
/// <remarks>
/// <para>
/// The README describes the backend as "Controllers -&gt; Command/Query handlers -&gt; DbContext
/// (CQRS-lite)". That sentence was a convention: nothing checked it, so a third of the controllers
/// (13 of 47) ended up injecting <see cref="ApplicationDbContext"/> and doing their own reads and
/// writes. The business rules that live in those controllers are invisible to the unit suite,
/// because the unit suite never touches a controller - it drives handlers.
/// </para>
/// <para>
/// This test is that sentence, made executable. It reads the COMPILED API assembly rather than
/// the source text: a controller that takes the context in any way - as a constructor parameter,
/// as a property, as a field - is an offender, whatever the source looks like. A text scanner
/// cannot see a constructor parameter that arrives through a base class or an injected wrapper;
/// reflection can.
/// </para>
/// <para>
/// The rule is deliberately about the CONTROLLER, not about the context: a handler is supposed to
/// hold the context. That is the whole point of the middle layer.
/// </para>
/// </remarks>
public sealed class ControllerLayeringTests
{
    [Fact]
    public void No_Controller_Talks_To_The_DbContext()
    {
        var offenders = ControllerTypes()
            .Select(controller => new
            {
                Controller = controller,
                Dependencies = DatabaseDependencies(controller)
            })
            .Where(x => x.Dependencies.Count > 0)
            .Select(x => $"{x.Controller.FullName} depends on {string.Join(", ", x.Dependencies)}")
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToArray();

        offenders.Should().BeEmpty(
            "a controller that holds the DbContext is a controller that does its own reads and " +
            "writes, and the rules inside it are then untested by the unit suite. The data " +
            "access belongs in a command/query handler, which the unit suite can drive without " +
            "a database. Planning finding C-4 (issue #534).");
    }

    /// <summary>
    /// Every controller the API actually serves.
    /// </summary>
    /// <remarks>
    /// The rule is scoped to the controllers: a controller that inherits from
    /// <see cref="ControllerBase"/> and is not abstract is an endpoint, and an endpoint is where
    /// the HTTP contract lives. Everything else in the assembly is a handler, a service or a
    /// model - and those are allowed to hold the context.
    /// </remarks>
    private static IReadOnlyList<Type> ControllerTypes() =>
        typeof(ApplicationDbContext).Assembly
            .GetTypes()
            .Where(type => type is { IsClass: true, IsAbstract: false })
            .Where(type => typeof(ControllerBase).IsAssignableFrom(type))
            .OrderBy(type => type.FullName, StringComparer.Ordinal)
            .ToArray();

    /// <summary>
    /// How this controller reaches the database, if it does.
    /// </summary>
    /// <remarks>
    /// A constructor parameter is the ordinary way. The property and the field are checked too
    /// because a controller can be handed the context after it was built - and a rule that only
    /// looked at the constructor would pass on a controller that reaches the database through
    /// either of the other two.
    /// </remarks>
    private static IReadOnlyList<string> DatabaseDependencies(Type controller)
    {
        var found = new List<string>();

        foreach (var parameter in controller
                     .GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                     .SelectMany(constructor => constructor.GetParameters()))
        {
            if (IsDatabaseType(parameter.ParameterType))
                found.Add($"constructor parameter '{parameter.Name}' of type {parameter.ParameterType.Name}");
        }

        foreach (var property in controller.GetProperties(
                     BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
        {
            if (IsDatabaseType(property.PropertyType))
                found.Add($"property '{property.Name}' of type {property.PropertyType.Name}");
        }

        foreach (var field in controller.GetFields(
                     BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
        {
            if (IsDatabaseType(field.FieldType))
                found.Add($"field '{field.Name}' of type {field.FieldType.Name}");
        }

        return found;
    }

    /// <summary>
    /// Is this type a way to reach the database?
    /// </summary>
    /// <remarks>
    /// Any <see cref="DbContext"/> and anything derived from it - the application context is one,
    /// and so would be any other context the API might grow. A controller that holds one is
    /// holding the database, whatever it is called.
    /// </remarks>
    private static bool IsDatabaseType(Type type) => typeof(DbContext).IsAssignableFrom(type);
}
