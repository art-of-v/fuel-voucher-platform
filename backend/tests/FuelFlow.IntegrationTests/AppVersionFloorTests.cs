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
/// full-screen update wall instead of the app. The value therefore has to satisfy two opposing
/// requirements at once: high enough that a stale build cannot call an endpoint the cleanup work
/// deleted, and never higher than the version actually shipping in the stores - because a user sent
/// to a store that does not yet serve the demanded version finds the build they already had, and is
/// stuck behind a wall nothing can clear.
/// </para>
///
/// <para>
/// The second requirement is the one that has to be checked mechanically, and it is checked here.
/// On 2026-10-12 the floor was <c>1.1.0</c> while the stores still served <c>1.0.1</c>: the API
/// auto-deploys on merge, so production served the wall to every user, pointing them at a store
/// build that did not exist. It was undone, not designed around - so the check below is now the
/// thing standing between a bumped <c>app.json</c> and a locked-out app.
/// </para>
///
/// <para>
/// The opposite requirement - that the floor is <em>above</em> the published build - cannot be
/// checked from this repository, because the repository does not know what the stores serve. That
/// direction is a deliberate decision per release (see <see cref="PublishedOnAppStore"/>), not an
/// invariant, so there is no test for it here.
/// </para>
/// </summary>
public class AppVersionFloorTests
{
    /// <summary>
    /// The marketing version the stores actually serve right now. Bumping <c>mobile/app.json</c> does
    /// not change this - only a submitted build does - and forgetting that is what put a
    /// non-existent version behind the wall on 2026-10-12. Update it in the same PR that ships the
    /// build, never the one that prepares it.
    /// </summary>
    /// <remarks>
    /// 1.1.0 reached the public <b>iOS</b> App Store on 2026-10-16, which is what made the floor 1.1.0
    /// safe. Android is a different story and this constant cannot express it: no Android build has
    /// ever been produced from this tree and none is on Play, so a floor of 1.1.0 walls every Android
    /// build below it and points it at a listing that cannot serve the version. That is the lockout
    /// this file exists to prevent - it just cannot happen through Play, because there is nothing on
    /// Play. It is currently clear, and the place to re-check it is not this constant but the fleet:
    /// every sub-1.1.0 device on 2026-10-16 was iOS (<c>SELECT app_version, device_model FROM devices
    /// WHERE app_version &lt;&gt; '1.1.0'</c>). Re-run it before raising the floor again.
    /// </remarks>
    /// <summary>
    /// The version each store actually serves, per store, and <c>null</c> for a store nothing has
    /// ever been published to.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This used to be one constant called <c>PublishedInStores</c> reading <c>"1.1.0"</c>. That was
    /// a claim about <em>both</em> stores, and it was true of exactly one: iOS shipped 1.1.0 on
    /// 2026-10-16, while no Android build has ever been published to Play at all. A single constant
    /// cannot say "1.1.0" and "never published" at the same time, so it said neither — it claimed
    /// 1.1.0 everywhere, and the check built on it guarded a store that has nothing.
    /// </para>
    /// <para>
    /// The hazard it was supposed to catch is real and narrow: if an Android build <em>older</em> than
    /// the floor is ever published, every Android user on it hits the forced-update wall and cannot
    /// use the app. That is #291's incident, and with Play empty it cannot happen yet.
    /// </para>
    /// <para>
    /// <b>Why Play is still a constant and not an observation.</b> Apple publishes a free, unauthenticated
    /// lookup endpoint that returns the live version, so <see cref="AppVersionFloorLiveTests"/> can
    /// watch iOS for real. Google has no equivalent, and reaching the Play Console needs credentials
    /// this repository deliberately does not hold. So the honest thing for Play is to make "nothing
    /// published" <em>expressible</em> — which it is not while the value is a non-null string — and to
    /// catch the real risk from the fleet instead, which
    /// <see cref="AppVersionFloorLiveTests.Floor_DoesNotWallAnyDeviceActuallyInUse"/> does.
    /// </para>
    /// <para>
    /// Bump a store's entry in the same PR that submits the build, never the one that prepares it.
    /// </para>
    /// </remarks>
    private const string? PublishedOnAppStore = "1.1.0";

    /// <summary>
    /// Nothing has ever been published to Google Play from this tree, and that is a fact rather than
    /// an omission. <c>null</c> means "this store has no users, so it cannot constrain the floor".
    /// </summary>
    private const string? PublishedOnPlayStore = null;

    /// <summary>
    /// Every store that has something published, which is what the floor has to respect.
    /// </summary>
    private static readonly (string Store, string? Version)[] Stores =
    {
        ("App Store", PublishedOnAppStore),
        ("Google Play", PublishedOnPlayStore),
    };

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
    internal static int Compare(string left, string right)
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

    internal static string ConfiguredFloor()
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
    /// The same direction, against the version that is genuinely live in the stores. This is the
    /// check that was missing when the floor went to 1.1.0: <c>app.json</c> agreed with it, so the
    /// check above passed while every real user was locked out.
    /// </summary>
    [Fact]
    public void Floor_DoesNotExcludeTheVersionLiveInAnyStoreThatHasOne()
    {
        var floor = ConfiguredFloor();

        foreach (var (store, published) in Stores.Where(s => s.Version is not null))
        {
            Compare(published!, floor).Should().BeGreaterThanOrEqualTo(0,
                $"AppVersion:MinimumVersion demands {floor} but {store} serves {published}: every " +
                "user on the installed build would hit the forced-update wall and be sent to a " +
                "store that cannot give them that version");
        }
    }

    /// <summary>
    /// A store with nothing published constrains nothing, and that is the whole reason the value is
    /// nullable rather than an empty string or a sentinel. Asserted rather than assumed, because the
    /// failure mode is silent: with Play absent from the list, the floor is genuinely unconstrained
    /// there, and a reader who does not know that could assume the floor is being checked against
    /// Play when it is not.
    /// </summary>
    [Fact]
    public void AStoreWithNothingPublishedIsExcludedRatherThanTreatedAsZero()
    {
        Stores.Should().Contain(s => s.Version == null,
            "Google Play has nothing published, and pretending otherwise would either break the " +
            "comparison or, worse, silently constrain the floor to an empty string");

        foreach (var (store, published) in Stores.Where(s => s.Version != null))
        {
            (Version.TryParse(published, out _) ? published! : "unparseable").Should().NotBe("unparseable",
                $"{store} is recorded as serving {published}, which is not a version, so every " +
                "comparison below would be comparing noise");
        }
    }

    /// <summary>
    /// Keeps the published constant honest. It is a hand-maintained claim about the App Store, so
    /// the cheapest thing to do when it goes stale is bump <c>app.json</c> and never touch it - and
    /// then the check above quietly guards nothing. A store build that is newer than what we recorded
    /// means the recording is what is out of date, not the app.
    /// </summary>
    [Fact]
    public void PublishedVersion_IsNotAheadOfAppJson()
    {
        var shipped = ShippedVersion();

        foreach (var (store, published) in Stores.Where(s => s.Version is not null))
        {
            Compare(published!, shipped).Should().BeLessThanOrEqualTo(0,
                $"{store} is recorded as serving {published} but app.json already ships " +
                $"{shipped}: record the version in the stores only once a build carrying it is submitted");
        }
    }
}