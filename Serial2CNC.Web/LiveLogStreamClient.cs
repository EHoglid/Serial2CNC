using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.SignalR;
using Serial2CNC.Security;
using Serial2CNC.Web.Security;

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
    private readonly UserAccess _access;

    public event Action<LiveLogEntry>? LogReceived;
    public bool IsConnected => _connection?.State == HubConnectionState.Connected;

    public LiveLogStreamClient(IConfiguration configuration, NavigationManager navigationManager, ILogger<LiveLogStreamClient> logger, UserAccess access)
    {
        _configuration = configuration;
        _navigationManager = navigationManager;
        _logger = logger;
        _access = access;
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
        await _access.RequireAsync();

        var apiBaseUrl = _configuration["Api:BaseUrl"] ?? _navigationManager.BaseUri;
        var hubUrl = $"{apiBaseUrl.TrimEnd('/')}/hubs/logs";

        _connection = new HubConnectionBuilder()
            .WithUrl(hubUrl, options => options.Headers.Add(ServiceSecurity.HeaderName, _configuration.RequireKey("Api:ApiKey")))
            .WithAutomaticReconnect()
            .Build();

        _connection.On<LiveLogEntry>("log", async entry =>
        {
            try
            {
                await _access.RequireAsync();
                LogReceived?.Invoke(entry);
            }
            catch (HttpRequestException ex) when (ex.StatusCode is System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden)
            {
                _logger.LogInformation("Discarded log event for an expired or unauthorized session.");
            }
        });

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
                try
                {
                    await _access.RequireAsync();
                }
                catch (HttpRequestException ex) when (ex.StatusCode is System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden)
                {
                    _logger.LogInformation("Stopping live logs because the user session is no longer authorized.");
                    await _connection.StopAsync(cancellationToken);
                    return;
                }
                await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
                continue;
            }

            try
            {
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeoutCts.CancelAfter(TimeSpan.FromSeconds(1));

                await _access.RequireAsync();
                await _connection.StartAsync(timeoutCts.Token);
                _logger.LogInformation("Connected to API live log stream.");
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                LogUnavailableOnce();
            }
            catch (HttpRequestException ex) when (ex.StatusCode is System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden)
            {
                _logger.LogWarning("Live log authentication failed; sign in again or check the service credential.");
                return;
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
