using Microsoft.AspNetCore.SignalR;

namespace Serial2CNC.Api.Logs;

public sealed class LogBroadcastService(IHubContext<LogStreamHub> hubContext)
{
    public Task BroadcastAsync(string source, string level, string message, CancellationToken cancellationToken)
    {
        var entry = new LiveLogEntry(DateTimeOffset.UtcNow, source, level, message);
        return hubContext.Clients.All.SendAsync("log", entry, cancellationToken);
    }

    private sealed record LiveLogEntry(DateTimeOffset Timestamp, string Source, string Level, string Message);
}
