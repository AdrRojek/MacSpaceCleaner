namespace MacSpaceCleaner.Core.Models;

public enum CleanupGroup
{
    System,
    Developer
}

public sealed class CleanupCategory
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public required string Description { get; init; }
    public CleanupGroup Group { get; init; }
    public bool IsSelected { get; set; }
    public bool IsEnabledByDefault { get; init; } = true;
    public long SizeBytes { get; set; }
    public List<CleanupTarget> Targets { get; init; } = [];
    public bool IsScanning { get; set; }
    public string? Error { get; set; }
}

public sealed class CleanupTarget
{
    public required string Path { get; init; }
    public required CleanupActionKind Action { get; init; }
    public long SizeBytes { get; set; }
    public string? Note { get; init; }
}

public enum CleanupActionKind
{
    DeleteDirectoryContents,
    DeleteDirectory,
    DeleteFile
}

public sealed class CleanupProgress
{
    public required string Message { get; init; }
    public int Current { get; init; }
    public int Total { get; init; }
}

public sealed class CleanupResult
{
    public long FreedBytes { get; init; }
    public int DeletedItems { get; init; }
    public IReadOnlyList<string> Errors { get; init; } = [];
    public IReadOnlyList<string> Log { get; init; } = [];
}

public sealed class LargeFileEntry
{
    public required string Path { get; init; }
    public long SizeBytes { get; init; }
    public bool IsSelected { get; set; }
}
