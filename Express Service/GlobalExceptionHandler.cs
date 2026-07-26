using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace Express_Service;

/// <summary>
/// Converts unhandled exceptions into a stable, non-sensitive API response while
/// retaining the exception and request correlation identifier in server logs.
/// </summary>
public sealed class GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        logger.LogError(
            exception,
            "Unhandled request failure. CorrelationId: {CorrelationId}; Method: {Method}; Path: {Path}",
            httpContext.TraceIdentifier,
            httpContext.Request.Method,
            httpContext.Request.Path);

        if (httpContext.Response.HasStarted)
            return false;

        var problem = new ProblemDetails
        {
            Status = StatusCodes.Status500InternalServerError,
            Title = "UnexpectedError",
            Detail = "An unexpected error occurred. Use the correlation ID when contacting support.",
            Instance = httpContext.Request.Path
        };

        problem.Extensions["correlationId"] = httpContext.TraceIdentifier;
        problem.Extensions["exceptionType"] = exception.GetType().FullName;
        problem.Extensions["exceptionMessage"] = exception.Message;
        if (exception.InnerException is not null)
            problem.Extensions["innerExceptionMessage"] = exception.InnerException.Message;

        httpContext.Response.StatusCode = StatusCodes.Status500InternalServerError;
        await httpContext.Response.WriteAsJsonAsync(problem, cancellationToken);

        return true;
    }
}
