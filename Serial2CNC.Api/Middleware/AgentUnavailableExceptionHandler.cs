using Microsoft.AspNetCore.Diagnostics;

namespace Serial2CNC.Api.Middleware;

internal sealed class AgentUnavailableExceptionHandler(ILogger<AgentUnavailableExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken cancellationToken)
    {
        if (context.RequestAborted.IsCancellationRequested
            || exception is not (HttpRequestException or OperationCanceledException))
        {
            return false;
        }

        var timedOut = exception is OperationCanceledException;
        logger.LogWarning(exception, "Agent request failed for {RequestPath}.", context.Request.Path);
        await Results.Problem(
            statusCode: timedOut ? StatusCodes.Status504GatewayTimeout : StatusCodes.Status503ServiceUnavailable,
            title: timedOut ? "Agent not responding" : "Agent unavailable",
            detail: timedOut
                ? "The Agent did not respond in time. Check that the Agent service is running."
                : "The API cannot reach the Agent. Check that the Agent service is running and reachable.")
            .ExecuteAsync(context);
        return true;
    }
}
