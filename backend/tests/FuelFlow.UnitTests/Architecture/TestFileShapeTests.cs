using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace FuelFlow.UnitTests.Architecture;

/// <summary>
/// Every test file is a file: it has lines, and it declares a type the compiler can see.
/// </summary>
/// <remarks>
/// <para>
/// The concrete failure this exists for: four test files were written to disk with no newline in
/// them. A C# file with no newline is still a valid compilation unit - the whole thing parses as
/// one line - and the first <c>//</c> in it comments out everything after it. In each of those
/// files the comment marker came before the class, so the class, its <c>[Fact]</c>s and its
/// assertions were all inside a comment: the file compiled, the class did not exist, and the
/// tests did not run. The suite was green because nothing was missing from it.
/// </para>
/// <para>
/// Thirty-six tests were in those four files - the OKKO catalogue import's own tests, the pump
/// price sync's, the data-kind migration's and the margin migration's. A fifth file was created
/// empty and never had content. The files were flattened by the offer removal (2026-10-18/19),
/// when the tests still spoke a schema that no longer existed, and nobody noticed because the
/// compiler had nothing to complain about: a file that declares no type is not an error, it is
/// nothing.
/// </para>
/// <para>
/// So the check is on the SHAPE, not on the content: a file with no type in it is a broken file,
/// and a broken file is a build failure rather than a suite that quietly stopped testing
/// something. The same shape as the other guards here - a rule nobody checks is a rule one
/// afternoon of "just one more refactor" erases.
/// </para>
/// </remarks>
public sealed class TestFileShapeTests
{
    /// <summary>
    /// A type declaration at the start of a line: modifiers, then the keyword, then a name.
    /// Anchored, so a doc comment that happens to mention a class is not a declaration.
    /// </summary>
    private static readonly Regex TypeDeclaration = new(
        @"^\s*(?:(?:public|internal|private|protected|sealed|abstract|static|partial|readonly|record)\s+)*(?:class|record|struct|interface|enum)\s+[A-Za-z_]",
        RegexOptions.Compiled | RegexOptions.Multiline);

    [Fact]
    public void Every_Test_File_Has_Lines_And_Declares_A_Type_The_Compiler_Can_See()
    {
        var testsRoot = LocateTestsRoot();

        var files = Directory
            .EnumerateFiles(testsRoot, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Replace('\\', '/').Contains("/bin/"))
            .Where(path => !path.Replace('\\', '/').Contains("/obj/"))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToList();

        files.Should().NotBeEmpty(
            "the test tree should be visible to this test; an empty scan would pass forever");

        var offenders = files
            .Select(Offender)
            .Where(reason => reason is not null)
            .Select(reason => reason!)
            .ToArray();

        offenders.Should().BeEmpty(
            "a test file with no type in it is a file whose tests do not exist - the compiler has " +
            "nothing to complain about, so the suite stays green while it stops testing something. " +
            "A file with no newline is how that happens: the first // comments out the rest of it, " +
            "class and [Fact]s included (planning #388)");
    }

    /// <summary>
    /// Why the file is broken, or null when it is fine.
    /// </summary>
    private static string? Offender(string path)
    {
        var relative = Path.GetRelativePath(LocateTestsRoot(), path).Replace('\\', '/');
        var text = File.ReadAllText(path);

        if (!text.Contains('\n'))
        {
            return $"{relative}: the whole file is one line - the first // comments out the rest " +
                   "of it, so the class and every test in it are inside a comment";
        }

        // What the compiler sees: the comments are gone, and what is left is the file.
        var code = StripComments(text);

        if (!TypeDeclaration.IsMatch(code))
        {
            return $"{relative}: the file declares no type - everything in it is inside a comment, " +
                   "so it compiles to nothing and the tests it was written for do not exist";
        }

        return null;
    }

    /// <summary>
    /// Removes the comments, which is what makes "declares a type" mean what it says: a type
    /// behind a <c>//</c> is not a type. A doc comment counts - it is a line comment too, and a
    /// one-line file's first <c>///</c> is exactly what kills the class below it.
    /// </summary>
    private static string StripComments(string text)
    {
        var withoutBlock = Regex.Replace(text, @"/\*.*?\*/", " ", RegexOptions.Singleline);
        return Regex.Replace(withoutBlock, @"//[^\n]*", " ");
    }

    /// <summary>
    /// Resolves backend/tests from this test file's compile-time location, with a fallback that
    /// walks up from the test assembly's directory.
    /// </summary>
    private static string LocateTestsRoot([CallerFilePath] string thisFile = "")
    {
        // thisFile = .../backend/tests/FuelFlow.UnitTests/Architecture/<this>.cs
        // Up 2: FuelFlow.UnitTests -> tests
        var tests = Directory.GetParent(thisFile)!.Parent!.Parent!.FullName;
        if (Directory.Exists(tests)) return tests;

        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var probe = Path.Combine(dir.FullName, "tests");
            if (Directory.Exists(probe)) return probe;
            probe = Path.Combine(dir.FullName, "backend", "tests");
            if (Directory.Exists(probe)) return probe;
            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException(
            "Could not locate backend/tests from either the test file path or the assembly directory.");
    }
}
