namespace Serial2CNC.Models;

public sealed class FileTreeSnapshotDto
{
    public required string RootPath { get; init; }

    public required DateTimeOffset LastRefreshedUtc { get; init; }

    public required FileTreeNodeDto Root { get; init; }
}
