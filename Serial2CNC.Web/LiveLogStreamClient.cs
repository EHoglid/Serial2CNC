using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.SignalR;

namespace Serial2CNC.Web;

public sealed class LiveLogStreamClient : IAsyncDisposable
{
    private readonly IConfiguration _configuration;
    private readonly NavigationManager _navigationManager;
    private readonly ILogger<LiveLogStreamClient> _logger;
    private HubConnection? _connection;
    private bool _initialized;
    private CancellationTokenSource? _connectCts;
    private Task? _connectLoopTask;
    private DateTimeOffset _lastUnavailableLogUtc;
    private bool _disposed;

    public event Action<LiveLogEntry>? LogReceived;

    public LiveLogStreamClient(IConfiguration configuration, NavigationManager navigationManager, ILogger<LiveLogStreamClient> logger)
    {
        _configuration = configuration;
        _navigationManager = navigationManager;
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

        var apiBaseUrl = _configuration["Api:BaseUrl"] ?? _navigationManager.BaseUri;
        var hubUrl = $"{apiBaseUrl.TrimEnd('/')}/hubs/logs";

        _connection = new HubConnectionBuilder()
            .WithUrl(hubUrl)
            .WithAutomaticReconnect()
            .Build();

        _connection.On<LiveLogEntry>("log", entry => LogReceived?.Invoke(entry));

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
            if (_connection.State == HubConnectionState.Connected)
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
        _logger.LogInformation("Live log stream unavailable. Start the API project to enable real-time logs.");
    }

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
