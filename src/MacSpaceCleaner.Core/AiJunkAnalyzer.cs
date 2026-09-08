using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using MacSpaceCleaner.Core.Models;

namespace MacSpaceCleaner.Core;

/// <summary>
/// Optional cloud AI ranking via OpenAI-compatible APIs.
/// Prefers Groq (free tier), then OpenAI.
/// </summary>
public sealed class AiJunkAnalyzer
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(90) };

    private sealed record Provider(string Name, string ApiKey, string Endpoint, string Model);

    public string? ResolveApiKey() => ResolveProvider()?.ApiKey;

    public bool IsAvailable => ResolveProvider() is not null;

    public string? ActiveProviderName => ResolveProvider()?.Name;

    private static Provider? ResolveProvider()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var configDir = Path.Combine(home, ".config", "macspacecleaner");

        // 1) Explicit Groq
        var groq = Environment.GetEnvironmentVariable("GROQ_API_KEY")
                   ?? Environment.GetEnvironmentVariable("MACSPACECLEANER_GROQ_KEY")
                   ?? ReadKeyFile(Path.Combine(configDir, "groq_api_key"));
        if (!string.IsNullOrWhiteSpace(groq))
        {
            var model = Environment.GetEnvironmentVariable("MACSPACECLEANER_GROQ_MODEL")
                        ?? "openai/gpt-oss-20b";
            return new Provider(
                "Groq",
                groq.Trim(),
                "https://api.groq.com/openai/v1/chat/completions",
                model);
        }

        // 2) OpenAI / generic (also accept gsk_ via OPENAI_API_KEY pointing at Groq URL)
        var openai = Environment.GetEnvironmentVariable("OPENAI_API_KEY")
                     ?? Environment.GetEnvironmentVariable("MACSPACECLEANER_OPENAI_KEY")
                     ?? ReadKeyFile(Path.Combine(configDir, "openai_api_key"));
        if (!string.IsNullOrWhiteSpace(openai))
        {
            var key = openai.Trim();
            if (key.StartsWith("gsk_", StringComparison.Ordinal))
            {
                var model = Environment.GetEnvironmentVariable("MACSPACECLEANER_GROQ_MODEL")
                            ?? "openai/gpt-oss-20b";
                return new Provider(
                    "Groq",
                    key,
                    "https://api.groq.com/openai/v1/chat/completions",
                    model);
            }

            var oaiModel = Environment.GetEnvironmentVariable("MACSPACECLEANER_OPENAI_MODEL")
                           ?? "gpt-4o-mini";
            var endpoint = Environment.GetEnvironmentVariable("MACSPACECLEANER_OPENAI_BASE")
                           ?? "https://api.openai.com/v1/chat/completions";
            return new Provider("OpenAI", key, endpoint, oaiModel);
        }

        return null;
    }

    private static string? ReadKeyFile(string path)
    {
        try
        {
            if (!File.Exists(path))
                return null;
            var key = File.ReadAllText(path).Trim();
            return string.IsNullOrWhiteSpace(key) ? null : key;
        }
        catch
        {
            return null;
        }
    }

    public async Task<string> AnalyzeAsync(
        IList<CandidateEntry> candidates,
        IProgress<CleanupProgress>? progress = null,
        CancellationToken ct = default)
    {
        var provider = ResolveProvider();
        if (provider is null)
        {
            return "Brak klucza API. Ustaw GROQ_API_KEY / ~/.config/macspacecleaner/groq_api_key " +
                   "albo OPENAI_API_KEY.";
        }

        var batch = candidates
            .OrderByDescending(c => c.SizeBytes)
            .Take(60)
            .ToList();

        if (batch.Count == 0)
            return "Brak plików do analizy AI.";

        progress?.Report(new CleanupProgress
        {
            Message = $"{provider.Name} analizuje {batch.Count} największych pozycji…",
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
            Oceń, które pozycje są prawdopodobnie niepotrzebne / bezpieczne do usunięcia (cache, kosz, tmp, derived data, stare instalatory),
            a które mogą być ważne (dokumenty, zdjęcia, projekty, klucze, Documents, Desktop z unikalnymi danymi).
            Zwróć WYŁĄCZNIE JSON: {"items":[{"id":0,"score":0-100,"reason":"krótko po polsku","recommend":true/false}]}
            score=100 oznacza typowy śmieć do skasowania. Nie wymyślaj ścieżek spoza listy.
            """;

        // Try primary model, then Groq free-tier fallbacks if model was deprecated
        var modelsToTry = provider.Name == "Groq"
            ? new[]
            {
                provider.Model,
                "openai/gpt-oss-20b",
                "openai/gpt-oss-120b",
                "qwen/qwen3.6-27b",
                "llama-3.1-8b-instant"
            }.Distinct(StringComparer.Ordinal).ToArray()
            : new[] { provider.Model };

        string? content = null;
        string lastError = "";
        string usedModel = provider.Model;

        foreach (var model in modelsToTry)
        {
            ct.ThrowIfCancellationRequested();
            var attempt = new Provider(provider.Name, provider.ApiKey, provider.Endpoint, model);
            var payload = BuildPayload(attempt.Model, system, listBuilder.ToString());
            payload["response_format"] = new { type = "json_object" };

            var (ok, body) = await SendAsync(attempt, payload, ct).ConfigureAwait(false);
            if (!ok && body.Contains("response_format", StringComparison.OrdinalIgnoreCase))
            {
                payload.Remove("response_format");
                (ok, body) = await SendAsync(attempt, payload, ct).ConfigureAwait(false);
            }

            if (!ok)
            {
                lastError = body;
                if (body.Contains("model_not_found", StringComparison.OrdinalIgnoreCase) ||
                    body.Contains("does not exist", StringComparison.OrdinalIgnoreCase))
                    continue;
                return $"{provider.Name}: {FriendlyHttpError(body)}";
            }

            try
            {
                using var doc = JsonDocument.Parse(body);
                content = doc.RootElement
                    .GetProperty("choices")[0]
                    .GetProperty("message")
                    .GetProperty("content")
                    .GetString();
                usedModel = model;
                break;
            }
            catch (Exception ex)
            {
                lastError = $"{ex.Message}: {Truncate(body, 200)}";
            }
        }

        if (string.IsNullOrWhiteSpace(content))
            return $"{provider.Name}: {FriendlyHttpError(lastError)}";

        ApplyAiJson(content, batch, provider.Name);

        progress?.Report(new CleanupProgress
        {
            Message = $"Analiza {provider.Name} zakończona",
            Current = 1,
            Total = 1
        });

        var recommended = candidates.Count(c => c.IsRecommended);
        return $"{provider.Name} ({usedModel}) ocenił {batch.Count} pozycji. Rekomendowanych łącznie: {recommended}.";
    }

    private static Dictionary<string, object?> BuildPayload(string model, string system, string listText) =>
        new()
        {
            ["model"] = model,
            ["temperature"] = 0.1,
            ["messages"] = new object[]
            {
                new { role = "system", content = system },
                new { role = "user", content = "Zwróć JSON w formie {\"items\":[...]} .\n\nLista:\n" + listText }
            }
        };

    private static string FriendlyHttpError(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
            return "brak odpowiedzi z API.";

        try
        {
            using var doc = JsonDocument.Parse(body.Contains('{') ? body[body.IndexOf('{')..] : body);
            if (doc.RootElement.TryGetProperty("error", out var err) &&
                err.TryGetProperty("message", out var msg))
                return msg.GetString() ?? Truncate(body, 180);
        }
        catch
        {
            // fall through
        }

        return Truncate(body, 180);
    }

    private static async Task<(bool Ok, string Body)> SendAsync(
        Provider provider,
        object payload,
        CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, provider.Endpoint);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", provider.ApiKey);
        req.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        using var resp = await Http.SendAsync(req, ct).ConfigureAwait(false);
        var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        return (resp.IsSuccessStatusCode, resp.IsSuccessStatusCode ? body : $"{(int)resp.StatusCode} {body}");
    }

    private static void ApplyAiJson(string content, List<CandidateEntry> batch, string providerName)
    {
        content = content.Trim();
        // Strip markdown fences if model wraps JSON
        if (content.StartsWith("```", StringComparison.Ordinal))
        {
            var firstNl = content.IndexOf('\n');
            var lastFence = content.LastIndexOf("```", StringComparison.Ordinal);
            if (firstNl >= 0 && lastFence > firstNl)
                content = content[(firstNl + 1)..lastFence].Trim();
        }

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
            var recommend = el.TryGetProperty("recommend", out var rec) &&
                            rec.ValueKind is JsonValueKind.True or JsonValueKind.False
                ? rec.GetBoolean()
                : score >= 70;

            c.JunkScore = Math.Clamp(Math.Max(c.JunkScore, score), 0, 100);
            if (!string.IsNullOrWhiteSpace(reason))
                c.AnalysisReason = reason;
            c.AnalysisSource = $"{providerName} + heurystyka";
            c.IsRecommended = recommend || c.JunkScore >= 75;
            if (c.IsRecommended && c.JunkScore >= 85)
                c.IsSelected = true;
        }
    }

    private static string Truncate(string s, int n) =>
        s.Length <= n ? s : s[..n] + "…";
}
