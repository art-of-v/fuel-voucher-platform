using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FuelFlow.IntegrationTests;

/// <summary>
/// Asserts that every backend path the admin SPA names actually exists in, and is allowed to reach,
/// the API.
///
/// <para>
/// Both bugs this file was written for were the same defect seen twice: a client-side path literal
/// that no route template matches. <c>admin.tsx</c> asked for
/// <c>/api/admin/legal-entity/signed-contracts</c> while the route is
/// <c>/api/admin/legal-entity/contracts/signed-contracts</c>, so the Contracts tab's signed list
/// was a permanent 404; <c>VoucherExchangeTab</c> invalidated the cache key
/// <c>/api/admin/imports</c>, which is not a route at all, leaving the import list stale after an
/// exchange. Neither raised an error anywhere - a wrong path is a valid request that answers 404,
/// and a wrong cache key is a silent no-op - so nothing but a cross-checking test can catch them.
/// </para>
///
/// <para>
/// The client literals are compared against the live endpoint graph from the real composition root,
/// not against a hand-maintained list, so renaming a route and forgetting the SPA fails here.
/// </para>
/// </summary>
[Collection("Integration Tests")]
public class AdminClientRouteContractTests : IClassFixture<TestDatabaseFixture>
{
    private readonly TestDatabaseFixture _fixture;

