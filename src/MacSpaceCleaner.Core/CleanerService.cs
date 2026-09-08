using MacSpaceCleaner.Core.Models;

namespace MacSpaceCleaner.Core;

public sealed class CleanerService
{
    public async Task<CleanupResult> CleanCandidatesAsync(
        IEnumerable<CandidateEntry> candidates,
        IProgress<CleanupProgress>? progress = null,
        CancellationToken ct = default)
    {
        return await Task.Run(() =>
        {
            long freed = 0;
            var deleted = 0;
            var errors = new List<string>();
            var log = new List<string>();

            // Delete deepest paths first so files go before their parent folders
            var selected = candidates
                .Where(c => c.IsSelected)
                .OrderByDescending(c => c.Path.Length)
                .ToList();

            var totalSteps = Math.Max(1, selected.Count);
            var step = 0;

            foreach (var item in selected)
            {
                ct.ThrowIfCancellationRequested();
                step++;
                progress?.Report(new CleanupProgress
                {
                    Message = $"Usuwam: {item.Path}",
                    Current = step,
                    Total = totalSteps
                });

                try
                {
                    if (!PathSafety.CanDelete(item.Path) &&
                        !(item.Path.StartsWith("/tmp", StringComparison.Ordinal) ||
                          item.Path.StartsWith("/private/tmp", StringComparison.Ordinal)))
                        throw new InvalidOperationException("Ścieżka niedozwolona.");

                    long bytes;
                    int count;
                    if (File.Exists(item.Path))
                        (bytes, count) = DeleteFile(item.Path);
                    else if (Directory.Exists(item.Path))
                        (bytes, count) = DeleteDirectory(item.Path);
                    else
                        continue;

                    freed += bytes;
                    deleted += count;
                    log.Add($"OK [{item.CategoryTitle}] {item.Path} (−{ByteFormatter.Format(bytes)})");
                }
                catch (Exception ex)
                {
                    errors.Add($"{item.Path}: {ex.Message}");
                    log.Add($"BŁĄD [{item.CategoryTitle}] {item.Path}: {ex.Message}");
                }
            }

            progress?.Report(new CleanupProgress
            {
                Message = $"Gotowe. Zwolniono {ByteFormatter.Format(freed)}.",
                Current = totalSteps,
                Total = totalSteps
            });

            return new CleanupResult
            {
                FreedBytes = freed,
                DeletedItems = deleted,
                Errors = errors,
                Log = log
            };
        }, ct).ConfigureAwait(false);
    }

    public async Task<CleanupResult> CleanAsync(
        IEnumerable<CleanupCategory> selectedCategories,
        IEnumerable<LargeFileEntry>? selectedLargeFiles = null,
        IProgress<CleanupProgress>? progress = null,
        CancellationToken ct = default)
    {
        return await Task.Run(() =>
        {
            long freed = 0;
            var deleted = 0;
            var errors = new List<string>();
            var log = new List<string>();

            var categories = selectedCategories.Where(c => c.IsSelected).ToList();
            var large = (selectedLargeFiles ?? []).Where(f => f.IsSelected).ToList();
            var totalSteps = categories.Sum(c => c.Targets.Count) + large.Count;
            var step = 0;

            foreach (var category in categories)
            {
                foreach (var target in category.Targets)
                {
                    ct.ThrowIfCancellationRequested();
                    step++;
                    progress?.Report(new CleanupProgress
                    {
                        Message = $"Usuwam: {target.Path}",
                        Current = step,
                        Total = Math.Max(1, totalSteps)
                    });

                    try
                    {
                        var (bytes, count) = DeleteTarget(target);
                        freed += bytes;
                        deleted += count;
                        log.Add($"OK [{category.Title}] {target.Path} (−{ByteFormatter.Format(bytes)})");
                    }
                    catch (Exception ex)
                    {
                        errors.Add($"{target.Path}: {ex.Message}");
                        log.Add($"BŁĄD [{category.Title}] {target.Path}: {ex.Message}");
                    }
                }
            }

            foreach (var file in large)
            {
                ct.ThrowIfCancellationRequested();
                step++;
                progress?.Report(new CleanupProgress
                {
                    Message = $"Usuwam duży plik: {file.Path}",
                    Current = step,
                    Total = Math.Max(1, totalSteps)
                });

                try
                {
                    if (!PathSafety.CanDelete(file.Path))
                        throw new InvalidOperationException("Ścieżka niedozwolona.");

                    if (File.Exists(file.Path))
                    {
                        var size = new FileInfo(file.Path).Length;
                        File.Delete(file.Path);
                        freed += size;
                        deleted++;
                        log.Add($"OK [Duże pliki] {file.Path} (−{ByteFormatter.Format(size)})");
                    }
                }
                catch (Exception ex)
                {
                    errors.Add($"{file.Path}: {ex.Message}");
                    log.Add($"BŁĄD [Duże pliki] {file.Path}: {ex.Message}");
                }
            }

            progress?.Report(new CleanupProgress
            {
                Message = $"Gotowe. Zwolniono {ByteFormatter.Format(freed)}.",
                Current = totalSteps,
                Total = Math.Max(1, totalSteps)
            });

            return new CleanupResult
            {
                FreedBytes = freed,
                DeletedItems = deleted,
                Errors = errors,
                Log = log
            };
        }, ct).ConfigureAwait(false);
    }

    private static (long Bytes, int Count) DeleteTarget(CleanupTarget target)
    {
        if (!PathSafety.CanDelete(target.Path))
            throw new InvalidOperationException("Ścieżka niedozwolona przez reguły bezpieczeństwa.");

        return target.Action switch
        {
            CleanupActionKind.DeleteFile => DeleteFile(target.Path),
            CleanupActionKind.DeleteDirectory => DeleteDirectory(target.Path),
            CleanupActionKind.DeleteDirectoryContents => DeleteDirectoryContents(target.Path),
            _ => (0, 0)
        };
    }

    private static (long Bytes, int Count) DeleteFile(string path)
    {
        if (!File.Exists(path))
            return (0, 0);

        var size = new FileInfo(path).Length;
        File.Delete(path);
        return (size, 1);
    }

    private static (long Bytes, int Count) DeleteDirectory(string path)
    {
        if (!Directory.Exists(path))
            return (0, 0);

        var size = DirectorySizer.GetSizeBytes(path);
        Directory.Delete(path, recursive: true);
        return (size, 1);
    }

    private static (long Bytes, int Count) DeleteDirectoryContents(string path)
    {
        if (!Directory.Exists(path))
            return (0, 0);

        long freed = 0;
        var count = 0;

        foreach (var file in Directory.EnumerateFiles(path))
        {
            try
            {
                if (!PathSafety.CanDelete(file) && !IsTempException(path, file))
                    continue;

                var size = new FileInfo(file).Length;
                File.Delete(file);
                freed += size;
                count++;
            }
            catch
            {
                // skip locked files
            }
        }

        foreach (var dir in Directory.EnumerateDirectories(path))
        {
            try
            {
                if (!PathSafety.CanDelete(dir) && !IsTempException(path, dir))
                    continue;

                var size = DirectorySizer.GetSizeBytes(dir);
                Directory.Delete(dir, recursive: true);
                freed += size;
                count++;
            }
            catch
            {
                // skip
            }
        }

        return (freed, count);
    }

    private static bool IsTempException(string root, string path)
    {
        // Allow deleting contents of /tmp that we can write to
        return root is "/tmp" or "/private/tmp";
    }
}
