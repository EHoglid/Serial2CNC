using Microsoft.AspNetCore.SignalR.Client;
using Serial2CNC.Models;

namespace Serial2CNC.Api.Logs;

public sealed class AgentLogBridgeService(
    IConfiguration configuration,
    LogBroadcastService logs,
    ILogger<AgentLogBridgeService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var baseUrl = configuration["Agent:BaseUrl"] ?? "http://localhost:62904";
        await using var connection = new HubConnectionBuilder()
            .WithUrl($"{baseUrl.TrimEnd('/')}/hubs/transfers")
            .WithAutomaticReconnect()
            .Build();

        connection.On<string>("status", message =>
            logs.BroadcastAsync("Agent", "Information", message, stoppingToken));
        connection.On<TransferProgress>("progress", progress =>
            logs.BroadcastAsync("Agent", "Information", progress.Message, stoppingToken));
        connection.On<string>("failed", message =>
            logs.BroadcastAsync("Agent", "Error", message, stoppingToken));
        connection.On("completed", () =>
            logs.BroadcastAsync("Agent", "Information", "Transfer complete.", stoppingToken));
        connection.On("canceled", () =>
            logs.BroadcastAsync("Agent", "Information", "Transfer canceled.", stoppingToken));

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                if (connection.State == HubConnectionState.Disconnected)
                {
                    try
                    {
                        await connection.StartAsync(stoppingToken);
                        logger.LogInformation("Connected to Agent transfer events.");
                    }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                    {
                        break;
                    }
                    catch (Exception exception)
                    {
                        logger.LogWarning(exception, "Cannot connect to Agent transfer events; retrying in five seconds.");
                    }
                }

                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }
}
