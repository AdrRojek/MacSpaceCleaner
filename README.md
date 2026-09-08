# MacSpaceCleaner

A focused macOS desktop utility that finds reclaimable disk space across **every mounted volume**, ranks likely junk with local heuristics (and optional cloud AI), then deletes only what you explicitly confirm.

Built with **C# / .NET 9** and **Avalonia**.

![.NET](https://img.shields.io/badge/.NET-9-512BD4?style=flat-square)
![Platform](https://img.shields.io/badge/platform-macOS-000000?style=flat-square)
![UI](https://img.shields.io/badge/UI-Avalonia-0F766E?style=flat-square)

---

## Why it exists

macOS “Storage” often looks full even when Downloads are empty. The real weight is usually developer caches, simulators leftovers, Xcode DerivedData, Gradle/CocoaPods, browser/Electron caches, and forgotten large installers — spread across the system Data volume and external disks.

MacSpaceCleaner is built for that reality: **whole machine**, not a single folder.

## Features

- **Multi-volume scan** — internal Data volume, `/Volumes/*`, and other mounted disks
- **Category scan** — Trash, user caches, logs, temp files, large Downloads, optional developer junk
- **File-level review** — path, size, checkbox, Reveal in Finder (`open -R`)
- **Smart ranking** — local heuristics score “safe to delete” candidates
- **Optional AI** — Groq (preferred) or OpenAI ranks the heaviest items
- **Safe by design** — no `sudo`, never touches `/System` or `/Applications`, always asks for confirmation
- **Clear result** — after cleanup, a **Deleted** success screen shows how much space was freed

## Requirements

- macOS
- [.NET 9 SDK](https://dotnet.microsoft.com/download)

If your system disk is nearly full, point NuGet/temp to an external drive:

```bash
export NUGET_PACKAGES=/Volumes/ADATA_SE880/nuget-packages
export TMPDIR=/Volumes/ADATA_SE880/tmp
```

## Run

```bash
cd /path/to/MacSpaceCleaner
dotnet run --project src/MacSpaceCleaner/MacSpaceCleaner.csproj -c Release
```

### Workflow

1. **Scan Mac** — measure volumes and cleanup categories  
2. **Next** — build the candidate list, run heuristics (+ AI if configured)  
3. Review / tweak selection (Recommended / All / None, Finder)  
4. **Clean selected…** — confirm, delete, see the **Deleted** summary  

## Optional AI keys

Local heuristics always work. For cloud ranking:

```bash
mkdir -p ~/.config/macspacecleaner

# Preferred (Groq free tier)
echo "gsk_..." > ~/.config/macspacecleaner/groq_api_key

# Or OpenAI
echo "sk-..." > ~/.config/macspacecleaner/openai_api_key
```

Environment variables also work: `GROQ_API_KEY`, `OPENAI_API_KEY`.  
Default Groq model: `openai/gpt-oss-20b` (override with `MACSPACECLEANER_GROQ_MODEL`). Deprecated models automatically fall back.

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
