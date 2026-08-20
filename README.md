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
2. **Skan kategorii** z szacunkiem GB:
   - Kosz (w tym `.Trashes` na dyskach zewnętrznych)
   - Cache użytkownika (`~/Library/Caches` — wybrane bezpieczne foldery)
   - Logi (`~/Library/Logs`)
   - Pliki tymczasowe (`/tmp`, TemporaryItems)
   - Duże pliki w Downloads (≥ 100 MB)
   - Lista dużych plików ≥ 500 MB (zaznaczasz ręcznie)
3. **Dev junk** (przełącznik, domyślnie ukryty / odznaczony): Gradle, CocoaPods, pub-cache, Xcode DerivedData/Archives, Android cache, cache CoreSimulator.

Czyszczenie **wymaga potwierdzenia**. Nie używa `sudo`, nie rusza `/System`, `/Applications` ani całego katalogu domowego.

## Ostrzeżenia

- Usuwanie jest trwałe (nie zawsze ląduje w Koszu).
- Cache narzędzi deweloperskich odbuduje się przy następnym buildzie — może chwilę potrwać.
- Przy bardzo pełnym dysku systemowym najpierw zwolnij kilka GB (np. cache), inaczej `dotnet restore` może się wyłożyć.

## Struktura

- `src/MacSpaceCleaner` — GUI Avalonia
- `src/MacSpaceCleaner.Core` — skan wolumenów, kategorie, bezpieczne usuwanie
