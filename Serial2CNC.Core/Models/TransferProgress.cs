namespace Serial2CNC.Models;

public sealed class TransferProgress
{
    public required string Message { get; init; }

    public int Current { get; init; }

    public int Total { get; init; }

    public string? Line { get; init; }
}
