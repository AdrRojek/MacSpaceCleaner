using MacSpaceCleaner.Core.Models;

namespace MacSpaceCleaner.Core;

public sealed class CategoryScanner
{
    public IReadOnlyList<CleanupCategory> CreateCategories()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var categories = new List<CleanupCategory>
        {
            new()
            {
                Id = "trash",
                Title = "Kosz",
                Description = "Zawartość kosza użytkownika i .Trashes na wolumenach zewnętrznych.",
                Group = CleanupGroup.System,
                IsEnabledByDefault = true,
                IsSelected = true
            },
            new()
            {
                Id = "user-caches",
                Title = "Cache użytkownika",
                Description = "Bezpieczne podfoldery w ~/Library/Caches (JetBrains, przeglądarki, pip, Homebrew, ShipIt).",
                Group = CleanupGroup.System,
                IsEnabledByDefault = true,
                IsSelected = true
            },
            new()
            {
                Id = "logs",
                Title = "Logi",
                Description = "Logi aplikacji w ~/Library/Logs.",
                Group = CleanupGroup.System,
                IsEnabledByDefault = true,
                IsSelected = true
            },
            new()
            {
                Id = "temp",
                Title = "Pliki tymczasowe",
                Description = "Zawartość /tmp dostępna dla użytkownika oraz stare pliki w katalogach tymczasowych.",
                Group = CleanupGroup.System,
                IsEnabledByDefault = true,
                IsSelected = true
            },
            new()
            {
                Id = "downloads-large",
                Title = "Duże pliki w Downloads",
                Description = "Pliki ≥ 100 MB w ~/Downloads (kasowane tylko po zaznaczeniu kategorii).",
                Group = CleanupGroup.System,
                IsEnabledByDefault = false,
                IsSelected = false
            },
            new()
            {
                Id = "dev-gradle",
                Title = "Dev: Gradle cache",
                Description = "~/.gradle/caches — cache buildów (projektów nie usuwa).",
                Group = CleanupGroup.Developer,
                IsEnabledByDefault = false,
                IsSelected = false
            },
            new()
            {
                Id = "dev-cocoapods",
                Title = "Dev: CocoaPods cache",
                Description = "~/.cocoapods — lokalny cache podów.",
                Group = CleanupGroup.Developer,
                IsEnabledByDefault = false,
                IsSelected = false
            },
            new()
            {
                Id = "dev-pub",
                Title = "Dev: Flutter/Dart pub-cache",
                Description = "~/.pub-cache — pobrane pakiety pub.",
                Group = CleanupGroup.Developer,
                IsEnabledByDefault = false,
                IsSelected = false
            },
            new()
            {
                Id = "dev-xcode",
                Title = "Dev: Xcode DerivedData / Archives",
                Description = "~/Library/Developer/Xcode/DerivedData oraz Archives.",
                Group = CleanupGroup.Developer,
                IsEnabledByDefault = false,
                IsSelected = false
            },
            new()
            {
                Id = "dev-android",
                Title = "Dev: Android cache / build leftovers",
                Description = "~/.android/cache oraz typowe cache SDK (nie usuwa całego SDK).",
                Group = CleanupGroup.Developer,
                IsEnabledByDefault = false,
                IsSelected = false
            },
            new()
            {
                Id = "dev-simulators",
                Title = "Dev: dane symulatorów (na dysku Maca)",
                Description = "~/Library/Developer/CoreSimulator/Caches — cache, nie całe runtime’y systemowe.",
                Group = CleanupGroup.Developer,
                IsEnabledByDefault = false,
                IsSelected = false
            }
        };

        // Bind default targets (sizes filled during ScanAsync)
        foreach (var cat in categories)
            cat.Targets.AddRange(BuildTargets(cat.Id, home));

