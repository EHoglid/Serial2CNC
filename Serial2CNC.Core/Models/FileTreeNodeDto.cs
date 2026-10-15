namespace Serial2CNC.Models;

public sealed class FileTreeNodeDto
{
    public required string Name { get; init; }

    public required string RelativePath { get; init; }

    public bool IsDirectory { get; init; }

    public bool HasChildren { get; init; }

    public List<FileTreeNodeDto> Children { get; init; } = [];
}
