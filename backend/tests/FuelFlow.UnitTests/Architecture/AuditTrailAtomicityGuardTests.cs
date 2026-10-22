using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using FluentAssertions;

namespace FuelFlow.UnitTests.Architecture;

/// <summary>
/// The audit row and the change it describes commit together, or neither does (planning #533).
///
/// <para>
/// The concrete failure this exists for: <c>AuditEventService</c> called its own
/// <c>SaveChangesAsync</c>, and every caller saved its business change first. The two writes were
/// two transactions, so a failure between them left a committed change with no row explaining it -
/// on a platform that moves money and issues bearer instruments, that is an audit-integrity gap and
/// not a cosmetic one. The opposite failure mattered less often and mattered just as much: a row
/// for a change that was rolled back.
/// </para>
///
/// <para>
/// The service no longer saves; it tracks the row in the caller's context and the caller's own
/// save commits it. Two rules keep that true, and both are checked here rather than in review:
/// the service must never commit on its own, and a file that records a row must also save - a
/// caller that records and forgets would lose the row silently, which is the same defect wearing
/// the other hat.
/// </para>
///
/// <para>
/// The rule is per FILE, so a caller whose save lives in another file passes this check. That is
/// the known blind spot, and it is the same one <c>NoTrackingMutationGuardTests</c> documents: the
/// per-call ordering needs data-flow analysis. The integration test
/// (<c>AuditTrailAtomicityIntegrationTests</c>) is what closes the gap in practice - it proves the
/// transaction against a real PostgreSQL.
/// </para>
/// </summary>
public sealed class AuditTrailAtomicityGuardTests
{
    [Fact]
    public void The_Audit_Service_Should_Never_Commit_On_Its_Own()
    {
        var service = Path.Combine(LocateApiProjectRoot(), "SharedKernel", "Observability", "AuditEventService.cs");

        File.Exists(service).Should().BeTrue("the audit service is the file this rule is about");

        var code = StripComments(File.ReadAllText(service));

        code.Should().NotContain("SaveChanges",
            "the audit service used to call its own SaveChangesAsync, and every caller saved its "
            + "business change first - so the row and the change it describes were two transactions, "
            + "and a failure between them left a committed change with nothing explaining it. The "
            + "service tracks the row; the caller's save commits it (planning #533)");
    }

    [Fact]
    public void Every_File_That_Records_An_Audit_Row_Should_Also_Save()
    {
        var apiRoot = LocateApiProjectRoot();

        var offenders = Directory
            .EnumerateFiles(apiRoot, "*.cs", SearchOption.AllDirectories)
            .Where(IsInScope)
            .Select(path => (path, code: StripComments(File.ReadAllText(path))))
            .Where(f => f.code.Contains(".RecordEvent("))
            .Where(f => !f.code.Contains("SaveChangesAsync("))
            .Select(f => Path.GetRelativePath(apiRoot, f.path).Replace('\\', '/'))
            .OrderBy(p => p)
            .ToList();

        offenders.Should().BeEmpty(
            "a file that records an audit row but never saves would lose the row silently - the "
            + "change would be committed and the trail would have a hole in it, which is the same "
            + "defect the service's own save used to cause (planning #533). If the save really "
            + "belongs to another file, say so in a comment the reviewer can read");
    }

    /// <summary>
    /// Removes // and /* */ comments before scanning: prose that happens to contain "SaveChanges"
    /// or ".RecordEvent(" is not a call site.
    /// </summary>
    private static string StripComments(string text)
    {
        var withoutBlock = Regex.Replace(text, @"/\*.*?\*/", " ", RegexOptions.Singleline);
        return Regex.Replace(withoutBlock, @"//[^\n]*", " ");
    }

    private static bool IsInScope(string path)
    {
        var normalized = path.Replace('\\', '/');
        if (normalized.Contains("/Migrations/")) return false;
        if (normalized.Contains("/bin/") || normalized.Contains("/obj/")) return false;
        return true;
    }

    /// <summary>
    /// Resolves backend/src/FuelFlow.API from this test file's compile-time location, with a
    /// fallback that walks up from the test assembly's directory.
    /// </summary>
    private static string LocateApiProjectRoot([CallerFilePath] string thisFile = "")
    {
        // thisFile = .../backend/tests/FuelFlow.UnitTests/Architecture/<this>.cs
        // Up 3: FuelFlow.UnitTests -> tests -> backend
        var backend = Directory.GetParent(thisFile)!.Parent!.Parent!.Parent!.FullName;
        var candidate = Path.Combine(backend, "src", "FuelFlow.API");
        if (Directory.Exists(candidate)) return candidate;

        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var probe = Path.Combine(dir.FullName, "src", "FuelFlow.API");
            if (Directory.Exists(probe)) return probe;
            probe = Path.Combine(dir.FullName, "backend", "src", "FuelFlow.API");
            if (Directory.Exists(probe)) return probe;
            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException(
            "Could not locate backend/src/FuelFlow.API from either the test file path or the assembly directory.");
    }
}
