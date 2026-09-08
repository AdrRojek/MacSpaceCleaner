using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using MacSpaceCleaner.Core.Models;

namespace MacSpaceCleaner.Core;

/// <summary>
/// Optional cloud AI (OpenAI-compatible) ranking for large candidates.
/// Uses OPENAI_API_KEY or ~/.config/macspacecleaner/openai_api_key.
/// </summary>
public sealed class AiJunkAnalyzer
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(90) };

    public string? ResolveApiKey()
    {
        var env = Environment.GetEnvironmentVariable("OPENAI_API_KEY")
                  ?? Environment.GetEnvironmentVariable("MACSPACECLEANER_OPENAI_KEY");
        if (!string.IsNullOrWhiteSpace(env))
            return env.Trim();

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var path = Path.Combine(home, ".config", "macspacecleaner", "openai_api_key");
        if (File.Exists(path))
        {
            var key = File.ReadAllText(path).Trim();
            if (!string.IsNullOrWhiteSpace(key))
                return key;
        }

        return null;
    }

    public bool IsAvailable => !string.IsNullOrWhiteSpace(ResolveApiKey());

    public async Task<string> AnalyzeAsync(
        IList<CandidateEntry> candidates,
        IProgress<CleanupProgress>? progress = null,
        CancellationToken ct = default)
    {
        var apiKey = ResolveApiKey();
        if (string.IsNullOrWhiteSpace(apiKey))
            return "Brak klucza API. Ustaw OPENAI_API_KEY albo plik ~/.config/macspacecleaner/openai_api_key";

        // Send top heaviest items to keep prompt small
        var batch = candidates
            .OrderByDescending(c => c.SizeBytes)
            .Take(60)
            .ToList();

        if (batch.Count == 0)
            return "Brak plików do analizy AI.";

        progress?.Report(new CleanupProgress
        {
            Message = $"AI analizuje {batch.Count} największych pozycji…",
            Current = 0,
            Total = 1
        });

        var listBuilder = new StringBuilder();
        for (var i = 0; i < batch.Count; i++)
        {
            var c = batch[i];
            listBuilder.AppendLine(
                $"{i}|{ByteFormatter.Format(c.SizeBytes)}|{c.CategoryTitle}|{(c.IsDirectory ? "dir" : "file")}|{c.Path}");
        }

        var system = """
            Jesteś asystentem czyszczenia dysku macOS. Dostajesz listę ścieżek (id|rozmiar|kategoria|typ|ścieżka).
            Oceń, które pozycje są prawdopodobnie NIEPOTzebne / bezpieczne do usunięcia (cache, kosz, tmp, derived data, stare instalatory),
            a które mogą być ważne (dokumenty, zdjęcia, projekty, klucze, Documents, Desktop z unikalnymi danymi).
            Zwróć WYŁĄCZNIE JSON array obiektów:
            [{"id":0,"score":0-100,"reason":"krótko po polsku","recommend":true/false}]
            score=100 oznacza typowy śmieć do skasowania. Nie wymyślaj ścieżek spoza listy.
            """;

        var user = "Lista:\n" + listBuilder;

        var payload = new
        {
            model = Environment.GetEnvironmentVariable("MACSPACECLEANER_OPENAI_MODEL") ?? "gpt-4o-mini",
            temperature = 0.1,
            response_format = new { type = "json_object" },
            messages = new object[]
            {
                new { role = "system", content = system },
                new { role = "user", content = "Zwróć JSON w formie {\"items\":[...]} .\n\n" + user }
            }
        };

        using var req = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/chat/completions");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        req.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        using var resp = await Http.SendAsync(req, ct).ConfigureAwait(false);
        var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode)
            return $"AI błąd HTTP {(int)resp.StatusCode}: {Truncate(body, 300)}";

        using var doc = JsonDocument.Parse(body);
        var content = doc.RootElement
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString();

        if (string.IsNullOrWhiteSpace(content))
            return "AI nie zwróciło treści.";

        ApplyAiJson(content, batch);

        progress?.Report(new CleanupProgress
        {
            Message = "Analiza AI zakończona",
            Current = 1,
            Total = 1
        });

        var recommended = candidates.Count(c => c.IsRecommended);
        return $"AI oceniło {batch.Count} pozycji. Rekomendowanych łącznie: {recommended}.";
    }

    private static void ApplyAiJson(string content, List<CandidateEntry> batch)
    {
        content = content.Trim();
        JsonElement root;
        try
        {
            using var parsed = JsonDocument.Parse(content);
            root = parsed.RootElement.Clone();
        }
        catch
        {
            return;
        }

        JsonElement items;
        if (root.ValueKind == JsonValueKind.Array)
            items = root;
        else if (root.TryGetProperty("items", out var arr))
            items = arr;
        else
            return;

        foreach (var el in items.EnumerateArray())
        {
            if (!el.TryGetProperty("id", out var idEl))
                continue;
            var id = idEl.GetInt32();
            if (id < 0 || id >= batch.Count)
                continue;

            var c = batch[id];
            var score = el.TryGetProperty("score", out var s) ? s.GetInt32() : c.JunkScore;
            var reason = el.TryGetProperty("reason", out var r) ? r.GetString() : c.AnalysisReason;
            var recommend = el.TryGetProperty("recommend", out var rec) && rec.ValueKind is JsonValueKind.True or JsonValueKind.False
                ? rec.GetBoolean()
                : score >= 70;

            // Blend with heuristic: take max score so we don't lose strong local signals
            c.JunkScore = Math.Clamp(Math.Max(c.JunkScore, score), 0, 100);
            if (!string.IsNullOrWhiteSpace(reason))
                c.AnalysisReason = reason;
            c.AnalysisSource = "AI + heurystyka";
            c.IsRecommended = recommend || c.JunkScore >= 75;
            if (c.IsRecommended && c.JunkScore >= 85)
                c.IsSelected = true;
        }
    }

    private static string Truncate(string s, int n) =>
        s.Length <= n ? s : s[..n] + "…";
}
