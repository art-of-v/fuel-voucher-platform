using System.Runtime.CompilerServices;
using FluentAssertions;

namespace FuelFlow.UnitTests.Architecture;

/// <summary>
/// The repository root carries no scratch file, and the build files that make the build
/// reproducible are where they are supposed to be.
/// </summary>
/// <remarks>
/// <para>
/// The concrete failure this exists for: five files sat at the root of the repository, tracked
/// in git, where anybody cloning it would find them.
/// </para>
/// <list type="bullet">
/// <item><description>
/// <c>fix_compose.py</c>, <c>fix_compose2.py</c> and <c>fix_staging.py</c> - one-shot scripts that
/// rewrote the Postgres tuning block in the compose files. They had already run; the strings
/// they assert on were replaced by the very run they performed, so every one of them now fails
/// its own assertion. A script that cannot run is not a tool, it is a trap with a Python
/// extension.
/// </description></item>
/// <item><description>
/// <c>msg-fixdown.txt</c> and <c>msg-outbox.txt</c> - commit messages that were written to a file
/// so a shell command could carry them, and then never deleted. The commits they describe are in
/// the history; the files are the draft, kept.
/// </description></item>
/// </list>
/// <para>
/// The rule is deliberately about the SHAPE rather than about these five names: the root may hold
/// configuration and documentation, and nothing else. A scratch file at the root is invisible in
/// review - a reviewer reads the diff, and the diff of a new root file is four lines of Python
/// that look like anybody's helper - and it stays there for years because removing it is nobody's
/// job. The same shape as the other guards here: a rule nobody checks is a rule one afternoon of
/// "just one more refactor" erases.
/// </para>
/// <para>
/// The second half of the file is the other side of the same finding: the build settings that
/// make a build reproducible - the editor conventions, the pinned SDK, the shared MSBuild
/// properties and the resolved package graph - were absent, so nothing held them in place and
/// nothing could say when one of them went missing.
/// </para>
/// </remarks>
public sealed class RepositoryHousekeepingGuardTests
{
    /// <summary>
    /// The extensions a scratch file wears: a script, a patch, or a log of something that
    /// happened once. None of them belongs at the root of a repository that is read by other
    /// people.
    /// </summary>
    private static readonly string[] ScratchExtensions =
    [
        ".py", ".txt", ".sh", ".ps1", ".bat", ".cmd",
        ".bak", ".tmp", ".orig", ".rej", ".log", ".patch", ".diff",
    ];

    /// <summary>
    /// The build files, at the root, where the tools look for them.
    /// </summary>
    private static readonly string[] BuildFiles =
    [
        ".editorconfig",
        "Directory.Build.props",
        "global.json",
    ];

    [Fact]
    public void The_Repository_Root_Should_Carry_No_Scratch_File()
    {
        var root = LocateRepositoryRoot();

        var offenders = Directory
            .EnumerateFiles(root, "*", SearchOption.TopDirectoryOnly)
            .Where(path => ScratchExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
            .Select(path => Path.GetFileName(path))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        offenders.Should().BeEmpty(
            "the repository root is what everybody sees first, and a script or a scratch file there "
            + "is invisible in review - a reviewer reads the diff, and the diff of a new root file "
            + "looks like anybody's helper. The five that were there are gone (fix_compose.py, "
            + "fix_compose2.py, fix_staging.py, msg-fixdown.txt, msg-outbox.txt); the rule is here "
            + "so the sixth does not arrive");
    }

    [Fact]
    public void The_Repository_Should_Carry_Its_Build_Files()
    {
        var root = LocateRepositoryRoot();

        var missing = BuildFiles
            .Where(name => !File.Exists(Path.Combine(root, name)))
            .ToArray();

        missing.Should().BeEmpty(
            "the build settings that make a build reproducible live in three files at the root: "
            + "the editor conventions (.editorconfig), the shared MSBuild properties "
            + "(Directory.Build.props) and the pinned SDK (global.json). A missing one does not "
            + "announce itself - it just makes the build depend on the machine it runs on");
    }

    [Fact]
    public void Every_Project_Should_Carry_A_Packages_Lock_File()
    {
        var root = LocateRepositoryRoot();
        var backend = Path.Combine(root, "backend");

        var projects = Directory
            .EnumerateFiles(backend, "*.csproj", SearchOption.AllDirectories)
            .Where(path => !path.Replace('\\', '/').Contains("/bin/"))
            .Where(path => !path.Replace('\\', '/').Contains("/obj/"))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

        projects.Should().NotBeEmpty(
            "the backend tree should be visible to this test; an empty scan would pass forever");

        var missing = projects
            .Where(project => !File.Exists(
                Path.Combine(Path.GetDirectoryName(project)!, "packages.lock.json")))
            .Select(project => Path.GetRelativePath(root, project).Replace('\\', '/'))
            .ToArray();

        missing.Should().BeEmpty(
            "without a lock file the TRANSITIVE versions float: the direct PackageReference says "
            + "the same thing, but the version of the package it pulls in is chosen at restore "
            + "time, and a commit that changed nothing can restore something different. The lock "
            + "file is regenerated by `dotnet restore` and committed with the change that moved a "
            + "version");
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
