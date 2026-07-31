using Hangfire.Dashboard;

namespace FuelFlow.Middleware;

public sealed class HangfireDashboardAuthorizationFilter : IDashboardAuthorizationFilter
{
    private readonly bool _allowDevelopmentBypass;

    public HangfireDashboardAuthorizationFilter(bool allowDevelopmentBypass)
    {
        _allowDevelopmentBypass = allowDevelopmentBypass;
    }

    public bool Authorize(DashboardContext context)
    {
        if (_allowDevelopmentBypass)
            return true;

        var user = context.GetHttpContext().User;
        return user.Identity?.IsAuthenticated == true
            && user.IsInRole("Admin");
    }
}
