using FluentAssertions;
using Microsoft.AspNetCore.Http;

namespace FuelFlow.UnitTests.Architecture.DependencyRules;

/// <summary>
/// Rule 2 of the dependency suite: a handler must not reach for the HTTP context.
/// </summary>
/// <remarks>
/// <para>
/// The handler is where the business rule lives, and a business rule that can read the request
/// is a rule that behaves differently depending on who asked. The context also carries
/// <c>User</c>, <c>Items</c> and <c>RequestAborted</c>, and a handler that reads them is a
/// handler whose test needs an HTTP pipeline around it - which is why the rules in the handlers
/// are the ones the unit suite can reach.
/// </para>
/// <para>
/// What a handler legitimately needs from the request - who is acting, their id, their role -
/// arrives as a value on the command, written by the controller. The commands carry it
/// already (<c>ActingUserId</c>, <c>UserId</c>, <c>ActingUserName</c>), so the rule costs
/// nothing.
/// </para>
/// <para>
/// <see cref="IHttpContextAccessor"/> is in the same family: it is the context by another name.
/// </para>
/// </remarks>
public sealed class HandlersDoNotTouchHttpContextTests
{
    [Fact]
    public void NoHandlerReachesForTheHttpContext()
    {
        var offenders = Handlers()
            .Select(handler => (handler, references: ReferencesTheHttpContext(handler)))
            .Where(found => found.references.Count > 0)
            .Select(found => $"{found.handler.FullName} ({string.Join(", ", found.references)})")
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        offenders.Should().BeEmpty(
            "a handler that reads the HTTP context is a rule that depends on who asked, and a " +
            "unit test of it needs a request around it. The values a handler needs - who is " +
            "acting, their id - travel on the command instead.");
    }

    /// <summary>
    /// The handlers: the command and query handlers of the feature layer. The name says it, and
    /// the namespace says where it lives - an ASP.NET <c>IExceptionHandler</c> in the middleware
    /// is called a handler too, and it is exactly the piece that has to see the request.
    /// </summary>
    private static IEnumerable<Type> Handlers() =>
        ArchitectureRules.ApiTypes.Where(type =>
            type.IsClass
            && !type.IsAbstract
            && ArchitectureRules.FeatureOf(type) is not null
            && type.Name.EndsWith("Handler", StringComparison.Ordinal));

    private static List<string> ReferencesTheHttpContext(Type handler)
    {
        var references = new List<string>();

        foreach (var reference in ArchitectureRules.DirectReferences(handler))
        {
            if (reference == typeof(HttpContext)) references.Add(nameof(HttpContext));
            if (reference == typeof(IHttpContextAccessor)) references.Add(nameof(IHttpContextAccessor));
        }

        return references;
    }
}
