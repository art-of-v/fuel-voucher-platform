using System.Runtime.CompilerServices;
using FluentAssertions;

namespace FuelFlow.UnitTests.Architecture;

/// <summary>
/// The source carries no CP437-mojibake: no file may contain the fingerprint of a UTF-8 file
/// that was read as CP437 and written back as UTF-8.
/// </summary>
/// <remarks>
/// <para>
/// The concrete failure this exists for: the Monobank receipt path carried the CP437 twins of
/// its Cyrillic strings. "Паливо" - what the acquirer prints on the fiscal receipt - had become
/// box-drawing characters, and so had the receipt's unit and the fuel names in the fixtures.
/// The cause was a native command's output piped into a PowerShell cmdlet on a machine whose
/// console code page is CP437: the bytes were decoded as CP437 and re-encoded as UTF-8, so the
/// damage was invisible in review - a diff full of box-drawing characters reads like a font
/// choice.
/// </para>
/// <para>
/// The fingerprint is the LEAD BYTE. A Cyrillic letter is two UTF-8 bytes, and the first of them
/// is 0xD0, 0xD1, 0xD2 or 0xD3. Read as CP437 those four bytes are the box-drawing characters
/// U+2568, U+2564, U+2565 and U+2559. So every run of mojibake contains at least one of them,
/// and a file that contains one of them is carrying mojibake. A correctly decoded file has no
/// reason to contain those four characters, and no file in this repository does - the check is
/// therefore exact, not a heuristic over the box-drawing block.
/// </para>
/// <para>
/// The repair is to read and write the file with a UTF-8 tool (an editor, or the file APIs)
/// and never to move a file's text through a shell pipeline: <c>git show &lt;ref&gt;:&lt;path&gt; |
/// Set-Content</c> is how this happened, and it destroys every em dash and every section sign
/// on the way.
/// </para>
/// </remarks>
public sealed class Cp437MojibakeGuardTests
{
    /// <summary>
    /// The CP437 rendering of the four UTF-8 lead bytes a Cyrillic letter can start with
    /// (0xD0, 0xD1, 0xD2, 0xD3). Written as escapes on purpose: the guard must not contain the
    /// characters it forbids.
    /// </summary>
    private static readonly char[] LeadByteCharacters = ['\u2568', '\u2564', '\u2565', '\u2559'];

    /// <summary>
    /// The files a defect of this kind can hide in: source, configuration, docs and the scripts
    /// the deploy runs. Generated output (bin, obj, dist, build) is not source and is skipped.
    /// </summary>
    private static readonly HashSet<string> SourceExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".cs", ".csproj", ".sln", ".props", ".targets", ".editorconfig",
        ".json", ".md", ".txt", ".xml", ".yml", ".yaml", ".toml", ".http",
        ".ts", ".tsx", ".js", ".jsx", ".mjs", ".cjs", ".css", ".html",
        ".sh", ".ps1", ".py", ".sql", ".env", ".example",
    };

    private static readonly HashSet<string> SkippedDirectories = new(StringComparer.OrdinalIgnoreCase)
    {
        "node_modules", ".git", "bin", "obj", "dist", "build", ".next", "coverage",
        "TestResults", "Pods", ".expo",
    };

    [Fact]
    public void The_Source_Should_Carry_No_Cp437_Mojibake()
    {
        var root = LocateRepositoryRoot();

        var scanned = 0;
        var offenders = new List<string>();

        foreach (var path in EnumerateSourceFiles(root))
        {
            scanned++;
            var lines = File.ReadAllText(path).Split('\n');
            for (var i = 0; i < lines.Length; i++)
            {
                if (!lines[i].AsSpan().ContainsAny(LeadByteCharacters)) continue;
                offenders.Add($"{Path.GetRelativePath(root, path).Replace('\\', '/')}:{i + 1}");
            }
        }

        scanned.Should().BeGreaterThan(
            0,
            "the repository's own sources should be visible to this test; an empty scan would "
            + "pass forever");

        offenders.Should().BeEmpty(
            "these lines carry the CP437 twin of a Cyrillic string: a UTF-8 file was read as "
            + "CP437 and written back as UTF-8, and every Cyrillic character in it became its "
            + "box-drawing twin. A fiscal receipt is one of the places this text lands - the "
            + "acquirer prints what we declare it - and a diff full of box-drawing characters "
            + "reads like a font choice, so the damage survives review. Repair it with a UTF-8 "
            + "tool (an editor, or the file APIs); never move a file's text through a shell "
            + "pipeline, because the console's code page is CP437 and the pipeline re-encodes it");
    }

    private static IEnumerable<string> EnumerateSourceFiles(string root)
    {
        foreach (var path in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(root, path);
            var segments = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (segments.Any(segment => SkippedDirectories.Contains(segment))) continue;
            if (!SourceExtensions.Contains(Path.GetExtension(path))) continue;
            yield return path;
        }
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
