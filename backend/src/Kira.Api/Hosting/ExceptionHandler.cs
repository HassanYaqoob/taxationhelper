using Microsoft.AspNetCore.Diagnostics;

namespace Kira.Api.Hosting;

/// <summary>Turns exceptions into RFC 9457 problem details. The "detail" is safe to show to the user.</summary>
public sealed class ExceptionHandler(IProblemDetailsService problems, ILogger<ExceptionHandler> log) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext ctx, Exception ex, CancellationToken ct)
    {
        var (status, title, detail) = ex switch
        {
            DomainException d => (400, "Check your input", d.Message),
            UnauthorizedException u => (401, "Unauthorised", u.Message),
            ForbiddenException f => (403, "Forbidden", f.Message),
            NotFoundException n => (404, "Not found", n.Message),
            AiUnavailableException a => (503, "AI unavailable", a.Message),
            BadHttpRequestException { StatusCode: 413 } => (413, "Too large", "That file is too large."),
            BadHttpRequestException b => (400, "Bad request", b.Message),
            OperationCanceledException => (499, "Cancelled", "The request was cancelled."),
            _ => (500, "Server error", "Something went wrong. Please try again.")
        };
        if (status == 500) log.LogError(ex, "Unhandled error on {Method} {Path}", ctx.Request.Method, ctx.Request.Path);
        ctx.Response.StatusCode = status;
        return await problems.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = ctx,
            Exception = ex,
            ProblemDetails = { Status = status, Title = title, Detail = detail }
        });
    }
}
