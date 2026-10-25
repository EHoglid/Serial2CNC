using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Serial2CNC.Logging;

namespace Serial2CNC.Agent.Middleware;

public sealed class FileAccessExceptionHandler(ILogger<FileAccessExceptionHandler> logger) : IExceptionHandler
{
    internal static readonly object RequestedPathKey = new();

    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        if (exception is not UnauthorizedAccessException)
        {
            return false;
        }

        var requestPath = LogValueSanitizer.Sanitize(httpContext.Request.Path.Value);
        logger.LogWarning(exception, "File system access denied for {RequestPath}.", requestPath);

        var requestedPath = httpContext.Items[RequestedPathKey] as string;
        var detail = string.IsNullOrWhiteSpace(requestedPath)
            ? "Access to the requested path is denied."
            : $"The folder '{requestedPath}' cannot be used because access is restricted. Choose another folder or check the host account's permissions.";

        httpContext.Response.StatusCode = StatusCodes.Status403Forbidden;
        await httpContext.Response.WriteAsJsonAsync(new ProblemDetails
        {
            Status = StatusCodes.Status403Forbidden,
            Title = "Path restricted / access denied",
            Detail = detail,
            Instance = httpContext.Request.Path
        }, options: null,
            contentType: "application/problem+json", cancellationToken: cancellationToken);

        return true;
    }
}
