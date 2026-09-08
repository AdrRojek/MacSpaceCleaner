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
   - opcjonalnie **OpenAI** (największe pozycje), jeśli ustawisz klucz.
4. Filtr **Tylko rekomendowane**, score, powód, **W Finderze**, potem **Wyczyść zaznaczone**.

### Klucz OpenAI (opcjonalnie)

```bash
export OPENAI_API_KEY="sk-..."
# albo:
mkdir -p ~/.config/macspacecleaner
echo "sk-..." > ~/.config/macspacecleaner/openai_api_key
```

Model: domyślnie `gpt-4o-mini` (nadpisz `MACSPACECLEANER_OPENAI_MODEL`).

Czyszczenie **wymaga potwierdzenia**. Nie używa `sudo`, nie rusza `/System`, `/Applications` ani całego katalogu domowego.

## Ostrzeżenia

- Usuwanie jest trwałe (nie zawsze ląduje w Koszu).
- Cache narzędzi deweloperskich odbuduje się przy następnym buildzie — może chwilę potrwać.
- Przy bardzo pełnym dysku systemowym najpierw zwolnij kilka GB (np. cache), inaczej `dotnet restore` może się wyłożyć.

## Struktura

- `src/MacSpaceCleaner` — GUI Avalonia
- `src/MacSpaceCleaner.Core` — skan wolumenów, kategorie, bezpieczne usuwanie
