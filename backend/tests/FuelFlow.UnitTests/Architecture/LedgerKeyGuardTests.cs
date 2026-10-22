using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using FluentAssertions;

namespace FuelFlow.UnitTests.Architecture;

/// <summary>
/// Architecture guard: the ledger's keys are time-ordered.
/// <remarks>
/// <para>
/// Every entity key in this platform is a UUIDv7 - a timestamp followed by randomness - except
/// where a key is deliberately NOT an entity key: the refresh-token family id (it must not say
/// when it was minted), the JWT jti, idempotency-key suffixes. The ledger was the one place
/// that broke the convention, and it broke it on the three tables that matter most:
/// <c>journal_entries</c>, <c>journal_lines</c> and <c>accounting_periods</c>.
/// </para>
/// <para>
/// The cost is not a style preference. <c>journal_lines</c> gets two rows per accounting fact
/// and grows forever, and a random key lands every one of them on a random leaf of the B-tree:
/// the index is written across the whole key space instead of at its right-hand edge, the pages
/// churn, and the table bloats faster than it needs to. UUIDv7 exists to avoid exactly this.
/// </para>
/// <para>
/// The guard reads the SOURCE, not the behaviour, because the behaviour is invisible from the
/// outside: a random key and a time-ordered key are both valid uuids, and nothing in the schema
/// says which one a row got. A rule nobody checks is a rule one afternoon of "just one more
/// refactor" erases - and the next person to add a row to the ledger would have no way of
/// knowing that the keys around it are ordered.
/// </para>
/// </remarks>
public sealed class LedgerKeyGuardTests
{
    [Fact]
    public void The_Ledger_Should_Not_Write_A_Random_Key()
    {
        var offenders = new List<string>();

        foreach (var file in LedgerSourceFiles())
        {
            var source = WithoutComments(File.ReadAllText(file));
            var relative = Path.GetRelativePath(LedgerFolder(), file).Replace('\\', '/');

            foreach (var (pattern, what) in new[]
                     {
                         ("Guid.NewGuid(", "Guid.NewGuid()"),
                         ("gen_random_uuid(", "gen_random_uuid()")
                     })
            {
                var index = source.IndexOf(pattern, StringComparison.Ordinal);
                if (index >= 0)
                {
                    offenders.Add($"{relative}: {what} writes a random key");
                }
            }
        }

        offenders.Should().BeEmpty(
            "the ledger's keys are time-ordered everywhere else in the platform, and a random key "
            + "on the table that grows forever scatters every insert across the B-tree. "
            + "Guid.CreateVersion7() is the convention; gen_random_uuid() is not.");
    }

    [Fact]
    public void The_Ledger_Should_Write_Time_Ordered_Keys()
    {
        var source = WithoutComments(File.ReadAllText(
            Path.Combine(LedgerFolder(), "JournalPostingService.cs")));

        source.Should().Contain("Guid.CreateVersion7()",
            "the posting service writes the entry, the line and the period, and all three are "
            + "entity keys in the ledger");
    }

    /// <summary>
    /// Every source file of the ledger feature. The migrations are not in here: they are
    /// generated, they are rewritten by every model change, and the key they write is the one
    /// the database builds for itself.
    /// </summary>
    private static IEnumerable<string> LedgerSourceFiles()
        => Directory
            .EnumerateFiles(LedgerFolder(), "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Replace('\\', '/').Contains("/bin/"))
            .Where(path => !path.Replace('\\', '/').Contains("/obj/"))
            .OrderBy(path => path, StringComparer.Ordinal);

    /// <summary>
    /// The comments are stripped first: a file that explains the change by naming the old call
    /// would otherwise fail the guard for the explanation.
    /// </summary>
    private static string WithoutComments(string source)
    {
        var withoutBlockComments = Regex.Replace(source, @"/\*.*?\*/", " ", RegexOptions.Singleline);
        return Regex.Replace(withoutBlockComments, @"//[^\n]*", " ");
    }

    private static string LedgerFolder()
        => Path.Combine(LocateBackend(), "src", "FuelFlow.API", "Features", "Ledger");

    /// <summary>
    /// The backend's root - the folder that holds <c>src</c>. Taken from this file's own path
    /// when it is there, and from the assembly's directory when the tests run from somewhere
    /// else.
    /// </summary>
    private static string LocateBackend([CallerFilePath] string thisFile = "")
    {
        // thisFile = .../backend/tests/FuelFlow.UnitTests/Architecture/<this>.cs
        var backend = Directory.GetParent(thisFile)!.Parent!.Parent!.Parent!.FullName;
        if (Directory.Exists(Path.Combine(backend, "src", "FuelFlow.API", "Features", "Ledger")))
            return backend;

        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var probe = Path.Combine(dir.FullName, "src", "FuelFlow.API", "Features", "Ledger");
            if (Directory.Exists(probe)) return dir.FullName;

            var nested = Path.Combine(dir.FullName, "backend", "src", "FuelFlow.API", "Features", "Ledger");
            if (Directory.Exists(nested)) return Path.Combine(dir.FullName, "backend");

            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException(
            "Could not locate backend/src/FuelFlow.API/Features/Ledger from either the test file path or the assembly directory.");
    }
}
