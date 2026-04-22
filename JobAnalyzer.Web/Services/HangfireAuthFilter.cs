using Hangfire.Dashboard;

namespace JobAnalyzer.Web.Services
{
    /// <summary>
    /// Hangfire Dashboard'a sadece Admin rolündeki kullanıcılar erişebilir.
    /// </summary>
    public class HangfireAuthFilter : IDashboardAuthorizationFilter
    {
        public bool Authorize(DashboardContext context)
        {
            var httpContext = context.GetHttpContext();
            return httpContext.User.Identity?.IsAuthenticated == true
                && httpContext.User.IsInRole("Admin");
        }
    }
}
