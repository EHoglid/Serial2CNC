namespace Serial2CNC.Services;

public interface ILineTransport : IAsyncDisposable
{
    Task OpenAsync(CancellationToken cancellationToken);

    Task SendLineAsync(string line, CancellationToken cancellationToken);
}
