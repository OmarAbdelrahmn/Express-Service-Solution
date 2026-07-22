using Hangfire.Dashboard;

namespace Express_Service;

public sealed class HangfireDashboardAuthorizationFilter : IDashboardAuthorizationFilter
{
    public bool Authorize(DashboardContext context)
    {
        var user = context.GetHttpContext().User;

        return user.Identity?.IsAuthenticated == true &&
               (user.IsInRole("Master") || user.IsInRole("Admin"));
    }
}
