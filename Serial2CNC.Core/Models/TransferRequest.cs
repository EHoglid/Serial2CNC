namespace Serial2CNC.Models;

public sealed class TransferRequest
{
    public ConnectionMode Mode { get; init; }

    public TcpConnectionOptions Tcp { get; init; } = new();

    public SerialConnectionOptions Serial { get; init; } = new();

    public required TransferOptions Transfer { get; init; }
}
