namespace ElectCrm.Presentation.Middleware;

using ElectCrm.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;

/// <summary>
/// Intercepts every authenticated request and redirects to the change-password page
/// if the user's <see cref="ApplicationUser.RequirePasswordChange"/> flag is set.
///
/// Runs after UseAuthentication / UseAuthorization so the user's identity is resolved.
/// Blazor's long-lived circuit bypasses per-request middleware after circuit establishment;
/// the companion check in MainLayout.razor OnInitializedAsync covers that path.
///
/// PERFORMANCE_NOTE — this adds one DB query per request for authenticated users with
/// RequirePasswordChange=true; once cleared it no longer fires
/// (RequirePasswordChange=false users skip after first check).
/// </summary>
public sealed class RequirePasswordChangeMiddleware : IMiddleware
{
    private readonly UserManager<ApplicationUser> _userManager;

    public RequirePasswordChangeMiddleware(UserManager<ApplicationUser> userManager)
    {
        _userManager = userManager;
    }

    public async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        // Pass through: unauthenticated requests.
        if (context.User.Identity?.IsAuthenticated != true)
        {
            await next(context);
            return;
        }

        var path = context.Request.Path.Value ?? string.Empty;

        // Pass through: allow-listed paths (change-password page, logout, static assets, Blazor internals).
        if (IsAllowListed(path))
        {
            await next(context);
            return;
        }

        // Check RequirePasswordChange flag.
        var appUser = await _userManager.GetUserAsync(context.User);
        if (appUser?.RequirePasswordChange == true)
        {
            context.Response.Redirect("/profile/change-password?required=true");
            return; // short-circuit — do NOT call next
        }

        await next(context);
    }

    private static bool IsAllowListed(string path) =>
        path.StartsWith("/profile/change-password", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("/account/logout", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("/_blazor", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("/_framework", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("/_content", StringComparison.OrdinalIgnoreCase) ||
        IsStaticFile(path);

    private static bool IsStaticFile(string path)
    {
        var ext = System.IO.Path.GetExtension(path);
        return ext is ".css" or ".js" or ".png" or ".ico" or ".woff" or ".woff2"
            or ".jpg" or ".jpeg" or ".gif" or ".svg" or ".map" or ".webp";
    }
}
