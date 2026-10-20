using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using FluentAssertions;

namespace FuelFlow.UnitTests.Architecture;

/// <summary>
/// Architecture guard for the cost of the <c>/health</c> probe.
///
/// The probe asked "is the database reachable?" the expensive way: it called
/// <c>GetPendingMigrationsAsync()</c>, which reads the WHOLE <c>__EFMigrationsHistory</c> table
/// (166 rows when this was written) and diffs it against the model snapshot - and then threw the
/// result away, so a pending migration never made the probe unhealthy anyway. The probe is hit by
/// UptimeRobot, by the CI health poll and by whatever the load balancer does, and the answer does
/// not change between probes.
///
/// The guard keeps the probe a single trivial statement: it still asks the database something, and
/// it does not read the migration history to do it.
/// </summary>
public sealed class HealthProbeGuardTests
{
    [Fact]
    public void The_Health_Probe_Should_Not_Read_The_Migration_History()
    {
        var source = WithoutComments(ReadPipelineSetup());

        source.Should().NotContain("GetPendingMigrationsAsync",
            "the probe asks whether the database is reachable, not which migrations are pending - " +
            "reading the whole history on every probe is the cost this guard exists to prevent");

        source.Should().Contain("SELECT 1",
            "the probe must still ask the database something, or a broken connection reads as healthy");
    }

    /// <summary>
    /// The comments are stripped first: this file's own comment explains the change by naming the
    /// method it replaced, and a guard that read the comment would forbid the explanation.
    /// </summary>
    private static string WithoutComments(string source)
    {
        var withoutBlockComments = Regex.Replace(source, @"/\*.*?\*/", " ", RegexOptions.Singleline);
        return Regex.Replace(withoutBlockComments, @"//[^\n]*", " ");
    }

    private static string ReadPipelineSetup([CallerFilePath] string thisFile = "")
    {
        // thisFile = .../backend/tests/FuelFlow.UnitTests/Architecture/<this>.cs
        var backend = Directory.GetParent(thisFile)!.Parent!.Parent!.Parent!.FullName;
        var path = Path.Combine(backend, "src", "FuelFlow.API", "Extensions", "PipelineSetup.cs");
        if (File.Exists(path)) return File.ReadAllText(path);

        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "src", "FuelFlow.API", "Extensions", "PipelineSetup.cs");
            if (File.Exists(candidate)) return File.ReadAllText(candidate);
            dir = dir.Parent;
        }

        throw new FileNotFoundException("PipelineSetup.cs was not found from the test assembly.");
    }
}
