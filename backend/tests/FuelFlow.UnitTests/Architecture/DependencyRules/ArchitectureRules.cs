using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.AspNetCore.Mvc;

namespace FuelFlow.UnitTests.Architecture.DependencyRules;

/// <summary>
/// The machinery the four dependency rules share.
/// </summary>
/// <remarks>
/// <para>
/// The eight architecture tests that were in this folder before (planning finding C-6) are all
/// text or regex scanners: they look at the source and count things. They are useful - a guard
/// that fails when a file loses its <c>.AsTracking()</c> marker is a guard that works - but none
/// of them can tell whether a controller talks to the database, because that is a property of
/// the COMPILED type, not of its text.
/// </para>
/// <para>
/// These helpers read the compiled assembly instead, so the rules they serve are dependency
/// rules: what a type points at, which is what "the layering" actually is.
/// </para>
/// </remarks>
internal static class ArchitectureRules
{
    /// <summary>The API assembly - the code the rules are about.</summary>
    public static Assembly ApiAssembly { get; } = typeof(FuelFlow.Persistence.ApplicationDbContext).Assembly;

    /// <summary>Every type the API declares.</summary>
    public static IReadOnlyList<Type> ApiTypes { get; } = ApiAssembly.GetTypes();

    /// <summary>
    /// A controller: a concrete type that ends up serving an HTTP endpoint. Minimal-API endpoints
    /// are not controllers, but they are also not allowed to touch the database by the same rule,
    /// and they are checked by the source scan in <c>OneErrorEnvelopeTests</c>.
    /// </summary>
    public static bool IsController(Type type) =>
        typeof(ControllerBase).IsAssignableFrom(type)
        && !type.IsAbstract
        && !type.IsInterface;

    /// <summary>
    /// The feature a type belongs to: the segment after <c>Features.</c> in its namespace. The
    /// codebase carries two roots for the same tree (<c>FuelFlow.API.Features.X</c> and
    /// <c>FuelFlow.Features.X</c>), so both are read. A type outside a feature namespace
    /// (the persistence layer, the shared kernel, the middleware) belongs to no feature.
    /// </summary>
    public static string? FeatureOf(Type type)
    {
        var ns = type.Namespace;
        if (ns is null) return null;

        const string marker = ".Features.";
        var index = ns.IndexOf(marker, StringComparison.Ordinal);
        if (index < 0) return null;

        var rest = ns[(index + marker.Length)..];
        var dot = rest.IndexOf('.');
        return dot < 0 ? rest : rest[..dot];
    }

    /// <summary>
    /// The types a type points at directly: its base, its interfaces, and the types of its
    /// fields, properties, methods, events and their generic arguments.
    /// </summary>
    /// <remarks>
    /// Direct, on purpose. If feature A uses a PUBLIC type of feature B, and that public type
    /// happens to use an internal type of B, A has not reached into B's internals - B has, and
    /// B is allowed to. A transitive walk would report that as a violation of A.
    /// </remarks>
    public static IEnumerable<Type> DirectReferences(Type type)
    {
        const BindingFlags Declared = BindingFlags.Public | BindingFlags.NonPublic
            | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

        if (type.BaseType is { } baseType)
        {
            foreach (var referenced in Unwrap(baseType)) yield return referenced;
        }

        foreach (var contract in type.GetInterfaces())
        {
            foreach (var referenced in Unwrap(contract)) yield return referenced;
        }

        foreach (var field in type.GetFields(Declared))
        {
            foreach (var referenced in Unwrap(field.FieldType)) yield return referenced;
        }

        foreach (var property in type.GetProperties(Declared))
        {
            foreach (var referenced in Unwrap(property.PropertyType)) yield return referenced;
        }

        foreach (var constructor in type.GetConstructors(Declared))
        {
            foreach (var parameter in constructor.GetParameters())
            {
                foreach (var referenced in Unwrap(parameter.ParameterType)) yield return referenced;
            }
        }

        foreach (var method in type.GetMethods(Declared))
        {
            foreach (var referenced in Unwrap(method.ReturnType)) yield return referenced;
            foreach (var parameter in method.GetParameters())
            {
                foreach (var referenced in Unwrap(parameter.ParameterType)) yield return referenced;
            }
        }

        foreach (var @event in type.GetEvents(Declared))
        {
            if (@event.EventHandlerType is { } handlerType)
            {
                foreach (var referenced in Unwrap(handlerType)) yield return referenced;
            }
        }
    }

    /// <summary>
    /// A type and its generic arguments, so that <c>List&lt;InternalType&gt;</c> is a reference to
    /// <c>InternalType</c> and not only to <c>List&lt;&gt;</c>.
    /// </summary>
    private static IEnumerable<Type> Unwrap(Type type)
    {
        yield return type;

        if (!type.IsGenericType) yield break;

        foreach (var argument in type.GetGenericArguments())
        {
            foreach (var nested in Unwrap(argument)) yield return nested;
        }
    }

    /// <summary>
    /// True when the type is visible outside the assembly - that is, when another assembly could
    /// name it. An <c>internal</c> type is not, and an internal type of one feature is not
    /// available to another feature even though both live in this assembly.
    /// </summary>
    public static bool IsVisibleOutsideTheAssembly(Type type) =>
        type.IsPublic || type.IsNestedPublic;

    /// <summary>
    /// Resolves backend/src/FuelFlow.API from this test file's compile-time location, with a
    /// fallback that walks up from the test assembly's directory. Build and test run on the same
    /// checkout here, so the source tree is always present.
    /// </summary>
    public static string LocateApiProjectRoot([CallerFilePath] string thisFile = "")
    {
        // thisFile = .../backend/tests/FuelFlow.UnitTests/Architecture/DependencyRules/<this>.cs
        // Up 4: DependencyRules -> Architecture -> FuelFlow.UnitTests -> tests -> backend
        var backend = Directory.GetParent(thisFile)!.Parent!.Parent!.Parent!.Parent!.FullName;
        var candidate = Path.Combine(backend, "src", "FuelFlow.API");
        if (Directory.Exists(candidate)) return candidate;

        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var probe = Path.Combine(dir.FullName, "src", "FuelFlow.API");
            if (Directory.Exists(probe)) return probe;
            probe = Path.Combine(dir.FullName, "backend", "src", "FuelFlow.API");
            if (Directory.Exists(probe)) return probe;
            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException(
            "Could not locate backend/src/FuelFlow.API from either the test file path or the assembly directory.");
    }
}
