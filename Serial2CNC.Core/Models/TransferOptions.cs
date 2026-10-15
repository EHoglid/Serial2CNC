namespace Serial2CNC.Models;

public sealed class TransferOptions
{
    public required string FilePath { get; init; }

    public int SendDelayMs { get; init; }

    public bool UseOsp7000lFraming { get; init; }

    public string? ProgramName { get; init; }
}
