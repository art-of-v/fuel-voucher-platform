namespace FuelFlow.SharedKernel.Options;

public sealed class AppVersionOptions
{
    public const string SectionName = "AppVersion";

    /// <summary>
    /// The floor used when configuration is absent. Kept as a constant because it is also the
    /// fallback the endpoint serves - two literals in two places drift apart silently, and the
    /// drifted copy is the one nobody reads.
    /// </summary>
    /// <remarks>
    /// Bumped to 1.1.0 on 2026-10-16, once 1.1.0 was live in the App Store. It tracks the real floor,
    /// not the one the project would like: a fallback below the configured floor is a silent
    /// downgrade for any deployment that drops the <c>AppVersion</c> section.
    /// </remarks>
    public const string DefaultMinimumVersion = "1.1.0";

    /// <summary>
    /// Hard floor, not a suggestion. <c>mobile/app/_layout.tsx</c> reads <c>/api/app-version</c> on
    /// launch and, when the running build is below this, returns a full-screen update wall instead
    /// of the app - so an old build is locked out entirely, not merely warned.
    /// </summary>
    /// <remarks>
    /// The floor must never exceed the version the stores actually serve. A user below it opens the
    /// app, gets the wall, and is sent to a store that still serves them the build they already
    /// have - which is the same build, so the wall never clears. That is not a nag loop, it is a
    /// bricked app.
    ///
    /// <para>
    /// 2026-10-12: this was <c>1.1.0</c> for one deployment. <c>mobile/app.json</c> says 1.1.0,
    /// but 1.1.0 had not been submitted to either store - the stores still served 1.0.1 - and the
    /// API auto-deploys on merge. Every user on 1.0.1 was behind the wall with no way past it.
    /// Bumping <c>app.json</c> is <em>not</em> shipping a version: only a live store build counts,
    /// and only the store build raises what this may safely be set to.
    /// </para>
    ///
    /// <para>
    /// The other direction - high enough that a stale build cannot call an endpoint that has been
    /// deleted - is a real cost of parking it here, and is why the floor has to trail a shipped build
    /// rather than lead it. <c>AppVersionFloorTests</c> checks the locking-out direction against both
    /// <c>mobile/app.json</c> and the version recorded as published in the stores.
    /// </para>
    ///
    /// <para>
    /// This default only applies when configuration is absent; the real value lives in
    /// appsettings.json.
    /// </para>
    /// </remarks>
    public string MinimumVersion { get; set; } = DefaultMinimumVersion;

    public string IosStoreUrl { get; set; } = string.Empty;
    public string AndroidStoreUrl { get; set; } = string.Empty;

    /// <summary>
    /// Compares two marketing versions the way the release rules read them: <c>1.1</c> and
    /// <c>1.1.0</c> are the same version, because the header a client sends and the floor a
    /// deployment configures may spell the same number differently. Returns <c>null</c> when either
    /// side does not parse, so each caller decides what an unknown version means - the attestation
    /// rollout does not enforce it, and the update floor does not refuse it.
    /// </summary>
    public static int? Compare(string? left, string? right)
    {
        if (!Version.TryParse(left, out var leftVersion)
            || !Version.TryParse(right, out var rightVersion))
        {
            return null;
        }

        return Normalize(leftVersion).CompareTo(Normalize(rightVersion));
    }

    private static Version Normalize(Version version) => new(
        version.Major,
        version.Minor,
        Math.Max(version.Build, 0),
        Math.Max(version.Revision, 0));
}
