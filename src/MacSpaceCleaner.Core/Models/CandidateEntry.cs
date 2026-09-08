namespace MacSpaceCleaner.Core.Models;

public sealed class CandidateEntry
{
    public required string Path { get; init; }
    public long SizeBytes { get; init; }
    public required string CategoryTitle { get; init; }
    public bool IsDirectory { get; init; }
    public bool IsSelected { get; set; }

    /// <summary>0–100: how likely the item is safe/unnecessary junk.</summary>
    public int JunkScore { get; set; }

    public string? AnalysisReason { get; set; }
    public bool IsRecommended { get; set; }
    public string AnalysisSource { get; set; } = "none";
}
