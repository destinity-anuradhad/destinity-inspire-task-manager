using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace TaskTracker.Web.Auth;

/// <summary>
/// Requires an API-key request to carry a specific scope. This is a no-op for interactive
/// (cookie) sessions, which are governed by their role/team authorization instead — so the same
/// endpoint serves both the app UI and scoped integration clients.
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = true)]
public sealed class RequireApiScopeAttribute(string scope) : Attribute, IAuthorizationFilter
{
    public string Scope { get; } = scope;

    public void OnAuthorization(AuthorizationFilterContext context)
    {
        var identity = context.HttpContext.User.Identity;
        if (identity?.AuthenticationType != ApiKeyDefaults.Scheme)
            return;   // not an API-key request → scope model does not apply

        var granted = context.HttpContext.User
            .FindAll(ApiKeyDefaults.ScopeClaim)
            .Any(c => string.Equals(c.Value, Scope, StringComparison.OrdinalIgnoreCase));

        if (!granted)
        {
            context.Result = new ObjectResult(new { error = $"This API key is missing the required '{Scope}' scope." })
            {
                StatusCode = StatusCodes.Status403Forbidden
            };
        }
    }
}
