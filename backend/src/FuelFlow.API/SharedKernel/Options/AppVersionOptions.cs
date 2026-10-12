namespace FuelFlow.SharedKernel.Options;

public sealed class AppVersionOptions
{
    public const string SectionName = "AppVersion";

    /// <summary>
    /// Hard floor, not a suggestion. <c>mobile/app/_layout.tsx</c> reads <c>/api/app-version</c> on
    /// launch and, when the running build is below this, returns a full-screen update wall instead
    /// of the app - so an old build is locked out entirely, not merely warned.
    /// </summary>
    /// <remarks>
    /// Raise it only AFTER the matching version is live in both stores. The floor has two
    /// requirements pulling against each other: high enough that a stale build cannot call an
    /// endpoint that has been deleted, and never higher than what is actually shipping, or a user is
    /// sent to a store that still serves them the build they already have and cannot get past the
    /// wall. <c>AppVersionFloorTests</c> checks both directions against <c>mobile/app.json</c>.
    /// This default only applies when configuration is absent; the real value lives in
    /// appsettings.json.
    /// </remarks>
    public string MinimumVersion { get; set; } = "1.1.0";

    public string IosStoreUrl { get; set; } = string.Empty;
    public string AndroidStoreUrl { get; set; } = string.Empty;
}
