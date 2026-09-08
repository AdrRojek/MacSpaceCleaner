using System.Text.RegularExpressions;
using MacSpaceCleaner.Core.Models;

namespace MacSpaceCleaner.Core;

/// <summary>
/// Offline heuristic "smart" analysis: ranks large, typically regenerable / junk paths.
/// </summary>
public sealed class SmartJunkAnalyzer
{
    private static readonly (Regex Pattern, int Score, string Reason)[] Rules =
    [
        (new Regex(@"/\.Trash(/|$)", RegexOptions.IgnoreCase), 95, "Kosz — zwykle bezpieczne do usunięcia"),
        (new Regex(@"/\.Trashes(/|$)", RegexOptions.IgnoreCase), 95, "Kosz wolumenu"),
        (new Regex(@"/Library/Caches/", RegexOptions.IgnoreCase), 88, "Cache aplikacji — odbuduje się"),
        (new Regex(@"/\.gradle/caches", RegexOptions.IgnoreCase), 90, "Cache Gradle — bezpieczny do skasowania"),
        (new Regex(@"/\.cocoapods", RegexOptions.IgnoreCase), 85, "Cache CocoaPods"),
        (new Regex(@"/\.pub-cache", RegexOptions.IgnoreCase), 82, "Cache Flutter/Dart pub"),
        (new Regex(@"/\.npm/_cacache|/node_modules/", RegexOptions.IgnoreCase), 80, "Cache/zależności Node — da się odtworzyć"),
        (new Regex(@"/DerivedData(/|$)", RegexOptions.IgnoreCase), 92, "Xcode DerivedData — regeneruje się przy buildzie"),
        (new Regex(@"/Xcode/Archives(/|$)", RegexOptions.IgnoreCase), 70, "Archiwa Xcode — sprawdź, czy ich potrzebujesz"),
        (new Regex(@"/\.android/cache|/\.android/build-cache", RegexOptions.IgnoreCase), 88, "Cache Androida"),
        (new Regex(@"/CoreSimulator/Caches", RegexOptions.IgnoreCase), 85, "Cache symulatora iOS"),
        (new Regex(@"/(Logs|log)/", RegexOptions.IgnoreCase), 75, "Logi — rzadko potrzebne na stałe"),
        (new Regex(@"(/tmp/|/TemporaryItems/|\.tmp$|\.temp$|~\$)", RegexOptions.IgnoreCase), 90, "Plik tymczasowy"),
        (new Regex(@"\.(dmg|pkg|iso|zip|tar\.gz|7z)$", RegexOptions.IgnoreCase), 65, "Instalator/archiwum — często zbędny po instalacji"),
        (new Regex(@"/Downloads/", RegexOptions.IgnoreCase), 55, "Pobrany plik — sprawdź przed usunięciem"),
        (new Regex(@"ShipIt|/Code Cache|/GPUCache|/CacheStorage", RegexOptions.IgnoreCase), 87, "Cache Electron/przeglądarki"),
        (new Regex(@"/\.dartServer/|/\.cursor/|/Crash Reports/", RegexOptions.IgnoreCase), 70, "Dane narzędzi deweloperskich / crash"),
    ];

    public void Analyze(IList<CandidateEntry> candidates)
    {
        foreach (var c in candidates)
        {
            var (score, reason) = Score(c);
            // Boost by size: bigger junk = higher priority in recommendations
            var sizeBoost = c.SizeBytes switch
            {
                >= 5L * 1024 * 1024 * 1024 => 8,
                >= 1L * 1024 * 1024 * 1024 => 5,
                >= 500L * 1024 * 1024 => 3,
                >= 100L * 1024 * 1024 => 1,
                _ => 0
            };

            c.JunkScore = Math.Clamp(score + sizeBoost, 0, 100);
            c.AnalysisReason = reason;
            c.AnalysisSource = "heurystyka";
            c.IsRecommended = c.JunkScore >= 70 && c.SizeBytes >= 20L * 1024 * 1024;
            if (c.IsRecommended && !c.IsSelected && score >= 85)
                c.IsSelected = true;
        }
    }

    private static (int Score, string Reason) Score(CandidateEntry c)
    {
        var path = c.Path;
        var best = 20;
        var reason = "Duży plik — oceń ręcznie, czy jest potrzebny";

        foreach (var (pattern, score, r) in Rules)
        {
            if (pattern.IsMatch(path) && score > best)
            {
                best = score;
                reason = r;
            }
        }

        // Category hints
        if (c.CategoryTitle.Contains("Cache", StringComparison.OrdinalIgnoreCase) ||
            c.CategoryTitle.Contains("Kosz", StringComparison.OrdinalIgnoreCase) ||
            c.CategoryTitle.Contains("Log", StringComparison.OrdinalIgnoreCase) ||
            c.CategoryTitle.Contains("tymczas", StringComparison.OrdinalIgnoreCase))
        {
            best = Math.Max(best, 80);
            if (best == 80)
                reason = $"Kategoria „{c.CategoryTitle}” — typowy junk";
        }

        if (c.CategoryTitle.Contains("Dev:", StringComparison.OrdinalIgnoreCase))
        {
            best = Math.Max(best, 78);
            reason = $"Dev junk: {c.CategoryTitle}";
        }

        return (best, reason);
    }
}
