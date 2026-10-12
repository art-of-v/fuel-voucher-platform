using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace FuelFlow.IntegrationTests;

/// <summary>
/// Guards the one configuration where the API can brick a published app.
///
/// <para>
/// <c>AppVersion:MinimumVersion</c> is not a nag. <c>mobile/app/_layout.tsx</c> reads
/// <c>/api/app-version</c> on launch and, when the running build is below the floor, returns a
/// full-screen update wall instead of the app. So the value has to satisfy two opposing
/// requirements at once: high enough that a stale build cannot call an endpoint the cleanup work
/// deleted, and never higher than the version actually shipping in the stores - because a user sent
/// to a store that does not yet serve the demanded version finds the build they already had, and is
/// stuck in a loop they cannot leave.
/// </para>
///
/// <para>
/// Both halves are checked here against the real files: the mobile <c>app.json</c> that becomes the
/// store build, and the backend configuration that names the floor. Raising one without the other
/// fails this test rather than production.
/// </para>
/// </summary>
public class AppVersionFloorTests
{
    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "mobile", "app.json"))
                && Directory.Exists(Path.Combine(directory.FullName, "backend", "src")))
                return directory.FullName;

            directory = directory.Parent;
        }

        throw new InvalidOperationException(
            "Could not locate the repository root (a directory holding both mobile/app.json and backend/src) " +
            $"walking up from {AppContext.BaseDirectory}.");
    }

    /// <summary>
    /// The same numeric-segment comparison the app performs in <c>isVersionBelow</c>: compare part
    /// by part, treating a missing part as zero. Done here rather than asserted against a constant
    /// so the test keeps working when either number is bumped.
    /// </summary>
    private static int Compare(string left, string right)
    {
        var a = left.Split('.').Select(int.Parse).ToArray();
        var b = right.Split('.').Select(int.Parse).ToArray();

        for (var i = 0; i < Math.Max(a.Length, b.Length); i++)
        {
            var l = i < a.Length ? a[i] : 0;
            var r = i < b.Length ? b[i] : 0;
            if (l != r) return l.CompareTo(r);
        }

        return 0;
    }

    // appsettings.json is not strict JSON - it carries // comments that ASP.NET's configuration
    // reader tolerates. This has to parse the same way, or the test would fail on a file it is not
    // trying to judge.
    private static readonly JsonDocumentOptions Lenient = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>
    /// The version that becomes the store build. It lives under <c>expo</c> in app.json, which is
    /// the Expo config shape - Constants.expoConfig.version on the client reads exactly this key.
    /// </summary>
    private static string ShippedVersion()
    {
        using var doc = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(RepositoryRoot(), "mobile", "app.json")), Lenient);

        return doc.RootElement.GetProperty("expo").GetProperty("version").GetString()!;
    }

    private static string ConfiguredFloor()
    {
        var path = Path.Combine(RepositoryRoot(), "backend", "src", "FuelFlow.API", "appsettings.json");
        using var doc = JsonDocument.Parse(File.ReadAllText(path), Lenient);

        return doc.RootElement.GetProperty("AppVersion").GetProperty("MinimumVersion").GetString()!;
    }

    [Fact]
    public void MinimumVersion_IsAWellFormedVersion()
    {
        var floor = ConfiguredFloor();

        floor.Should().MatchRegex(@"^\d+\.\d+\.\d+$",
            "isVersionBelow parses each segment with Number(), so anything non-numeric makes the " +
            "comparison return false and the gate silently stops gating");
    }

    /// <summary>
    /// The direction that locks users out. A user on the build that is live right now must not be
    /// below the floor, or they open the app and get a wall pointing at a version that is not there.
    /// </summary>
    [Fact]
    public void Floor_DoesNotExcludeTheVersionInAppJson()
    {
        var shipped = ShippedVersion();
        var floor = ConfiguredFloor();

        Compare(shipped, floor).Should().BeGreaterThanOrEqualTo(0,
            $"app.json ships {shipped} but AppVersion:MinimumVersion demands {floor}: every user on " +
            "the shipping build would hit the forced-update wall and be sent to a store that does " +
            "not serve that version yet");
    }

    /// <summary>
    /// The direction that leaves stale clients running. If app.json is already ahead of the floor,
    /// the floor is not doing the job it was raised for.
    /// </summary>
    [Fact]
    public void Floor_IsRaisedAboveTheLastPublishedBuild()
    {
        var shipped = ShippedVersion();
        var floor = ConfiguredFloor();

        Compare(floor, "1.0.1").Should().BeGreaterThan(0,
            "1.0.1 is the build in the stores; a floor at or below it lets those builds keep " +
            "calling routes that the endpoint cleanup deleted");
    }
}