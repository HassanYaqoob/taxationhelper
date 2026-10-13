namespace Kira.Api.Hosting;

/// <summary>Adds standard security headers to every response.</summary>
public sealed class SecurityHeadersMiddleware(RequestDelegate next)
{
    public Task Invoke(HttpContext ctx)
    {
        var h = ctx.Response.Headers;
        h.XContentTypeOptions = "nosniff";
        h.XFrameOptions = "DENY";
        h["Referrer-Policy"] = "strict-origin-when-cross-origin";
        h["Permissions-Policy"] = "camera=(self), geolocation=(), microphone=()";
        h["Cross-Origin-Opener-Policy"] = "same-origin";
        if (ctx.Request.Path.StartsWithSegments("/api")) h.CacheControl = "no-store";
        return next(ctx);
    }
}
