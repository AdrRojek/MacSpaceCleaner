using MacSpaceCleaner.Core.Models;

namespace MacSpaceCleaner.Core;

public sealed class CandidateBuilder
{
    public const int MaxCandidates = 8_000;

    public async Task<IReadOnlyList<CandidateEntry>> BuildAsync(
        IEnumerable<CleanupCategory> categories,
        IEnumerable<LargeFileEntry> largeFiles,
        IProgress<CleanupProgress>? progress = null,
        CancellationToken ct = default)
    {
        return await Task.Run(() =>
        {
            var map = new Dictionary<string, CandidateEntry>(StringComparer.Ordinal);
            var selectedCats = categories.Where(c => c.IsSelected).ToList();
            var step = 0;
            var total = Math.Max(1, selectedCats.Sum(c => Math.Max(1, c.Targets.Count)) + 1);

            foreach (var category in selectedCats)
            {
                foreach (var target in category.Targets)
                {
                    ct.ThrowIfCancellationRequested();
                    step++;
                    progress?.Report(new CleanupProgress
                    {
                        Message = $"Buduję listę plików: {target.Path}",
                        Current = step,
                        Total = total
                    });

                    AddTarget(map, target, category.Title, selectedByDefault: false, ct);
                    if (map.Count >= MaxCandidates)
                        break;
                }

                if (map.Count >= MaxCandidates)
                    break;
            }

            foreach (var large in largeFiles)
            {
                ct.ThrowIfCancellationRequested();
                Upsert(map, new CandidateEntry
                {
                    Path = large.Path,
                    SizeBytes = large.SizeBytes,
                    CategoryTitle = "Duże pliki",
                    IsDirectory = Directory.Exists(large.Path) && !File.Exists(large.Path),
                    IsSelected = large.IsSelected
                });
            }

            progress?.Report(new CleanupProgress
            {
                Message = $"Lista gotowa: {map.Count} pozycji",
                Current = total,
                Total = total
            });

            return (IReadOnlyList<CandidateEntry>)map.Values
                .OrderByDescending(c => c.SizeBytes)
                .ToList();
        }, ct).ConfigureAwait(false);
    }

    private static void AddTarget(
        Dictionary<string, CandidateEntry> map,
        CleanupTarget target,
        string categoryTitle,
        bool selectedByDefault,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(target.Path))
            return;

        if (File.Exists(target.Path))
        {
            long size;
            try { size = new FileInfo(target.Path).Length; }
            catch { size = target.SizeBytes; }

            Upsert(map, new CandidateEntry
            {
                Path = target.Path,
                SizeBytes = size,
                CategoryTitle = categoryTitle,
                IsDirectory = false,
                IsSelected = selectedByDefault
            });
            return;
        }

        if (!Directory.Exists(target.Path))
            return;

        // Enumerate files under the target. If too many, fall back to the folder itself.
        var files = new List<CandidateEntry>();
        try
        {
            foreach (var file in Directory.EnumerateFiles(target.Path, "*", SearchOption.AllDirectories))
            {
                ct.ThrowIfCancellationRequested();
                if (files.Count + map.Count >= MaxCandidates)
                    break;

                if (!PathSafety.CanDelete(file) &&
                    !(target.Path is "/tmp" or "/private/tmp"))
                    continue;

                try
                {
                    var fi = new FileInfo(file);
                    files.Add(new CandidateEntry
                    {
                        Path = file,
                        SizeBytes = fi.Length,
                        CategoryTitle = categoryTitle,
                        IsDirectory = false,
                        IsSelected = selectedByDefault
                    });
                }
                catch
                {
                    // skip
                }
            }
        }
        catch
        {
            // inaccessible — keep folder row
        }

        if (files.Count == 0)
        {
            Upsert(map, new CandidateEntry
            {
                Path = target.Path,
                SizeBytes = target.SizeBytes > 0 ? target.SizeBytes : DirectorySizer.GetSizeBytes(target.Path, ct),
                CategoryTitle = categoryTitle,
                IsDirectory = true,
                IsSelected = selectedByDefault
            });
            return;
        }

        // If folder is huge, keep the folder as one selectable item instead of flooding the list
        if (files.Count > 1_500 || map.Count + files.Count > MaxCandidates)
        {
            Upsert(map, new CandidateEntry
            {
                Path = target.Path,
                SizeBytes = target.SizeBytes > 0
                    ? target.SizeBytes
                    : files.Sum(f => f.SizeBytes),
                CategoryTitle = categoryTitle,
                IsDirectory = true,
                IsSelected = selectedByDefault
            });
            return;
        }

        foreach (var f in files)
            Upsert(map, f);
    }

    private static void Upsert(Dictionary<string, CandidateEntry> map, CandidateEntry entry)
    {
        if (map.TryGetValue(entry.Path, out var existing))
        {
            var selected = existing.IsSelected || entry.IsSelected;
            var size = Math.Max(existing.SizeBytes, entry.SizeBytes);
            map[entry.Path] = new CandidateEntry
            {
                Path = entry.Path,
                SizeBytes = size,
                CategoryTitle = existing.SizeBytes >= entry.SizeBytes
                    ? existing.CategoryTitle
                    : entry.CategoryTitle,
                IsDirectory = existing.IsDirectory || entry.IsDirectory,
                IsSelected = selected
            };
            return;
        }

        map[entry.Path] = entry;
    }
}
