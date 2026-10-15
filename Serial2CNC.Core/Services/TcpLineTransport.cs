using System.Net.Sockets;
using System.Text;
using Serial2CNC.Models;

namespace Serial2CNC.Services;

public sealed class TcpLineTransport : ILineTransport
{
    private const byte Xon = 0x11;
    private const byte Xoff = 0x13;

    private readonly TcpConnectionOptions _options;
    private readonly Action<string>? _status;
    private readonly object _stateLock = new();

    private TcpClient? _client;
    private NetworkStream? _stream;
    private CancellationTokenSource? _readerCts;
    private Task? _readerTask;
    private bool _allowSending = true;

    public TcpLineTransport(TcpConnectionOptions options, Action<string>? status = null)
    {
        _options = options;
        _status = status;
    }

    public async Task OpenAsync(CancellationToken cancellationToken)
    {
        _client = new TcpClient();
        await _client.ConnectAsync(_options.Host, _options.Port, cancellationToken);

        _stream = _client.GetStream();
        _status?.Invoke($"TCP connected: {_options.Host}:{_options.Port}");

        if (_options.UseXonXoff)
        {
            _readerCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _readerTask = Task.Run(() => ReceiveLoopAsync(_readerCts.Token), CancellationToken.None);
        }
    }

    public async Task SendLineAsync(string line, CancellationToken cancellationToken)
    {
        if (_stream is null)
        {
            throw new InvalidOperationException("TCP stream is not open.");
        }

        if (_options.UseXonXoff)
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                lock (_stateLock)
                {
                    if (_allowSending)
                    {
                        break;
                    }
                }

                await Task.Delay(15, cancellationToken);
            }
        }

        var payload = Encoding.ASCII.GetBytes(line + "\r\n");
        await _stream.WriteAsync(payload, cancellationToken);
        await _stream.FlushAsync(cancellationToken);
    }

    private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
    {
        if (_stream is null)
        {
            return;
        }

        var buffer = new byte[1];

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var read = await _stream.ReadAsync(buffer, cancellationToken);
                if (read == 0)
                {
                    break;
                }

                var value = buffer[0];
                if (value == Xoff)
                {
                    lock (_stateLock)
                    {
                        _allowSending = false;
                    }

                    _status?.Invoke("Receiving XOFF...");
                }
                else if (value == Xon)
                {
                    lock (_stateLock)
                    {
                        _allowSending = true;
                    }

                    _status?.Invoke("Receiving XON...");
                }
            }
        }
        catch (OperationCanceledException)
        {
            // expected during shutdown
        }
        catch (Exception ex)
        {
            _status?.Invoke($"TCP receive loop ended: {ex.Message}");
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_readerCts is not null)
        {
            _readerCts.Cancel();
        }

        if (_readerTask is not null)
        {
            try
            {
                await _readerTask;
            }
            catch (OperationCanceledException)
            {
                // ignored
            }
        }

        _readerCts?.Dispose();
        _readerCts = null;
        _readerTask = null;

        if (_stream is not null)
        {
            await _stream.DisposeAsync();
            _stream = null;
        }

        _client?.Dispose();
        _client = null;
    }
}
