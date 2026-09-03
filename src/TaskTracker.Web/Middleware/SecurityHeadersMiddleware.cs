namespace TaskTracker.Web.Middleware;

/// <summary>
/// Adds a baseline of browser security response headers to every response: framing/MIME protection,
/// referrer and permissions policies, and a Content-Security-Policy. The CSP default is deliberately
/// compatible with the app's current front-end (Google Fonts, the Tailwind Play/jsDelivr/cdnjs CDNs,
/// and inline handlers) and can be overridden — or disabled with "off" — via
/// <c>SecurityHeaders:ContentSecurityPolicy</c>.
/// </summary>
public sealed class SecurityHeadersMiddleware(RequestDelegate next, string? contentSecurityPolicy)
{
    // Permissive where the current UI requires it ('unsafe-inline'/'unsafe-eval' for the Tailwind Play
    // CDN and inline event handlers) but still restricts sources, framing, objects, and base URIs.
    private const string DefaultCsp =
        "default-src 'self'; " +
        "script-src 'self' 'unsafe-inline' 'unsafe-eval' https://cdn.tailwindcss.com https://cdn.jsdelivr.net https://cdnjs.cloudflare.com; " +
        "style-src 'self' 'unsafe-inline' https://fonts.googleapis.com; " +
        "font-src 'self' https://fonts.gstatic.com data:; " +
        "img-src 'self' data: https:; " +
        "connect-src 'self' https: wss: ws:; " +
        "frame-ancestors 'self'; " +
        "object-src 'none'; " +
        "base-uri 'self'; " +
        "form-action 'self'";

    private readonly bool _cspDisabled = string.Equals(contentSecurityPolicy, "off", StringComparison.OrdinalIgnoreCase);
    private readonly string _csp = string.IsNullOrWhiteSpace(contentSecurityPolicy) ? DefaultCsp : contentSecurityPolicy!;

    public Task InvokeAsync(HttpContext context)
    {
        var headers = context.Response.Headers;
        headers["X-Content-Type-Options"] = "nosniff";
        headers["X-Frame-Options"] = "SAMEORIGIN";
        headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
        headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
        if (!_cspDisabled && !headers.ContainsKey("Content-Security-Policy"))
            headers["Content-Security-Policy"] = _csp;
        return next(context);
    }
}
