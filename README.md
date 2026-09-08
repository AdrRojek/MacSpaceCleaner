# MacSpaceCleaner

Aplikacja desktopowa na **macOS** (C# / .NET 9 + Avalonia), która skanuje **cały komputer** — wszystkie zamontowane wolumeny — i pomaga bezpiecznie zwolnić miejsce.

## Wymagania

- macOS
- [.NET 9 SDK](https://dotnet.microsoft.com/download)
- (opcjonalnie) miejsce na cache NuGet — przy pełnym dysku systemowym ustaw:

```bash
export NUGET_PACKAGES=/Volumes/ADATA_SE880/nuget-packages
export TMPDIR=/Volumes/ADATA_SE880/tmp
```

## Uruchomienie

```bash
cd /Volumes/ADATA_SE880/projects/MacSpaceCleaner
dotnet run --project src/MacSpaceCleaner/MacSpaceCleaner.csproj -c Release
```

## Co robi

1. **Lista wolumenów** — wewnętrzny dysk, ADATA i inne `/Volumes/*` (rozmiar / zajęte / wolne).
2. **Skan kategorii** z szacunkiem GB (kosz, cache, logi, tmp, Downloads, opcjonalnie Dev junk).
3. **Dalej** — lista plików/folderów + **analiza**:
   - zawsze **heurystyka lokalna** (cache, kosz, DerivedData, tmp, duże instalatory…),
   - opcjonalnie **Groq** (darmowy tier) lub OpenAI — największe pozycje.
4. Filtr **Tylko rekomendowane**, score, powód, **W Finderze**, potem **Wyczyść zaznaczone**.

### Klucz AI (opcjonalnie) — preferowane Groq

```bash
mkdir -p ~/.config/macspacecleaner
echo "gsk_..." > ~/.config/macspacecleaner/groq_api_key
# albo: export GROQ_API_KEY="gsk_..."
```

Model Groq: `llama-3.3-70b-versatile` (nadpisz `MACSPACECLEANER_GROQ_MODEL`).  
OpenAI nadal działa przez `openai_api_key` / `OPENAI_API_KEY`.

Czyszczenie **wymaga potwierdzenia**. Nie używa `sudo`, nie rusza `/System`, `/Applications` ani całego katalogu domowego.

## Ostrzeżenia

- Usuwanie jest trwałe (nie zawsze ląduje w Koszu).
- Cache narzędzi deweloperskich odbuduje się przy następnym buildzie — może chwilę potrwać.
- Przy bardzo pełnym dysku systemowym najpierw zwolnij kilka GB (np. cache), inaczej `dotnet restore` może się wyłożyć.

## Struktura

- `src/MacSpaceCleaner` — GUI Avalonia
- `src/MacSpaceCleaner.Core` — skan wolumenów, kategorie, bezpieczne usuwanie
