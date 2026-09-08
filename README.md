# MacSpaceCleaner

A macOS desktop utility that finds reclaimable disk space across **all mounted disks for any user** — the internal Macintosh HD / Data volume **and every external drive under `/Volumes`** (USB SSDs, HDDs, SD cards, etc.). It ranks likely junk with local heuristics (optional cloud AI), then deletes only what you confirm.

Built with **C# / .NET 9** and **Avalonia**.

![.NET](https://img.shields.io/badge/.NET-9-512BD4?style=flat-square)
![Platform](https://img.shields.io/badge/platform-macOS-000000?style=flat-square)
![UI](https://img.shields.io/badge/UI-Avalonia-0F766E?style=flat-square)

---

## Why it exists

macOS “Storage” often looks full even when Downloads are empty. The real weight is usually developer caches, leftover build artifacts, Xcode DerivedData, Gradle/CocoaPods, browser caches, and forgotten installers — on the **system disk** and on **any attached external disks**.

MacSpaceCleaner targets the **whole machine for whoever runs it**, not one hard-coded drive name.

## Features

- **All disks** — discovers `/`, `/System/Volumes/Data`, and every mount in `/Volumes/*` for the current user
- **Category scan** — Trash, user caches, logs, temp files, large Downloads, optional developer junk
- **File-level review** — path, size, checkbox, Reveal in Finder (`open -R`)
- **Smart ranking** — local heuristics score “safe to delete” candidates
- **Optional AI** — Groq (preferred) or OpenAI ranks the heaviest items
- **Safe by design** — no `sudo`, never touches `/System` or `/Applications`, always asks for confirmation
- **Clear result** — after cleanup, a **Deleted** success screen shows how much space was freed

## Requirements

- macOS
- [.NET 9 SDK](https://dotnet.microsoft.com/download)

### Optional: free space for builds

If your **system** disk is nearly full and `dotnet restore` fails, temporarily point NuGet/temp to **any** disk with free space (example — replace with your own mount):

```bash
# Example only — use YOUR external disk path, not a fixed brand name
export NUGET_PACKAGES="/Volumes/YourExternalDisk/nuget-packages"
export TMPDIR="/Volumes/YourExternalDisk/tmp"
mkdir -p "$NUGET_PACKAGES" "$TMPDIR"
```

This is only for building/running tooling. The app itself always scans **all** mounted volumes.

## Run

```bash
cd /path/to/MacSpaceCleaner
dotnet run --project src/MacSpaceCleaner/MacSpaceCleaner.csproj -c Release
```

### Workflow

1. **Scan Mac** — list every volume + cleanup categories  
2. **Next** — build the candidate list, run heuristics (+ AI if configured)  
3. Review / tweak selection (Recommended / All / None, Finder)  
4. **Clean selected…** — confirm, delete, see the **Deleted** summary  

## Optional AI keys

Local heuristics always work. For cloud ranking (per-user config in the home folder):

```bash
mkdir -p ~/.config/macspacecleaner

# Preferred (Groq free tier)
echo "gsk_..." > ~/.config/macspacecleaner/groq_api_key

# Or OpenAI
echo "sk-..." > ~/.config/macspacecleaner/openai_api_key
```

Environment variables also work: `GROQ_API_KEY`, `OPENAI_API_KEY`.  
Default Groq model: `openai/gpt-oss-20b` (override with `MACSPACECLEANER_GROQ_MODEL`).

## Project layout

```
src/MacSpaceCleaner/          Avalonia desktop UI
src/MacSpaceCleaner.Core/     Volume scan, categories, analyzers, safe delete
```

## Safety notes

- Deletion is permanent (not always recoverable via Trash)
- Developer caches will regenerate on the next build
- Always use **Finder** on unfamiliar paths before deleting
- Keep a few GB free so restore/build tooling can run

## License

Private repository. All rights reserved unless otherwise stated by the owner.
