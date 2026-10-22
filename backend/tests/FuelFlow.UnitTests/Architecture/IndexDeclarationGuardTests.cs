using System.Runtime.CompilerServices;
using System.Text;
using FluentAssertions;
using FuelFlow.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FuelFlow.UnitTests.Architecture;

/// <summary>
/// No index is declared twice.
/// </summary>
/// <remarks>
/// <para>
/// EF Core's <c>HasIndex</c> does not create a second index when the same columns are named
/// again: it finds the index that is already there and EDITS it. A second declaration over the
/// same columns is therefore invisible in the model - there is one index, and it carries the
/// name, the filter and the sort of whichever declaration ran last.
/// </para>
/// <para>
/// That is how four indexes in this schema came to be dead code: a plain index was declared, a
/// filtered one over the same column was declared after it, and the plain one was gone - with
/// the second declaration reading as if two indexes existed. The outbox's is the one that cost
/// something: the plain index on <c>created_at_utc</c> was edited into the dispatchers' partial
/// index, and the nightly retention purge - which asks for the opposite set of rows - was left
/// with no index at all and read the whole table.
/// </para>
/// <para>
/// The guard reads the configuration files, because that is where the duplication is: the model
/// cannot show it, the database cannot show it, and the only thing that can is the source.
/// </para>
/// </remarks>
public sealed class IndexDeclarationGuardTests
{
    /// <summary>
    /// No configuration class declares the same index twice.
    /// </summary>
    [Fact]
    public void No_Configuration_Should_Declare_The_Same_Index_Twice()
    {
        var root = LocateRepositoryRoot();
        var source = Path.Combine(root, "backend", "src");

        var scanned = 0;
        var offenders = new List<string>();

        foreach (var path in EnumerateConfigurationSources(source))
        {
            var declarations = Declarations(File.ReadAllText(path));
            scanned += declarations.Count;

            foreach (var duplicate in declarations
                .GroupBy(d => (d.Class, d.Arguments))
                .Where(group => group.Count() > 1))
            {
                var lines = string.Join(", ", duplicate.Select(d => d.Line));
                offenders.Add(
                    $"{Path.GetRelativePath(root, path).Replace('\\', '/')}: "
                    + $"{duplicate.Key.Class} declares HasIndex({duplicate.Key.Arguments}) "
                    + $"{duplicate.Count()} times (lines {lines})");
            }
        }

        scanned.Should().BeGreaterThan(
            0,
            "the repository's own configurations should be visible to this test; an empty scan "
            + "would pass forever");

        offenders.Should().BeEmpty(
            "EF Core edits the index that is already there instead of creating a second one, so a "
            + "declaration over columns that are already indexed is dead code that reads as if two "
            + "indexes exist - and the next person to add a filter to one of them sees no effect. "
            + "The outbox's plain created_at_utc index was edited into the dispatchers' partial "
            + "index this way, and the nightly purge was left reading the whole table.");
    }

    /// <summary>
    /// And the model holds no two indexes over the same columns.
    /// </summary>
    /// <remarks>
    /// The model's index set is what the database is built from, so two indexes over the same
    /// columns there are two indexes in production that one of them is not. Read through the
    /// columns rather than through the properties, because two different properties mapped to
    /// one column are the same index twice as far as the planner is concerned.
    /// </remarks>
    [Fact]
    public void The_Model_Should_Not_Hold_Two_Indexes_Over_The_Same_Columns()
    {
        var context = BuildContext();

        var offenders = new List<string>();

        foreach (var entityType in context.Model.GetEntityTypes())
        {
            var seen = new Dictionary<string, string>(StringComparer.Ordinal);

            foreach (var index in entityType.GetIndexes())
            {
                var columns = string.Join(", ", index.Properties.Select(p => p.GetColumnName() ?? p.Name));
                var name = index.GetDatabaseName() ?? "(unnamed)";

                if (seen.TryGetValue(columns, out var first))
                {
                    offenders.Add(
                        $"{entityType.GetTableName()}: {first} and {name} are both over ({columns})");
                }
                else
                {
                    seen[columns] = name;
                }
            }
        }

        offenders.Should().BeEmpty(
            "two indexes over the same columns are two indexes to write and to maintain, and only "
            + "one of them is ever chosen");
    }

    /// <summary>
    /// One declaration: the class it is in, the columns it names, and the line it is on.
    /// </summary>
    private sealed record Declaration(string Class, string Arguments, int Line);

    private static List<Declaration> Declarations(string text)
    {
        var code = StripComments(text);
        var result = new List<Declaration>();
        var index = 0;

        while (true)
        {
            var at = code.IndexOf("HasIndex(", index, StringComparison.Ordinal);
            if (at < 0)
            {
                break;
            }

            index = at + 1;

            var open = at + "HasIndex".Length;
            var close = MatchingParen(code, open);
            if (close < 0)
            {
                continue;
            }

            result.Add(new Declaration(
                EnclosingClass(code, at),
                Normalize(code[(open + 1)..close]),
                LineOf(code, at)));
        }

        return result;
    }

