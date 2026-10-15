using Serial2CNC.Models;
using Serial2CNC.Services;

namespace Serial2CNC.Presentation;

public sealed class TransferExecutionService
{
    private readonly TransferRunner _runner = new();

    public Task RunAsync(
        TransferRequest request,
        Action<TransferProgress> report,
        Action<string>? status,
        CancellationToken cancellationToken)
    {
        return _runner.RunAsync(request, report, status, cancellationToken);
    }
}
