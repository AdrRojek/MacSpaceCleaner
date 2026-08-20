namespace MacSpaceCleaner.Core.Models;

public sealed class VolumeInfo
{
    public required string Name { get; init; }
    public required string MountPoint { get; init; }
    public long TotalBytes { get; init; }
    public long UsedBytes { get; init; }
    public long FreeBytes { get; init; }
    public bool IsRoot { get; init; }

    public double UsedPercent =>
        TotalBytes <= 0 ? 0 : UsedBytes * 100.0 / TotalBytes;

    public string DisplayLabel =>
        string.IsNullOrWhiteSpace(Name) ? MountPoint : $"{Name} ({MountPoint})";
}
