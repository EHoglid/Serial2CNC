using Serial2CNC.Models;

namespace Serial2CNC.Services;

public sealed class TransferRunner
{
    private static readonly SemaphoreSlim TransferGate = new(1, 1);
    private readonly CncTransferEngine _engine = new();

    public async Task RunAsync(
        TransferRequest request,
        Action<TransferProgress> report,
        Action<string>? status,
        CancellationToken cancellationToken)
    {
        if (!await TransferGate.WaitAsync(0, cancellationToken))
        {
            throw new InvalidOperationException("A transfer is already running in this application.");
        }
        try
        {
            await using ILineTransport transport = request.Mode switch
            {
                ConnectionMode.Tcp => new TcpLineTransport(request.Tcp, status),
                ConnectionMode.Serial => new SerialLineTransport(request.Serial, status),
                _ => throw new InvalidOperationException("Unsupported mode.")
            };

            await _engine.SendAsync(transport, request.Transfer, report, cancellationToken);
        }
        finally
        {
            TransferGate.Release();
        }
    }
}
