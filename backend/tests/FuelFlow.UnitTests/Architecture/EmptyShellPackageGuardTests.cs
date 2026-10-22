using System.Runtime.CompilerServices;
using FluentAssertions;

namespace FuelFlow.UnitTests.Architecture;

/// <summary>
/// The validation and mapping packages stay gone: the API references neither, and no source
/// file reaches for either of them.
/// </summary>
/// <remarks>
/// <para>
/// The concrete failure this exists for: the API referenced <c>FluentValidation</c> and
/// <c>AutoMapper</c> and used neither. The validation layer was one validator in the whole
/// API (<c>CreateSupportMessageCommandValidator</c>) behind a global filter, and the mapping
/// layer was nothing at all - zero <c>CreateMap</c> calls, zero <c>using AutoMapper</c>. The
/// mapping in this codebase is explicit field copies, and they read well.
/// </para>
/// <para>
/// Two dependencies that promise a capability the code does not have are worse than one
/// dependency that does not: a reader who sees <c>AddValidatorsFromAssemblyContaining</c> in
/// the composition root assumes the commands are validated, and they are not. Both packages
/// are gone now, and the one rule they carried - the support form's fields - is a check the
/// caller can read next to the command it guards.
/// </para>
/// <para>
/// The rule is here so the shell does not come back in a later "we might need this" commit:
/// a package reference is cheap, and a package nobody uses is a promise nobody keeps.
/// </para>
/// </remarks>
public sealed class EmptyShellPackageGuardTests
{
    /// <summary>
    /// The packages that were referenced and unused. AutoMapper was never used at all;
    /// FluentValidation was used by exactly one validator, which is now an explicit check.
    /// </summary>
    private static readonly string[] BannedPackages =
    [
        "AutoMapper",
        "FluentValidation",
        "FluentValidation.DependencyInjectionExtensions",
    ];

    /// <summary>
    /// What the code would look like if the packages were back in use.
    /// </summary>
    private static readonly string[] BannedUsages =
    [
        "using AutoMapper",
        "using FluentValidation",
        "AbstractValidator<",
        "CreateMap<",
    ];

    [Fact]
    public void No_Project_Should_Reference_The_Empty_Shells()
    {
        var offenders = ProjectFiles()
            .SelectMany(project => BannedPackages
                .Where(package => MentionsPackageReference(project, package))
                .Select(package => $"{Relative(project)} references {package}"))
            .OrderBy(text => text, StringComparer.Ordinal)
            .ToArray();

        offenders.Should().BeEmpty(
            "a package that is referenced but unused is a promise the code does not keep - a reader "
            + "who sees the reference assumes the capability is there. AutoMapper was referenced "
            + "and never used; FluentValidation carried one validator and has become an explicit "
            + "check on the support form. Both are gone, and the packages come back only with the "
            + "code that uses them");
    }

    [Fact]
    public void No_Source_File_Should_Reach_For_The_Empty_Shells()
    {
        var offenders = SourceFiles()
            .Where(file => !file.EndsWith("EmptyShellPackageGuardTests.cs", StringComparison.Ordinal))
            .SelectMany(file =>
            {
                var text = File.ReadAllText(file);

                return BannedUsages
                    .Where(usage => text.Contains(usage, StringComparison.Ordinal))
                    .Select(usage => $"{Relative(file)} uses {usage}");
            })
            .OrderBy(text => text, StringComparer.Ordinal)
            .ToArray();

        offenders.Should().BeEmpty(
            "the guard above stops the reference from coming back, and this one stops the code "
            + "from using a package that is not referenced - which compiles nowhere and reads as "
            + "though the validation layer were back");
    }

    private static bool MentionsPackageReference(string projectFile, string package) =>
        File.ReadAllText(projectFile)
            .Contains($"Include=\"{package}\"", StringComparison.Ordinal);

    private static IEnumerable<string> ProjectFiles() =>
        Directory.EnumerateFiles(BackendDirectory(), "*.csproj", SearchOption.AllDirectories)
            .Where(path => !IsBuildOutput(path));

    private static IEnumerable<string> SourceFiles() =>
        Directory.EnumerateFiles(BackendDirectory(), "*.cs", SearchOption.AllDirectories)
            .Where(path => !IsBuildOutput(path));

    private static bool IsBuildOutput(string path) =>
        path.Replace('\\', '/').Contains("/bin/", StringComparison.Ordinal)
        || path.Replace('\\', '/').Contains("/obj/", StringComparison.Ordinal);

    private static string BackendDirectory() =>
        Path.Combine(LocateRepositoryRoot(), "backend");

    private static string Relative(string path) =>
        Path.GetRelativePath(LocateRepositoryRoot(), path).Replace('\\', '/');

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
