using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using TaskTracker.Services;

namespace TaskTracker.Web.Auth;

/// <summary>
/// Makes API-key write requests idempotent when they carry an <c>Idempotency-Key</c> header. The
/// first request runs and its successful response (status + JSON body) is cached per (API client,
/// key); a retry with the same key replays the cached response without re-running the action —
/// preventing duplicate creates on network retries. Only applies to unsafe methods authenticated
/// with an API key; cookie sessions and GETs are unaffected. Only 2xx responses are cached, so a
/// failed request can always be retried.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false)]
public sealed class ApiIdempotencyAttribute : Attribute, IAsyncActionFilter
{
    public const string HeaderName = "Idempotency-Key";
    public const string ReplayedHeader = "Idempotency-Replayed";

    private static readonly HashSet<string> UnsafeMethods =
        new(StringComparer.OrdinalIgnoreCase) { "POST", "PUT", "PATCH", "DELETE" };
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var http = context.HttpContext;
        var key = http.Request.Headers[HeaderName].ToString().Trim();
        var clientIdClaim = http.User.FindFirst(ApiKeyDefaults.ClientIdClaim)?.Value;

        // Idempotency is opt-in (header) and only for API-key write requests.
        if (!UnsafeMethods.Contains(http.Request.Method)
            || string.IsNullOrEmpty(key)
            || !int.TryParse(clientIdClaim, out var clientId))
        {
            await next();
            return;
        }

        var store = http.RequestServices.GetRequiredService<IIdempotencyStore>();

        var cached = await store.TryGetAsync(clientId, key, http.RequestAborted);
        if (cached != null)
        {
            http.Response.Headers[ReplayedHeader] = "true";
            context.Result = new ContentResult
            {
                StatusCode = cached.StatusCode,
                Content = cached.ResponseBody,
                ContentType = "application/json",
            };
            return;
        }

        var executed = await next();
        if (executed.Result is null || (executed.Exception != null && !executed.ExceptionHandled))
            return;

        var (status, body) = Extract(executed.Result);
        if (status is >= 200 and < 300)   // only cache success; failures stay retryable
        {
            await store.SaveAsync(clientId, key, http.Request.Method, http.Request.Path.Value ?? "", status, body, http.RequestAborted);
        }
    }

    private static (int Status, string? Body) Extract(IActionResult result) => result switch
    {
        ObjectResult obj => (obj.StatusCode ?? StatusCodes.Status200OK,
                             obj.Value is null ? null : JsonSerializer.Serialize(obj.Value, JsonOptions)),
        StatusCodeResult sc => (sc.StatusCode, null),
        ContentResult cr => (cr.StatusCode ?? StatusCodes.Status200OK, cr.Content),
        _ => (StatusCodes.Status200OK, null),
    };
}
