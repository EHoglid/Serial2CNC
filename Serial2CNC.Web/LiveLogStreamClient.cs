using Microsoft.AspNetCore.SignalR.Client;
using Serial2CNC.Models;

namespace Serial2CNC.Web;

public sealed class LiveLogStreamClient : IAsyncDisposable
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<LiveLogStreamClient> _logger;
    private HubConnection? _connection;
    private bool _initialized;
    private CancellationTokenSource? _connectCts;
    private Task? _connectLoopTask;
    private DateTimeOffset _lastUnavailableLogUtc;
    private bool _disposed;

    public event Action<LiveLogEntry>? LogReceived;
    public bool IsConnected => _connection?.State == HubConnectionState.Connected;

    public LiveLogStreamClient(IHttpClientFactory httpClientFactory, ILogger<LiveLogStreamClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(LiveLogStreamClient));
        }

        if (_initialized)
        {
            return;
        }

        _initialized = true;
        using var client = _httpClientFactory.CreateClient("api");
        var hubUrl = new Uri(client.BaseAddress
            ?? throw new InvalidOperationException("The application's HTTP client has no base address."), "/hubs/transfers");

        _connection = new HubConnectionBuilder()
            .WithUrl(hubUrl)
            .WithAutomaticReconnect()
            .Build();

        _connection.On<TransferProgress>("progress", progress => Publish("Information", progress.Message));
        _connection.On<string>("status", status => Publish("Information", status));
        _connection.On<string>("failed", error => Publish("Error", error));
        _connection.On("completed", () => Publish("Information", "Transfer complete."));
        _connection.On("canceled", () => Publish("Warning", "Transfer canceled."));

        _connectCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _connectLoopTask = RunConnectLoopAsync(_connectCts.Token);
        await Task.CompletedTask;
    }

    private async Task RunConnectLoopAsync(CancellationToken cancellationToken)
    {
        if (_connection is null)
        {
            return;
        }

        while (!cancellationToken.IsCancellationRequested)
        {
            if (_connection.State != HubConnectionState.Disconnected)
            {
                await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
                continue;
            }

            try
            {
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeoutCts.CancelAfter(TimeSpan.FromSeconds(1));

                await _connection.StartAsync(timeoutCts.Token);
                _logger.LogInformation("Connected to API live log stream.");
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                LogUnavailableOnce();
            }
            catch (HttpRequestException)
            {
                LogUnavailableOnce();
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Live log stream connection attempt failed.");
            }

            await Task.Delay(TimeSpan.FromSeconds(3), cancellationToken);
        }
    }

    private void LogUnavailableOnce()
    {
        var now = DateTimeOffset.UtcNow;
        if (now - _lastUnavailableLogUtc < TimeSpan.FromSeconds(30))
        {
            return;
        }

        _lastUnavailableLogUtc = now;
        _logger.LogWarning("The application's live transfer log stream is unavailable; retrying.");
    }

    private void Publish(string level, string message) =>
        LogReceived?.Invoke(new LiveLogEntry(DateTimeOffset.UtcNow, "Transfer", level, message));

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        var connectCts = _connectCts;
        _connectCts = null;

        if (connectCts is not null)
        {
            try
            {
                await connectCts.CancelAsync();
            }
            catch (ObjectDisposedException)
            {
            }
        }

        var connectLoopTask = _connectLoopTask;
        _connectLoopTask = null;

        if (connectLoopTask is not null)
        {
            try
            {
                await connectLoopTask;
            }
            catch (OperationCanceledException)
            {
            }
        }

        connectCts?.Dispose();

        var connection = _connection;
        _connection = null;

        if (connection is not null)
        {
            await connection.DisposeAsync();
        }

        _initialized = false;
    }
}
