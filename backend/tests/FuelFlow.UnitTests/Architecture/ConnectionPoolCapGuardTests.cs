using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using FluentAssertions;

namespace FuelFlow.UnitTests.Architecture;

/// <summary>
/// Architecture guard for the connection-pool cap.
///
/// A process opens more than one pool against Postgres: the application's own data source, the
/// Hangfire storage, the Postgres health check - and the worker opens its own set. Npgsql's
/// default cap is 100 per pool, so the cap the application applied to ONE of them protected
/// nothing during the incident it was written for. The compose file's "the application's pool is
/// capped at 5, so this is insurance against a connection storm" was true of one pool out of
/// four, and the three that could storm the server were the ones left uncapped.
///
/// The guard fails the build when a pool is opened from a connection string that carries no cap,
/// so the next pool somebody adds does not silently come up with Npgsql's default of 100.
/// </summary>
public sealed class ConnectionPoolCapGuardTests
{
    private static readonly string[] PoolOpeners =
    [
        "UseNpgsqlConnection(",
        "AddNpgSql(",
        "UseNpgsql("
    ];

    [Fact]
    public void Every_Pool_Should_Be_Opened_With_A_Capped_Connection_String()
    {
        var srcRoot = LocateSrcRoot();
        var violations = new List<string>();

        foreach (var file in Directory.EnumerateFiles(srcRoot, "*.cs", SearchOption.AllDirectories))
        {
            var text = File.ReadAllText(file);
            var relative = Path.GetRelativePath(srcRoot, file);
            var cappedVariables = CappedVariableNames(text);

            foreach (var opener in PoolOpeners)
            {
                var index = 0;
                while ((index = text.IndexOf(opener, index, StringComparison.Ordinal)) >= 0)
                {
                    var argument = ExtractArgument(text, index + opener.Length - 1);
                    index += opener.Length;

                    if (argument is null)
                        continue;

                    if (IsCapped(argument, cappedVariables))
                        continue;

                    violations.Add($"{relative}: {opener.TrimEnd('(')} was given an uncapped connection string.");
                }
            }
        }

        violations.Should().BeEmpty(
            "every pool in the process needs the cap - use DatabaseConnectionPool.WithPoolSettings " +
            "(or WithMaxPoolSize, for the pool whose SQL is a library's and not ours). Offenders:{0}",
            string.Join(" | ", violations));
    }

    /// <summary>
    /// The names of the local variables that hold an already capped connection string - or a data
    /// source built from one - so a pool opened from one of them is not reported as uncapped.
    /// </summary>
    private static HashSet<string> CappedVariableNames(string text)
    {
        var assignments = new Dictionary<string, string>(StringComparer.Ordinal);
        // The "=" must be an assignment: "==" and "=>" are not, and a match that started on one of
        // those would swallow the real assignments that follow it. The right-hand side stays on one
        // line, so a parameter default cannot swallow the statement that follows it.
        foreach (Match match in Regex.Matches(text, @"\b([A-Za-z_]\w*)\s*=(?![=>])\s*([^\r\n;]+);"))
        {
            assignments[match.Groups[1].Value] = match.Groups[2].Value;
        }

        var capped = new HashSet<string>(StringComparer.Ordinal);
        var changed = true;
        while (changed)
        {
            changed = false;
            foreach (var (name, rightHandSide) in assignments)
            {
                if (capped.Contains(name))
                    continue;

                var carriesTheCap = rightHandSide.Contains("WithPoolSettings", StringComparison.Ordinal)
                    || rightHandSide.Contains("WithMaxPoolSize", StringComparison.Ordinal)
                    || capped.Any(cappedName => Regex.IsMatch(rightHandSide, $@"\b{Regex.Escape(cappedName)}\b"));

                if (!carriesTheCap)
                    continue;

                capped.Add(name);
                changed = true;
            }
        }

        return capped;
    }

    private static bool IsCapped(string argument, HashSet<string> cappedVariables)
    {
        if (argument.Contains("WithPoolSettings", StringComparison.Ordinal)
            || argument.Contains("WithMaxPoolSize", StringComparison.Ordinal)
            || argument.Contains("Maximum Pool Size", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return cappedVariables.Any(name => Regex.IsMatch(argument, $@"\b{Regex.Escape(name)}\b"));
    }

    /// <summary>
    /// Returns the text between the parenthesis that opens at <paramref name="openParenIndex"/> and
    /// its matching close, so an argument that spans several lines is read whole.
    /// </summary>
    private static string? ExtractArgument(string text, int openParenIndex)
    {
        var depth = 0;
        for (var i = openParenIndex; i < text.Length; i++)
        {
            var c = text[i];
            if (c == '(')
            {
                depth++;
            }
            else if (c == ')')
            {
                depth--;
                if (depth == 0)
                    return text[(openParenIndex + 1)..i];
            }
        }

        return null;
    }

    private static string LocateSrcRoot([CallerFilePath] string thisFile = "")
    {
        // thisFile = .../backend/tests/FuelFlow.UnitTests/Architecture/<this>.cs
        var backend = Directory.GetParent(thisFile)!.Parent!.Parent!.Parent!.FullName;
        var candidate = Path.Combine(backend, "src");
        if (Directory.Exists(candidate)) return candidate;

        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var path = Path.Combine(dir.FullName, "src");
            if (Directory.Exists(Path.Combine(path, "FuelFlow.API"))) return path;
            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("backend/src was not found from the test assembly.");
    }
}
