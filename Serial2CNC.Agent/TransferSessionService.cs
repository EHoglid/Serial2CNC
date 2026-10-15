using Microsoft.AspNetCore.SignalR;
using Serial2CNC.Models;
using Serial2CNC.Services;

namespace Serial2CNC.Agent;

public sealed class TransferSessionService
{
    private readonly object _sync = new();
    private readonly IHubContext<TransferHub> _hubContext;
    private CancellationTokenSource? _cts;
    private Task? _runningTask;
    private DateTimeOffset? _startedAt;
    private string _lastStatus = "Idle";

    public TransferSessionService(IHubContext<TransferHub> hubContext)
    {
        _hubContext = hubContext;
    }

    public TransferStateDto GetState()
    {
        lock (_sync)
        {
            return new TransferStateDto(
                IsRunning: _runningTask is { IsCompleted: false },
                StartedAt: _startedAt,
                LastStatus: _lastStatus);
        }
    }

    public async Task<bool> TryStartAsync(TransferRequest request, CancellationToken cancellationToken)
    {
        lock (_sync)
        {
            if (_runningTask is { IsCompleted: false })
            {
                return false;
            }

            _cts?.Dispose();
            _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _startedAt = DateTimeOffset.UtcNow;
            _lastStatus = "Starting transfer...";
            _runningTask = RunTransferAsync(request, _cts.Token);
        }

        await BroadcastStateAsync();
        return true;
    }

    public bool TryCancel()
    {
        lock (_sync)
        {
            if (_runningTask is not { IsCompleted: false } || _cts is null)
            {
                return false;
            }

            _cts.Cancel();
            _lastStatus = "Cancel requested.";
            _ = BroadcastStateAsync();
            return true;
        }
    }

    private async Task RunTransferAsync(TransferRequest request, CancellationToken cancellationToken)
    {
        var runner = new TransferRunner();

        try
        {
            await runner.RunAsync(
                request,
                progress =>
                {
                    UpdateStatus(progress.Message);
                    _ = _hubContext.Clients.All.SendAsync("progress", progress, CancellationToken.None);
                },
                status =>
                {
                    if (!string.IsNullOrWhiteSpace(status))
                    {
                        UpdateStatus(status);
                        _ = _hubContext.Clients.All.SendAsync("status", status, CancellationToken.None);
                    }
                },
                cancellationToken);

            UpdateStatus("Transfer complete.");
            await _hubContext.Clients.All.SendAsync("completed", CancellationToken.None);
        }
        catch (OperationCanceledException)
        {
            UpdateStatus("Transfer canceled.");
            await _hubContext.Clients.All.SendAsync("canceled", CancellationToken.None);
        }
        catch (Exception ex)
        {
            UpdateStatus($"Transfer failed: {ex.Message}");
            await _hubContext.Clients.All.SendAsync("failed", ex.Message, CancellationToken.None);
        }
        finally
        {
            lock (_sync)
            {
                _cts?.Dispose();
                _cts = null;
                _startedAt = null;
            }

            await BroadcastStateAsync();
        }
    }

    private void UpdateStatus(string status)
    {
        lock (_sync)
        {
            _lastStatus = status;
        }

        _ = BroadcastStateAsync();
    }

    private Task BroadcastStateAsync()
    {
        return _hubContext.Clients.All.SendAsync("state", GetState(), CancellationToken.None);
    }
}

public sealed record TransferStateDto(bool IsRunning, DateTimeOffset? StartedAt, string LastStatus);