        return categories;
    }

    public async Task ScanSizesAsync(
        IEnumerable<CleanupCategory> categories,
        IProgress<CleanupProgress>? progress = null,
        CancellationToken ct = default)
    {
        var list = categories.ToList();
        var total = list.Sum(c => Math.Max(1, c.Targets.Count));
        var current = 0;

        foreach (var category in list)
        {
            ct.ThrowIfCancellationRequested();
            category.IsScanning = true;
            category.Error = null;
            long sum = 0;

            try
            {
                foreach (var target in category.Targets)
                {
                    ct.ThrowIfCancellationRequested();
                    progress?.Report(new CleanupProgress
                    {
                        Message = $"Skan: {category.Title} — {target.Path}",
                        Current = current,
                        Total = total
                    });

                    target.SizeBytes = await DirectorySizer.GetSizeBytesAsync(target.Path, ct)
                        .ConfigureAwait(false);
                    sum += target.SizeBytes;
                    current++;
                }

                // Downloads: discover large files dynamically
                if (category.Id == "downloads-large")
                {
                    var downloads = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                        "Downloads");
                    category.Targets.Clear();
                    sum = 0;
                    if (Directory.Exists(downloads))
                    {
                        foreach (var file in Directory.EnumerateFiles(downloads))
                        {
                            ct.ThrowIfCancellationRequested();
                            try
                            {
                                var fi = new FileInfo(file);
                                if (fi.Length >= 100L * 1024 * 1024)
                                {
                                    category.Targets.Add(new CleanupTarget
                                    {
                                        Path = file,
                                        Action = CleanupActionKind.DeleteFile,
                                        SizeBytes = fi.Length,
                                        Note = "Downloads ≥ 100 MB"
                                    });
                                    sum += fi.Length;
                                }
                            }
                            catch
                            {
                                // skip
                            }
                        }
                    }
                }

                // Trash on external volumes
                if (category.Id == "trash")
                {
                    foreach (var volumeRoot in EnumerateExternalVolumeRoots())
                    {
                        var trashes = Path.Combine(volumeRoot, ".Trashes");
                        if (!Directory.Exists(trashes))
                            continue;
                        if (category.Targets.Any(t => t.Path == trashes))
                            continue;

                        var size = await DirectorySizer.GetSizeBytesAsync(trashes, ct)
                            .ConfigureAwait(false);
                        category.Targets.Add(new CleanupTarget
                        {
                            Path = trashes,
                            Action = CleanupActionKind.DeleteDirectoryContents,
                            SizeBytes = size,
                            Note = "Kosz wolumenu"
                        });
                        sum += size;
                    }
                }

                category.SizeBytes = sum;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                category.Error = ex.Message;
            }
            finally
            {
                category.IsScanning = false;
            }
        }

        progress?.Report(new CleanupProgress
        {
            Message = "Skan zakończony",
            Current = total,
            Total = total
        });
    }

    private static IEnumerable<CleanupTarget> BuildTargets(string id, string home)
    {
        return id switch
        {
            "trash" =>
            [
                new CleanupTarget
                {
                    Path = Path.Combine(home, ".Trash"),
                    Action = CleanupActionKind.DeleteDirectoryContents,
                    Note = "Kosz użytkownika"
                }
            ],
            "user-caches" => BuildCacheTargets(home),
            "logs" =>
            [
                new CleanupTarget
                {
                    Path = Path.Combine(home, "Library", "Logs"),
                    Action = CleanupActionKind.DeleteDirectoryContents,
                    Note = "Logi użytkownika"
                }
            ],
            "temp" =>
            [
                new CleanupTarget
                {
                    Path = "/tmp",
                    Action = CleanupActionKind.DeleteDirectoryContents,
                    Note = "Tylko pliki należące do użytkownika / dostępne do zapisu"
                },
                new CleanupTarget
                {
                    Path = Path.Combine(home, "Library", "Caches", "TemporaryItems"),
                    Action = CleanupActionKind.DeleteDirectoryContents
                }
            ],
            "downloads-large" => [],
            "dev-gradle" =>
            [
                new CleanupTarget
                {
                    Path = Path.Combine(home, ".gradle", "caches"),
                    Action = CleanupActionKind.DeleteDirectoryContents
                }
            ],
            "dev-cocoapods" =>
            [
                new CleanupTarget
                {
                    Path = Path.Combine(home, ".cocoapods"),
                    Action = CleanupActionKind.DeleteDirectoryContents
                }
            ],
            "dev-pub" =>
            [
                new CleanupTarget
                {
                    Path = Path.Combine(home, ".pub-cache"),
                    Action = CleanupActionKind.DeleteDirectoryContents
                }
            ],
            "dev-xcode" =>
            [
                new CleanupTarget
                {
                    Path = Path.Combine(home, "Library", "Developer", "Xcode", "DerivedData"),
                    Action = CleanupActionKind.DeleteDirectoryContents
                },
                new CleanupTarget
                {
                    Path = Path.Combine(home, "Library", "Developer", "Xcode", "Archives"),
                    Action = CleanupActionKind.DeleteDirectoryContents
                }
            ],
            "dev-android" =>
            [
                new CleanupTarget
                {
                    Path = Path.Combine(home, ".android", "cache"),
                    Action = CleanupActionKind.DeleteDirectoryContents
                },
                new CleanupTarget
                {
                    Path = Path.Combine(home, ".android", "build-cache"),
                    Action = CleanupActionKind.DeleteDirectoryContents
                }
            ],
            "dev-simulators" =>
            [
                new CleanupTarget
                {
                    Path = Path.Combine(home, "Library", "Developer", "CoreSimulator", "Caches"),
                    Action = CleanupActionKind.DeleteDirectoryContents
                }
            ],
            _ => []
        };
    }

    private static List<CleanupTarget> BuildCacheTargets(string home)
    {
        var cachesRoot = Path.Combine(home, "Library", "Caches");
        var names = new[]
        {
            "JetBrains",
            "Google",
            "Homebrew",
            "pip",
            "CocoaPods",
            "typescript",
            "vscode-cpptools",
            "com.todesktop.230313mzl4w4u92.ShipIt",
            "com.github.GitHubClient.ShipIt",
            "com.hnc.Discord.ShipIt",
            "ms-playwright",
            "node-gyp",
            "Yarn",
            "electron"
        };

        var targets = new List<CleanupTarget>();
        if (!Directory.Exists(cachesRoot))
            return targets;

        foreach (var name in names)
        {
            var path = Path.Combine(cachesRoot, name);
            if (Directory.Exists(path) || File.Exists(path))
            {
                targets.Add(new CleanupTarget
                {
                    Path = path,
                    Action = Directory.Exists(path)
                        ? CleanupActionKind.DeleteDirectory
                        : CleanupActionKind.DeleteFile,
                    Note = name
                });
            }
        }

        // Also pick any ShipIt caches
        try
        {
            foreach (var dir in Directory.EnumerateDirectories(cachesRoot, "*.ShipIt"))
            {
                if (targets.Any(t => t.Path == dir))
                    continue;
                targets.Add(new CleanupTarget
                {
                    Path = dir,
                    Action = CleanupActionKind.DeleteDirectory,
                    Note = Path.GetFileName(dir)
                });
            }
        }
        catch
        {
            // ignore
        }

        return targets;
    }

    private static IEnumerable<string> EnumerateExternalVolumeRoots()
    {
        const string volumes = "/Volumes";
        if (!Directory.Exists(volumes))
            yield break;

        IEnumerable<string> dirs;
        try
        {
            dirs = Directory.EnumerateDirectories(volumes);
        }
        catch
        {
            yield break;
        }

        foreach (var dir in dirs)
        {
            var name = Path.GetFileName(dir);
            if (name is "Macintosh HD" or ".timemachine")
                continue;
            yield return dir;
        }
    }
}
