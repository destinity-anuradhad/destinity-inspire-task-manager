using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using TaskTracker.Services;

namespace TaskTracker.Web.Auth;

/// <summary>Constants for the API-key authentication scheme.</summary>
public static class ApiKeyDefaults
{
    public const string Scheme        = "ApiKey";
    public const string HeaderName    = "X-Api-Key";
    public const string ClientIdClaim = "ApiClientId";
    public const string ScopeClaim    = "scope";
    /// <summary>Present ("true") when the key may only see and change its owner's own tasks.</summary>
    public const string OwnTasksOnlyClaim = "api_own_tasks_only";
}

/// <summary>The v1 API's rate limit ("api" policy), also published in response headers.</summary>
public static class ApiRateLimits
{
    public const int PermitLimit   = 120;
    public const int WindowSeconds = 60;
}

/// <summary>
/// Authenticates requests presenting an API key via the <c>X-Api-Key</c> header (or
/// <c>Authorization: Bearer &lt;key&gt;</c>). A valid key is mapped to its owner user, scoped team,
/// team role, and granted scopes so downstream authorization behaves like the interactive session
/// that minted it. Absent header → <see cref="AuthenticateResult.NoResult"/> so cookie auth can run.
/// </summary>
public sealed class ApiKeyAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    IApiClientService clients)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var presented = ExtractKey();
        if (string.IsNullOrEmpty(presented))
            return AuthenticateResult.NoResult();   // let other schemes (cookie) try

        var ip = Context.Connection.RemoteIpAddress?.ToString();
        var principal = await clients.AuthenticateAsync(presented, ip, Context.RequestAborted);
        if (principal is null)
            return AuthenticateResult.Fail("Invalid, expired, or revoked API key.");

        var identity = new ClaimsIdentity(ApiKeyDefaults.Scheme, ClaimTypes.Name, ClaimTypes.Role);
        identity.AddClaim(new Claim(ClaimTypes.Name, principal.Username));
        identity.AddClaim(new Claim(ClaimTypes.Role, principal.Role));
        identity.AddClaim(new Claim(AuthClaims.UserId, principal.UserId.ToString()));
        identity.AddClaim(new Claim("DisplayName", principal.DisplayName));
        identity.AddClaim(new Claim(ApiKeyDefaults.ClientIdClaim, principal.ClientId.ToString()));

        if (principal.TeamId is int teamId)
        {
            identity.AddClaim(new Claim(AuthClaims.ActiveTeamId, teamId.ToString()));
            identity.AddClaim(new Claim("ActiveTeamName", principal.TeamName));
            identity.AddClaim(new Claim(AuthClaims.TeamRole, principal.TeamRole));
        }

        foreach (var scope in principal.Scopes)
            identity.AddClaim(new Claim(ApiKeyDefaults.ScopeClaim, scope));
        if (principal.OwnTasksOnly)
            identity.AddClaim(new Claim(ApiKeyDefaults.OwnTasksOnlyClaim, "true"));

        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), ApiKeyDefaults.Scheme);
        return AuthenticateResult.Success(ticket);
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        // API clients want a status code, not a login redirect.
        Response.StatusCode = StatusCodes.Status401Unauthorized;
        Response.Headers.WWWAuthenticate = $"{ApiKeyDefaults.Scheme} realm=\"TaskTracker API\"";
        return Task.CompletedTask;
    }

    protected override Task HandleForbiddenAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status403Forbidden;
        return Task.CompletedTask;
    }

    private string? ExtractKey()
    {
        if (Request.Headers.TryGetValue(ApiKeyDefaults.HeaderName, out var apiKeyHeader))
        {
            var value = apiKeyHeader.ToString().Trim();
            if (value.Length > 0) return value;
        }

        if (Request.Headers.TryGetValue("Authorization", out var authHeader))
        {
            var value = authHeader.ToString();
            if (value.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            {
                var token = value["Bearer ".Length..].Trim();
                if (token.Length > 0) return token;
            }
        }

        return null;
    }
}