    /// <summary>
    /// The configuration files, and nothing else: the migrations are EF's own model snapshots,
    /// and they repeat every index of every table on purpose.
    /// </summary>
    private static IEnumerable<string> EnumerateConfigurationSources(string sourceRoot)
    {
        foreach (var path in Directory.EnumerateFiles(sourceRoot, "*.cs", SearchOption.AllDirectories))
        {
            if (path.Contains($"{Path.DirectorySeparatorChar}Migrations{Path.DirectorySeparatorChar}",
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            yield return path;
        }
    }

    /// <summary>
    /// The comments are blanked out rather than deleted, so the line numbers stay the ones the
    /// file really has. A comment that says "HasIndex(e => e.CreatedAtUtc)" is a sentence, not a
    /// declaration.
    /// </summary>
    private static string StripComments(string text)
    {
        var builder = new StringBuilder(text.Length);
        var i = 0;

        while (i < text.Length)
        {
            var c = text[i];

            if (c == '/' && i + 1 < text.Length && text[i + 1] == '/')
            {
                while (i < text.Length && text[i] != '\n')
                {
                    builder.Append(' ');
                    i++;
                }

                continue;
            }

            if (c == '/' && i + 1 < text.Length && text[i + 1] == '*')
            {
                builder.Append("  ");
                i += 2;

                while (i < text.Length && !(text[i] == '*' && i + 1 < text.Length && text[i + 1] == '/'))
                {
                    builder.Append(text[i] == '\n' ? '\n' : ' ');
                    i++;
                }

                if (i < text.Length)
                {
                    builder.Append("  ");
                    i += 2;
                }

                continue;
            }

            if (c == '"')
            {
                var raw = text.AsSpan(i).StartsWith("\"\"\"", StringComparison.Ordinal);
                var quotes = raw ? 3 : 1;
                builder.Append(text, i, quotes);
                i += quotes;

                while (i < text.Length)
                {
                    if (text[i] == '\\' && !raw)
                    {
                        builder.Append(text, i, Math.Min(2, text.Length - i));
                        i += Math.Min(2, text.Length - i);
                        continue;
                    }

                    if (text[i] == '"')
                    {
                        if (!raw)
                        {
                            builder.Append('"');
                            i++;
                            break;
                        }

                        if (text.AsSpan(i).StartsWith("\"\"\"", StringComparison.Ordinal))
                        {
                            builder.Append("\"\"\"");
                            i += 3;
                            break;
                        }
                    }

                    builder.Append(text[i]);
                    i++;
                }

                continue;
            }

            builder.Append(c);
            i++;
        }

        return builder.ToString();
    }

    private static int MatchingParen(string code, int open)
    {
        var depth = 0;

        for (var i = open; i < code.Length; i++)
        {
            if (code[i] == '(')
            {
                depth++;
            }
            else if (code[i] == ')')
            {
                depth--;
                if (depth == 0)
                {
                    return i;
                }
            }
        }

        return -1;
    }

    /// <summary>
    /// The class the declaration is in - the nearest one above it.
    /// </summary>
    private static string EnclosingClass(string code, int at)
    {
        var classAt = code.LastIndexOf("class ", at, StringComparison.Ordinal);
        if (classAt < 0)
        {
            return "(unknown)";
        }

        var nameStart = classAt + "class ".Length;
        var nameEnd = nameStart;

        while (nameEnd < code.Length && (char.IsLetterOrDigit(code[nameEnd]) || code[nameEnd] == '_'))
        {
            nameEnd++;
        }

        return code[nameStart..nameEnd];
    }

    private static int LineOf(string code, int at)
        => code[..at].Count(c => c == '\n') + 1;

    /// <summary>
    /// The columns, with the whitespace out of them: `e =&gt; e.CreatedAtUtc` and
    /// `e=&gt;e.CreatedAtUtc` are the same index.
    /// </summary>
    private static string Normalize(string arguments)
        => string.Concat(arguments.Where(c => !char.IsWhiteSpace(c)));

    /// <summary>
    /// The model, built against the relational provider so the indexes have columns. No connection
    /// is opened: the model is a mapping, not a query.
    /// </summary>
    private static ApplicationDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql("Host=localhost;Port=5432;Database=fuelflow;Username=postgres;Password=postgres")
            .Options;

        return new ApplicationDbContext(options, TestQrCipher.Instance);
    }

    /// <summary>
    /// Resolves the repository root from this test file's compile-time location, with a fallback
    /// that walks up from the test assembly's directory.
    /// </summary>
    private static string LocateRepositoryRoot([CallerFilePath] string thisFile = "")
    {
        // thisFile = .../backend/tests/FuelFlow.UnitTests/Architecture/<this>.cs
        // Up 3: Architecture -> FuelFlow.UnitTests -> tests -> backend, and one more for the root.
        var root = Directory.GetParent(thisFile)!.Parent!.Parent!.Parent!.Parent!.FullName;
        if (Directory.Exists(Path.Combine(root, "backend"))) return root;

        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "backend"))
                && Directory.Exists(Path.Combine(dir.FullName, "admin")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException(
            "Could not locate the repository root from either the test file path or the assembly "
            + "directory.");
    }
}
