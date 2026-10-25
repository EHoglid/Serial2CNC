using Microsoft.AspNetCore.SignalR;
using Serial2CNC.Models;
using Serial2CNC.Services;

namespace Serial2CNC.Agent;

public sealed class TransferSessionService : IAsyncDisposable
{
    private readonly object _sync = new();
    private readonly IHubContext<TransferHub> _hubContext;
    private CancellationTokenSource? _cts;
    private Task? _runningTask;
    private bool _isRunning;
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
                IsRunning: _isRunning,
                StartedAt: _startedAt,
                LastStatus: _lastStatus);
        }
    }

    public async Task<bool> TryStartAsync(TransferRequest request, CancellationToken cancellationToken)
    {
        lock (_sync)
        {
            if (_isRunning)
            {
                return false;
            }

            _cts?.Dispose();
            cancellationToken.ThrowIfCancellationRequested();
            _cts = new CancellationTokenSource();
            _startedAt = DateTimeOffset.UtcNow;
            _lastStatus = "Starting transfer...";
            _isRunning = true;
            var token = _cts.Token;
            _runningTask = Task.Run(() => RunTransferAsync(request, token), CancellationToken.None);
        }

        await BroadcastStateAsync();
        return true;
    }

    public bool TryCancel()
    {
        lock (_sync)
        {
            if (!_isRunning || _cts is null)
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
                _isRunning = false;
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

    public async ValueTask DisposeAsync()
    {
        Task? running;
        lock (_sync)
        {
            _cts?.Cancel();
            running = _runningTask;
        }
        if (running is not null)
        {
            await running;
        }
    }
}

public sealed record TransferStateDto(bool IsRunning, DateTimeOffset? StartedAt, string LastStatus);
