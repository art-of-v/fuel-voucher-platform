using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using FluentAssertions;

namespace FuelFlow.UnitTests.Architecture;

/// <summary>
/// Architecture guard for the query-filter bypass.
///
/// The global filters are read policies (soft delete, released fulfilments). A raw
/// <c>IgnoreQueryFilters()</c> lifts every filter on every entity in the query, including one added
/// years after the call site by someone who never saw it - which is how a narrow admin read turns
/// into a standing exception. The only bypass is the named set in
/// <c>SharedKernel/Database/QueryFilterScopes.cs</c>, and every use of one is listed with its
/// reason in <c>docs/QUERY_FILTERS.md</c>.
///
/// This test fails when a raw bypass appears, when a scope is used at an undocumented site, when a
/// documented count drifts from the code, or when a row names a file that no longer uses the scope.
/// See planning #689.
/// </summary>
public sealed class QueryFilterBypassGuardTests
{
    private static readonly string ScopeFile = "backend/src/FuelFlow.API/SharedKernel/Database/QueryFilterScopes.cs";

    private static readonly string[] Scopes =
    [
        "IncludingSoftDeletedAndReleasedFulfilments",
        "IncludingSoftDeleted",
        "IncludingReleasedFulfilments",
    ];

    [Fact]
    public void Only_the_scopes_lift_a_model_filter()
    {
        var apiRoot = LocateApiProjectRoot();
        var violations = new List<string>();

        foreach (var path in Directory.EnumerateFiles(apiRoot, "*.cs", SearchOption.AllDirectories))
        {
            var relative = ToRepositoryPath(apiRoot, path);
            if (relative is null || relative == ScopeFile)
                continue;

            if (StripComments(File.ReadAllText(path)).Contains("IgnoreQueryFilters"))
                violations.Add(relative);
        }

        violations.Should().BeEmpty(
            "a filter may only be lifted through the named scopes in QueryFilterScopes.cs, with the " +
            "call site listed in docs/QUERY_FILTERS.md. Offenders:{0}",
            string.Join(", ", violations));
    }

    [Fact]
    public void Every_scope_use_is_in_the_inventory()
    {
        var apiRoot = LocateApiProjectRoot();
        var documented = ReadInventory(LocateRepositoryRoot());
        var used = ScanScopeUses(apiRoot);

        used.Should().BeEquivalentTo(documented,
            because: "docs/QUERY_FILTERS.md is the allowlist: every scope use must be listed with an " +
                     "exact count and a reason, and no listed row may outlive the code it describes.");

        var undocumented = used.Keys.Except(documented.Keys).OrderBy(k => k.ToString()).ToList();
        undocumented.Should().BeEmpty(
            "add these to docs/QUERY_FILTERS.md with the count and the reason:{0}",
            string.Join(", ", undocumented));

        var stale = documented.Keys.Except(used.Keys).OrderBy(k => k.ToString()).ToList();
        stale.Should().BeEmpty(
            "these inventory rows name a scope that no longer exists at that path:{0}",
            string.Join(", ", stale));

        var drifted = used.Keys.Intersect(documented.Keys)
            .Where(k => used[k] != documented[k])
            .Select(k => $"{k.File} {k.Scope}: code={used[k]}, docs={documented[k]}")
            .OrderBy(s => s)
            .ToList();
        drifted.Should().BeEmpty(
            "the inventory's counts must match the code, or the allowlist stops describing what is " +
            "actually bypassed:{0}",
            string.Join(", ", drifted));
    }

    private static Dictionary<(string File, string Scope), int> ScanScopeUses(string apiRoot)
    {
        var counts = new Dictionary<(string, string), int>();

        foreach (var path in Directory.EnumerateFiles(apiRoot, "*.cs", SearchOption.AllDirectories))
        {
            var relative = ToRepositoryPath(apiRoot, path);
            if (relative is null)
                continue;

            var text = StripComments(File.ReadAllText(path));
            foreach (var scope in Scopes)
            {
                var count = Regex.Matches(text, @"\." + scope + @"\s*\(").Count;
                if (count > 0)
                    counts[(relative, scope)] = count;
            }
        }

        return counts;
    }

    private static Dictionary<(string File, string Scope), int> ReadInventory(string repositoryRoot)
    {
        var path = Path.Combine(repositoryRoot, "docs", "QUERY_FILTERS.md");
        File.Exists(path).Should().BeTrue("docs/QUERY_FILTERS.md is the allowlist this guard reads");

        var counts = new Dictionary<(string, string), int>();
        foreach (var line in File.ReadLines(path))
        {
            if (!line.TrimStart().StartsWith('|'))
                continue;

            var cells = line.Split('|', StringSplitOptions.TrimEntries);
            if (cells.Length < 5)
                continue;

            var file = cells[1].Trim('`');
            var scope = cells[2].Trim('`');
            if (!Scopes.Contains(scope))
                continue;

            int.TryParse(cells[3], out var sites).Should().BeTrue(
                $"the Sites cell for {file} / {scope} must be an integer");
            if (sites <= 0)
                continue;

            counts.Should().NotContainKey((file, scope),
                $"{file} / {scope} is listed twice in docs/QUERY_FILTERS.md");
            counts[(file, scope)] = sites;
        }

        return counts;
    }

    private static string? ToRepositoryPath(string apiRoot, string path)
    {
        var normalized = path.Replace('\\', '/');
        if (normalized.Contains("/Migrations/") ||
            normalized.Contains("/bin/") ||
            normalized.Contains("/obj/"))
        {
            return null;
        }

        return "backend/src/FuelFlow.API/" + Path.GetRelativePath(apiRoot, path).Replace('\\', '/');
    }

    private static string StripComments(string text)
    {
        var withoutBlock = Regex.Replace(text, @"/\*.*?\*/", " ", RegexOptions.Singleline);
        return Regex.Replace(withoutBlock, @"//[^\n]*", " ");
    }

    private static string LocateApiProjectRoot([CallerFilePath] string thisFile = "")
    {
        var repositoryRoot = LocateRepositoryRoot(thisFile);
        return Path.Combine(repositoryRoot, "backend", "src", "FuelFlow.API");
    }

    private static string LocateRepositoryRoot([CallerFilePath] string thisFile = "")
    {
        // thisFile = .../backend/tests/FuelFlow.UnitTests/Architecture/<this>.cs
        var repositoryRoot = Directory.GetParent(thisFile)!.Parent!.Parent!.Parent!.Parent!.FullName;
        if (Directory.Exists(Path.Combine(repositoryRoot, "backend", "src", "FuelFlow.API")))
            return repositoryRoot;

        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "backend", "src", "FuelFlow.API")))
                return dir.FullName;
            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException(
            "Could not locate the repository root from either the test file path or the assembly directory.");
    }
}
