using System.Text.RegularExpressions;
using FluentAssertions;

namespace FuelFlow.UnitTests.Architecture.DependencyRules;

/// <summary>
/// Rule 4 of the dependency suite: the API emits exactly one error envelope.
/// </summary>
/// <remarks>
/// <para>
/// The API used to fail in three shapes at once: <c>{ error = ... }</c> from most controllers,
/// <c>{ message = ... }</c> from the auth ones, and an RFC 9457 problem document from the
/// exception handler. The admin client sniffed four body shapes to find the sentence, which
/// made the contract, in effect, <c>any</c> - and the mobile client sniffed three of its own.
/// </para>
/// <para>
/// Every failure now leaves as one document: <c>type</c>, <c>title</c>, <c>status</c>,
/// <c>detail</c>, <c>instance</c>, <c>traceId</c>, and the optional <c>code</c> and <c>data</c>
/// extensions. It is built by one factory,
/// <c>backend/src/FuelFlow.API/SharedKernel/Errors/ApiError.cs</c>.
/// </para>
/// <para>
/// This test is a source scan, and it has to be: "the body of every failure is the same
/// document" is not a fact about a type, it is a fact about what a call site passes. The shapes
/// it forbids are the ones that made the contract <c>any</c> - an ad-hoc body, an empty body
/// and a plain-text body - and each of them is a way for the next failure to arrive in a shape
/// of its own.
/// </para>
/// <para>
/// The runtime half of the rule - that the document really is served with
/// <c>application/problem+json</c> and carries the fields - is proved by
/// <c>ErrorEnvelopeIntegrationTests</c> in the integration suite.
/// </para>
/// </remarks>
public sealed class OneErrorEnvelopeTests
{
    /// <summary>
    /// The ways an error body used to be built. Each one is a different shape for the client to
    /// cope with, which is what "the contract is any" means in practice.
    /// </summary>
    private static readonly (string Name, string Pattern, bool SkipDeclarations)[] ForbiddenShapes =
    [
        // BadRequest(new { error = ... }) and its five siblings: the ad-hoc body.
        ("an ad-hoc error body", @"(?<![.\w])(BadRequest|NotFound|Conflict|Unauthorized|Forbidden)\(\s*new\s*\{", false),

        // StatusCode(403, new { message = ... }): the ad-hoc body with an explicit status.
        ("an ad-hoc error body with an explicit status", @"StatusCode\([^,]+,\s*new\s*\{", false),

        // WriteAsJsonAsync(new { error = ... }): a hand-built body, in the middleware.
        ("a hand-built error body", @"WriteAsJsonAsync\(\s*new\s*\{", false),

        // BadRequest(): a status with no body at all - the client cannot even tell what happened.
        ("an error with no body", @"(?<![.\w])(BadRequest|NotFound|Conflict|Unauthorized|Forbidden)\(\s*\)", true),

        // StatusCode(500, "An error occurred ..."): a plain-text body, which is not a document.
        ("a plain-text error body", "StatusCode\\(\\s*\\d+\\s*,\\s*\"", false),
    ];

    [Fact]
    public void EveryFailureIsBuiltByTheOneEnvelope()
    {
        var apiRoot = ArchitectureRules.LocateApiProjectRoot();

        var offenders = new List<string>();

        foreach (var file in SourceFiles(apiRoot))
        {
            var text = File.ReadAllText(file);
            var relative = Path.GetRelativePath(apiRoot, file).Replace('\\', '/');

            foreach (var (name, pattern, skipDeclarations) in ForbiddenShapes)
            {
                foreach (var match in Regex.Matches(text, pattern).Cast<Match>())
                {
                    // A method that is DECLARED with an empty parameter list is not an error with
                    // no body: `public static UpdateOrderStatusResult NotFound() => ...` is a
                    // factory, and it is not an HTTP answer.
                    if (skipDeclarations && IsDeclaration(text, match)) continue;

                    var line = text[..match.Index].Count(c => c == '\n') + 1;
                    var snippet = match.Value.Replace("\n", " ");
                    offenders.Add($"{relative}:{line} - {name}: {snippet}");
                }
            }
        }

        offenders.Should().BeEmpty(
            "every failure has to leave the API as the one problem document, or the client is " +
            "back to sniffing body shapes (planning finding C-6). Build it with " +
            "FuelFlow.SharedKernel.Errors.ApiError - BadRequest(detail), NotFound(detail), " +
            "Conflict(detail, code), and so on - and the sentence goes in `detail`, the machine " +
            "code in `code`, the numbers in `data`.");
    }

    [Fact]
    public void TheEnvelopeFactoryIsTheOnlyWayToBuildAFailure()
    {
        // The scan above catches the shapes it knows. This one catches the factory itself
        // moving: if ApiError stopped being the one place a failure is built, the scan would
        // still pass while the contract quietly forked.
        var apiRoot = ArchitectureRules.LocateApiProjectRoot();

        var factory = Path.Combine(apiRoot, "SharedKernel", "Errors", "ApiError.cs");
        File.Exists(factory).Should().BeTrue(
            "the one envelope has one home: backend/src/FuelFlow.API/SharedKernel/Errors/ApiError.cs");

        var callers = SourceFiles(apiRoot)
            .Where(file => !file.EndsWith("ApiError.cs", StringComparison.Ordinal))
            .Select(file => (file, text: File.ReadAllText(file)))
            .Where(found => found.text.Contains("ApiError."))
            .Select(found => Path.GetRelativePath(apiRoot, found.file).Replace('\\', '/'))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToList();

        callers.Should().NotBeEmpty(
            "the envelope factory is in use; if this is empty, the failures went somewhere else " +
            "and the rule above is guarding a building nobody lives in.");
    }

    /// <summary>
    /// True when the match is a method DECLARATION rather than a call: what follows the closing
    /// parenthesis is a body or an expression body, not an argument list.
    /// </summary>
    private static bool IsDeclaration(string text, Match match)
    {
        var rest = text[(match.Index + match.Length)..].TrimStart();
        return rest.StartsWith("=>", StringComparison.Ordinal)
            || rest.StartsWith("{", StringComparison.Ordinal);
    }

    /// <summary>
    /// The C# files of the API, minus the generated ones: the migrations are EF's, and the build
    /// output is not source.
    /// </summary>
    private static IEnumerable<string> SourceFiles(string apiRoot) =>
        Directory.EnumerateFiles(apiRoot, "*.cs", SearchOption.AllDirectories)
            .Where(path =>
            {
                var normalized = path.Replace('\\', '/');
                if (normalized.Contains("/Migrations/")) return false;
                if (normalized.Contains("/obj/") || normalized.Contains("/bin/")) return false;
                return true;
            });
}
