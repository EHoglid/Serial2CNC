using System.IO.Ports;
using System.Text;
using Serial2CNC.Models;

namespace Serial2CNC.Services;

public sealed class SerialLineTransport : ILineTransport
{
    private readonly SerialConnectionOptions _options;
    private readonly Action<string>? _status;
    private SerialPort? _port;

    public SerialLineTransport(SerialConnectionOptions options, Action<string>? status = null)
    {
        _options = options;
        _status = status;
    }

    public Task OpenAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        _port = new SerialPort
        {
            PortName = _options.PortName,
            BaudRate = _options.BaudRate,
            Parity = _options.Parity,
            DataBits = _options.DataBits,
            StopBits = _options.StopBits,
            Handshake = _options.Handshake,
            NewLine = "\r\n",
            Encoding = Encoding.ASCII,
            DtrEnable = true,
            RtsEnable = true
        };

        _port.Open();
        _status?.Invoke($"Serial connected: {_options.PortName}");

        return Task.CompletedTask;
    }

    public async Task SendLineAsync(string line, CancellationToken cancellationToken)
    {
        if (_port is null)
        {
            throw new InvalidOperationException("Serial port is not open.");
        }

        var payload = Encoding.ASCII.GetBytes(line + "\r\n");
        await _port.BaseStream.WriteAsync(payload, cancellationToken);
        await _port.BaseStream.FlushAsync(cancellationToken);
    }

    public ValueTask DisposeAsync()
    {
        if (_port is not null)
        {
            if (_port.IsOpen)
            {
                _port.Close();
            }

            _port.Dispose();
            _port = null;
        }

        return ValueTask.CompletedTask;
    }
}
