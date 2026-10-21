using System.Runtime.CompilerServices;
using FluentAssertions;
using FuelFlow.SharedKernel.Time;
using FuelFlow.Testing;

namespace FuelFlow.UnitTests.Architecture;

/// <summary>
/// The guard that keeps the suite off the wall clock (planning finding C-10, issue #540).
///
/// </summary>
/// <remarks>
/// <para>
/// A test that reads the wall clock can fail at midnight, at the end of a month, or across a DST
/// boundary - and when it does, the failure looks like a product bug. Measured on 2026-10-21, 189
/// files under <c>backend/tests</c> read the clock; the same day, 114 files under
/// <c>backend/src</c> did. The suite ran on a fixed instant instead, and this guard is what keeps
/// it there.
/// </para>
/// <para>
/// The rule it enforces: <b>the application reads the clock through
/// <see cref="SystemClock"/>, and the tests read it through <see cref="TestClock"/></b>. Both are
/// the same instant, so a test that says "now" and the code under test that says "now" mean the
/// same thing - whatever day the suite runs on.
/// </para>
/// <para>
/// Comments are not scanned. A comment may name the old API while it explains why the new one
/// exists, and a guard that failed on that would be a guard people learned to work around.
/// </para>
/// </remarks>
public sealed class WallClockGuardTests
{
    /// <summary>
    /// The forbidden reads, spelled in two pieces so that this file does not trip its own guard.
    /// </summary>
    private static readonly string[] ForbiddenReads =
    [
        "DateTime" + ".UtcNow",
        "DateTime" + ".Now",
        "DateTimeOffset" + ".UtcNow",
        "DateTimeOffset" + ".Now"
    ];

    /// <summary>
    /// The suite runs on a fixed instant, and the production code under test reads the same one.
    /// </summary>
    [Fact]
    public void The_Suite_Should_Run_On_A_Fixed_Clock()
    {
        SystemClock.UtcNow.Should().Be(
            TestClock.UtcNow,
            "the module initializer installs the fixed clock before any test in this assembly runs");

        // A clock that stands still: two reads a moment apart are the same instant, whatever the
        // machine's clock says.
        var first = SystemClock.UtcNow;
        Thread.Sleep(20);
        SystemClock.UtcNow.Should().Be(first, "the clock the suite runs on must not move");
    }

    /// <summary>
    /// Nothing in the application or in the suite reads the wall clock.
    /// </summary>
    [Fact]
    public void No_Source_File_Should_Read_The_Wall_Clock()
    {
        var backend = LocateBackendRoot();
        var violations = new List<string>();

        foreach (var file in SourceFiles(backend))
        {
            var relative = Path.GetRelativePath(backend, file);
            var lines = File.ReadAllLines(file);

            for (var i = 0; i < lines.Length; i++)
            {
                var code = CodeOf(lines[i]);
                if (code.Length == 0)
                {
                    continue;
                }

                foreach (var forbidden in ForbiddenReads)
                {
                    if (code.Contains(forbidden, StringComparison.Ordinal))
                    {
                        violations.Add($"{relative}:{i + 1} reads {forbidden}");
                    }
                }
            }
        }

        violations.Should().BeEmpty(
            "the clock is read through SystemClock (the application) and TestClock (the suite), " +
            "never through the machine. Offenders:{0}",
            string.Join(" | ", violations));
    }

    /// <summary>
    /// Every C# file of the application and of the three test projects - and nothing else.
    /// </summary>
    /// <remarks>
    /// The migrations are exempt, and deliberately so: they are the frozen history of the schema,
    /// they are applied to production as they were written, and they are not code anybody runs.
    /// The build output directories are skipped because they hold copies of the same sources.
    /// </remarks>
    private static IEnumerable<string> SourceFiles(string backend)
    {
        var roots = new[] { Path.Combine(backend, "src"), Path.Combine(backend, "tests") };

        foreach (var root in roots)
        {
            foreach (var file in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
            {
                if (file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                    || file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                    || file.Contains($"{Path.DirectorySeparatorChar}Migrations{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
                {
                    continue;
                }

                yield return file;
            }
        }
    }

    /// <summary>
    /// The line's code, with the trailing comment removed. A comment may name the API this guard
    /// forbids; code may not.
    /// </summary>
    private static string CodeOf(string line)
    {
        var trimmed = line.TrimStart();
        if (trimmed.StartsWith("//", StringComparison.Ordinal)
            || trimmed.StartsWith("*", StringComparison.Ordinal)
            || trimmed.StartsWith("/*", StringComparison.Ordinal))
        {
            return string.Empty;
        }

        var comment = line.IndexOf("//", StringComparison.Ordinal);
        if (comment < 0)
        {
            return line;
        }

        // A "//" inside a string is not a comment. An odd number of quotes before it means the
        // line has an open string, so the "//" belongs to that string.
        var before = line[..comment];
        return before.Count(c => c == '"') % 2 == 1 ? line : before;
    }

    private static string LocateBackendRoot([CallerFilePath] string thisFile = "")
    {
        // thisFile = .../backend/tests/FuelFlow.UnitTests/Architecture/<this>.cs
        var backend = Directory.GetParent(thisFile)!.Parent!.Parent!.Parent!.FullName;
        if (Directory.Exists(Path.Combine(backend, "src")) && Directory.Exists(Path.Combine(backend, "tests")))
        {
            return backend;
        }

        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "backend");
            if (Directory.Exists(Path.Combine(candidate, "src")) && Directory.Exists(Path.Combine(candidate, "tests")))
            {
                return candidate;
            }

            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("backend/src and backend/tests were not found from the test assembly.");
    }
}