    public AdminClientRouteContractTests(TestDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    /// <summary>
    /// A path literal as a client writes it. Three shapes, because the two clients wrap their
    /// transport differently:
    /// <list type="bullet">
    /// <item><c>apiRequest("GET", path)</c> and <c>apiBlob(method, path, ...)</c> in the admin SPA,
    /// where the method precedes the path.</item>
    /// <item><c>queryKey: [path, ...]</c> in the admin SPA, which is a request because the global
    /// default queryFn fetches <c>queryKey[0]</c> verbatim (admin/src/lib/query-defaults.ts).</item>
    /// <item><c>apiFetch(path)</c> and <c>apiRequest("POST", path)</c> in the mobile app, where
    /// the path comes first and may be wrapped in <c>withEntity(...)</c>.</item>
    /// </list>
    /// A bare <c>const NAME = '/api/...'</c> is captured too: the mobile app names its paths in
    /// constants (USER_QUERY_KEY) and passes those into apiFetch, so scanning call sites alone
    /// would silently skip them.
    /// </summary>
    private static readonly Regex ClientCall = new(
        """
        \bapi(?:Request|Blob)\b[\s\S]{0,400}?\(\s*["'](?:GET|POST|PUT|PATCH|DELETE)["']\s*,\s*[`"'](?<path>/[^`"']*)
        |\bapiFetch\s*\(\s*(?:withEntity\s*\(\s*)?[`"'](?<path>/[^`'"]*)
        |queryKey:\s*\[\s*[`"'](?<path>/[^`'"]*)
        |\bconst\s+[A-Za-z0-9_]+\s*(?::[^=\n]+)?=\s*[`"'](?<path>/api/[^`"']*)
        """,
        RegexOptions.Compiled | RegexOptions.IgnorePatternWhitespace);

    private static readonly Regex ClientInterpolation = new(@"\$\{[^}]*\}", RegexOptions.Compiled);

    /// <summary>A route parameter in any of its declared forms: <c>{id}</c>, <c>{id:guid}</c>.</summary>
    private static readonly Regex RouteParameter = new(@"\{[^}]*\}", RegexOptions.Compiled);

    [Fact]
    public void EveryClientPath_MatchesARealRoute()
    {
        var routes = RouteTemplates();

        // Guard against a vacuous pass: an empty route graph would leave every client path
        // unresolved, but an empty client scan would silently pass. Both ends are pinned.
        routes.Should().HaveCountGreaterThan(100,
            "the whole routed endpoint graph should be visible to this test");

        var clientPaths = ClientPaths().ToList();
        clientPaths.Should().NotBeEmpty(
            "the extraction should still see the clients' API calls; a regex that stops matching " +
            "would turn this test into a no-op that passes forever");

        // The mobile app is the surface where money moves, so it must be seen here too - not
        // only the admin panel, where both mismatches found so far happened to live.
        clientPaths.Select(p => p.File).Should().Contain(
            f => f.StartsWith("mobile", StringComparison.Ordinal),
            "the mobile app is part of the client surface this contract covers");

        var unresolved = clientPaths
            .Where(p => !routes.Any(r => Matches(r, p.Path)))
            .OrderBy(x => x.Path, StringComparer.Ordinal)
            .Select(x => $"{x.Path}  ({x.File}:{x.Line})")
            .ToArray();

        unresolved.Should().BeEmpty(
            "every /api path a client requests or keys its cache on must match a route template; " +
            "a mismatched path is a silent 404, or a cache key that never invalidates");
    }

    /// <summary>
    /// The admin origin only forwards an allow-listed subset of <c>/api</c> to the backend, so an
    /// endpoint the SPA calls but the edge does not permit fails closed as a 404 in production
    /// while working fine in dev. nginx.conf carries its own copy of the list as defence in depth,
    /// and the two are documented as needing to move together - which is exactly the kind of
    /// coupling a test can hold instead of a comment.
    /// </summary>
    [Fact]
    public void EveryAdminPath_IsPermittedByTheEdgeAllowList()
    {
        var repoRoot = RepositoryRoot();

        var allowed = new[] { Path.Combine(repoRoot, "admin", "nginx.conf"), Path.Combine(repoRoot, "deploy", "Caddyfile") }
            .SelectMany(AllowListEntries)
            .ToList();

        allowed.Should().NotBeEmpty("the edge allow-lists should still declare their /api paths");

        var blocked = AdminPaths()
            .Select(p => p.Path)
            .Distinct(StringComparer.Ordinal)
            .Where(p => !allowed.Any(a => Permits(a, p)))
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToArray();

        blocked.Should().BeEmpty(
            "the admin SPA can only reach these paths; anything missing from admin/nginx.conf.template and " +
            "deploy/Caddyfile answers 404 at the edge while passing locally");
    }

    private IReadOnlyList<string> RouteTemplates()
    {
        // The composite EndpointDataSource is assembled by UseEndpoints, so ask the root brand
        // for every registered source and flatten. CreateClient() forces the host - and therefore
        // the endpoint graph - to be built before this runs.
        _ = _fixture.CreateClient();

        return _fixture.Services
            .GetServices<EndpointDataSource>()
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Select(e => RouteParameter.Replace(StripQuery(e.RoutePattern.RawText ?? string.Empty), "{}"))
            .Where(p => p.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    private static readonly string[] ClientSourceDirectories =
        ["admin/src", "mobile/src", "mobile/app"];

    /// <summary>
    /// Every path either shipped client names, with the file and line it was written on.
    /// </summary>
    private static IEnumerable<(string Path, string File, int Line)> ClientPaths()
    {
        var root = RepositoryRoot();

        foreach (var relative in ClientSourceDirectories)
        {
            foreach (var file in EnumerateClientFiles(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar))))
            {
                var lines = File.ReadAllLines(file);
                for (var i = 0; i < lines.Length; i++)
                {
                    foreach (Match match in ClientCall.Matches(lines[i]))
                    {
                        var path = NormalizeClientPath(match.Groups["path"].Value);

                        if (!path.StartsWith("/api/", StringComparison.Ordinal))
                            continue;

                        yield return (path, Path.GetRelativePath(root, file), i + 1);
                    }
                }
            }
        }
    }

    private static IEnumerable<string> EnumerateClientFiles(string root)
    {
        if (!Directory.Exists(root))
            return [];

        return Directory
            .EnumerateFiles(root, "*.*", SearchOption.AllDirectories)
            // Test files assert against invented paths ("/api/admin/whatever"); they are not
            // requests the shipped bundle makes.
            .Where(f => !f.Contains(".test.", StringComparison.Ordinal)
                        && !f.Contains(".spec.", StringComparison.Ordinal))
            // Only source the client actually ships: skip the type declaration files, which
            // mirror backend DTOs and carry field names rather than routes.
            .Where(f => f.EndsWith(".ts", StringComparison.Ordinal)
                        || f.EndsWith(".tsx", StringComparison.Ordinal));
    }

    /// <summary>Admin-only: the edge allow-lists describe the admin origin's surface, not mobile's.</summary>
    private static IEnumerable<(string Path, string File, int Line)> AdminPaths() =>
        ClientPaths().Where(p => p.File.StartsWith("admin" + Path.DirectorySeparatorChar, StringComparison.Ordinal)
                                 || p.File.StartsWith("admin/", StringComparison.Ordinal));

    /// <summary>
    /// Reduces a client-side path literal to bare segments. A complete <c>${expr}</c> collapses to a
    /// single <c>{}</c> placeholder, but a conditional interpolation (<c>/report${qs ? `?${qs}` :
    /// ""}</c>) closes on a later line, so the literal captured here is truncated mid-expression;
    /// anything from the leftover <c>$</c> on is a query-string fragment and is dropped.
    /// </summary>
    private static string NormalizeClientPath(string raw)
    {
        var value = ClientInterpolation.Replace(raw, "{}");

        var interpolationStart = value.IndexOf('$');
        if (interpolationStart >= 0)
            value = value[..interpolationStart];

        return StripQuery(value);
    }

    /// <summary>
    /// Whitespace-separated tokens naming an <c>/api</c> path, in either edge config's syntax:
    /// nginx writes <c>/api/auth/refresh</c> or the regex <c>~^/api/admin(/.*)?$</c>, Caddy writes
    /// <c>/api/admin /api/admin/*</c> inside a <c>path</c> matcher. Tokens carrying <c>/api</c>
    /// anywhere are kept; the rest of each line (directives, the trailing <c>1;</c>, comments)
    /// is not a path and is ignored.
    /// </summary>
    private static IEnumerable<string> AllowListEntries(string configPath) =>
        File.ReadLines(configPath)
            .SelectMany(line => line.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .Select(token => token.TrimEnd(';', ','))
            .Where(token => token.Contains("/api", StringComparison.Ordinal));

    /// <summary>
    /// True when an allow-list entry permits a path: a <c>~</c>-prefixed entry is a regex, a
    /// trailing <c>*</c> is a prefix, and anything else must match the path exactly.
    /// </summary>
    private static bool Permits(string entry, string path)
    {
        if (entry.StartsWith('~'))
            return Regex.IsMatch(path, entry[1..]);

        if (entry.EndsWith('*'))
            return path.StartsWith(entry[..^1], StringComparison.OrdinalIgnoreCase);

        return string.Equals(entry, path, StringComparison.OrdinalIgnoreCase);
    }

    private static string StripQuery(string raw)
    {
        var queryStart = raw.IndexOfAny(['?', '#']);
        return queryStart >= 0 ? raw[..queryStart] : raw;
    }

    /// <summary>
    /// True when a client path addresses a route template: the same number of segments, with every
    /// literal segment equal ignoring case (routing is case-insensitive, and <c>[controller]</c>
    /// expansion leaves the class casing in the template). A <c>{}</c> on either side - an
    /// interpolated value or a route parameter - matches any single segment.
    /// </summary>
    private static bool Matches(string template, string path)
    {
        var left = template.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var right = path.Split('/', StringSplitOptions.RemoveEmptyEntries);

        if (left.Length != right.Length)
            return false;

        for (var i = 0; i < left.Length; i++)
        {
            if (left[i] == "{}" || right[i] == "{}")
                continue;
            if (!left[i].Equals(right[i], StringComparison.OrdinalIgnoreCase))
                return false;
        }

        return true;
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "admin", "src")) &&
                Directory.Exists(Path.Combine(directory.FullName, "backend", "src")))
                return directory.FullName;

            directory = directory.Parent;
        }

        throw new InvalidOperationException(
            "Could not locate the repository root (a directory holding both admin/src and backend/src) " +
            $"walking up from {AppContext.BaseDirectory}.");
    }
}