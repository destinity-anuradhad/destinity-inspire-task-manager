using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace TaskTracker.Web.Auth;

/// <summary>
/// Validates the antiforgery token for unsafe requests <b>only when</b> the caller is an interactive
/// cookie session. API-key requests authenticate with an explicit header (not an ambient cookie), so
/// they are not exposed to CSRF and are intentionally exempt. Pair with
/// <c>[IgnoreAntiforgeryToken]</c> at the controller to disable the app-wide auto-validation, letting
/// this filter apply the conditional rule instead.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false)]
public sealed class ApiAntiforgeryAttribute : Attribute, IAsyncAuthorizationFilter
{
    private static readonly HashSet<string> UnsafeMethods =
        new(StringComparer.OrdinalIgnoreCase) { "POST", "PUT", "PATCH", "DELETE" };

    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        var request = context.HttpContext.Request;
        if (!UnsafeMethods.Contains(request.Method))
            return;

        // Only ambient cookie sessions need CSRF protection.
        if (context.HttpContext.User.Identity?.AuthenticationType != CookieAuthenticationDefaults.AuthenticationScheme)
            return;

        var antiforgery = context.HttpContext.RequestServices.GetRequiredService<IAntiforgery>();
        try
        {
            await antiforgery.ValidateRequestAsync(context.HttpContext);
        }
        catch (AntiforgeryValidationException)
        {
            context.Result = new BadRequestObjectResult(new { error = "Antiforgery token validation failed." });
        }
    }
}
