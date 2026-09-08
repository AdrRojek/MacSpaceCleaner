using MacSpaceCleaner.Core.Models;

namespace MacSpaceCleaner.Core;

public sealed class LargeFileFinder
{
    private static readonly HashSet<string> SkipDirNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ".Spotlight-V100",
        ".fseventsd",
        ".DocumentRevisions-V100",
        ".TemporaryItems",
        "System",
        "proc",
        "dev",
        "Volumes" // avoid double-scanning when walking from /
    };

    public async Task<IReadOnlyList<LargeFileEntry>> FindAsync(
        IEnumerable<VolumeInfo> volumes,
        long minBytes = 500L * 1024 * 1024,
        int maxResults = 100,
        IProgress<CleanupProgress>? progress = null,
        CancellationToken ct = default)
    {
        return await Task.Run(() =>
        {
            var found = new List<LargeFileEntry>();
            var roots = volumes
                .Select(v => v.MountPoint)
                .Where(m => !string.IsNullOrWhiteSpace(m))
                .Distinct(StringComparer.Ordinal)
                .ToList();

            // Prefer Data + /Volumes over sealed system root
            if (roots.Contains("/System/Volumes/Data"))
                roots.RemoveAll(r => r == "/");

            if (roots.Count == 0)
                return (IReadOnlyList<LargeFileEntry>)found;

            // Give every disk a fair share so one huge volume doesn't hide the rest
            var perVolumeCap = Math.Max(20, (maxResults + roots.Count - 1) / roots.Count);

            var index = 0;
            foreach (var root in roots)
            {
                ct.ThrowIfCancellationRequested();
                progress?.Report(new CleanupProgress
                {
                    Message = $"Scanning large files on {root}",
                    Current = index++,
                    Total = roots.Count
                });

                var before = found.Count;
                ScanRoot(root, minBytes, found, before + perVolumeCap, ct);
            }

            return (IReadOnlyList<LargeFileEntry>)found
                .OrderByDescending(f => f.SizeBytes)
                .Take(maxResults)
                .ToList();
        }, ct).ConfigureAwait(false);
    }

    private static void ScanRoot(
        string root,
        long minBytes,
        List<LargeFileEntry> found,
        int maxResults,
        CancellationToken ct)
    {
        if (!Directory.Exists(root) && root != "/")
            return;

        var stack = new Stack<string>();
        stack.Push(root == "/" ? "/Users" : root);

        var visited = 0;
        while (stack.Count > 0 && found.Count < maxResults)
        {
            ct.ThrowIfCancellationRequested();
            var current = stack.Pop();
            visited++;

            if (visited > 200_000)
                break;

            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(current);
            }
            catch
            {
                continue;
            }

            foreach (var file in files)
            {
                if (found.Count >= maxResults)
                    return;

                try
                {
                    var fi = new FileInfo(file);
                    if (fi.Length >= minBytes && PathSafety.CanDelete(file))
                    {
                        found.Add(new LargeFileEntry
                        {
                            Path = file,
                            SizeBytes = fi.Length,
                            IsSelected = false
                        });
                    }
                }
                catch
                {
                    // skip
                }
            }

            IEnumerable<string> dirs;
            try
            {
                dirs = Directory.EnumerateDirectories(current);
            }
            catch
            {
                continue;
            }

            foreach (var dir in dirs)
            {
                var name = Path.GetFileName(dir);
                if (SkipDirNames.Contains(name))
                    continue;
                if (dir.StartsWith("/System", StringComparison.Ordinal))
                    continue;
                if (dir.StartsWith("/usr", StringComparison.Ordinal) ||
                    dir.StartsWith("/bin", StringComparison.Ordinal) ||
                    dir.StartsWith("/sbin", StringComparison.Ordinal))
                    continue;

                stack.Push(dir);
            }
        }
    }
}
