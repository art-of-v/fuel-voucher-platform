using Hangfire;
using Hangfire.Dashboard;

namespace FuelFlow.Middleware;

/// <summary>
/// The options the job dashboard is mounted with, in one place a test can read.
/// </summary>
/// <remarks>
/// <para>
/// The dashboard can requeue, trigger and delete jobs, so it is mounted behind the Admin-role filter
/// in <see cref="HangfireDashboardAuthorizationFilter"/> and behind Caddy's 404 at the edge; it is
/// reached through an SSH tunnel. The filter is the authentication half. This class is the other
/// half: Hangfire validates a request token on every non-GET request, and the only guard that would
/// stop a cross-site request from an already-authenticated Admin is that validation.
/// </para>
/// <para>
/// It used to be switched off (<c>IgnoreAntiforgeryToken = true</c>) as a documented Hangfire escape
/// hatch for dashboards served from another origin. This one is not: the dashboard is a same-origin
/// page reached through a tunnel, and its own forms carry the token. A <c>true</c> in the composition
/// root was a decision nobody had made, and it is the kind that is invisible until it matters.
/// </para>
/// </remarks>
internal static class HangfireDashboardSetup
{
    /// <param name="allowDevelopmentBypass">
    /// <c>Auth:DevBypass</c> - true only in the Development configuration, where the local dashboard
    /// is opened without a signed-in Admin. The token is validated either way.
    /// </param>
    internal static DashboardOptions Options(bool allowDevelopmentBypass) => new()
    {
        Authorization = new IDashboardAuthorizationFilter[]
        {
            new HangfireDashboardAuthorizationFilter(allowDevelopmentBypass)
        },
        IgnoreAntiforgeryToken = false
    };
}
