# MacSpaceCleaner

**See what’s eating your Mac — then free it, safely.**

MacSpaceCleaner is a native macOS desktop app that scans **every mounted disk** on your computer, finds reclaimable junk (caches, logs, temp files, developer leftovers, oversized downloads), lets you review each path, and deletes **only** what you confirm.

No mystery “one-click wipe.” You always see the files first.

![.NET](https://img.shields.io/badge/.NET-9-512BD4?style=flat-square)
![Platform](https://img.shields.io/badge/platform-macOS-111111?style=flat-square)
![UI](https://img.shields.io/badge/UI-Avalonia-0F766E?style=flat-square)
![Safety](https://img.shields.io/badge/delete-confirm%20first-067647?style=flat-square)

---

## The problem

Your Mac says storage is almost full, but Downloads look empty and you didn’t install anything huge.

What usually fills the drive is invisible day-to-day:

- App and browser caches  
- Logs and temporary files  
- Trash on internal **and** external disks  
- Xcode DerivedData, Gradle, CocoaPods, Flutter/Dart caches  
- Old installers and large forgotten files  

That junk sits on the **system Data volume** and on **any USB / external drive** you plug in. Finder’s storage view rarely explains it clearly.

## What MacSpaceCleaner does

| Step | What you get |
|------|----------------|
| **1. Scan** | Lists all volumes (Macintosh HD, Data, every disk under `/Volumes`) with used / free space |
| **2. Categories** | Measures Trash, user caches, logs, temp files, large Downloads, optional **Dev junk** |
| **3. Review** | Builds a file-level list with sizes, scores, and reasons |
| **4. Decide** | Toggle whole rows, open **Finder** on any path, pick Recommended / All / None |
| **5. Clean** | Confirm once → delete → see a clear **Deleted** summary of space freed |

Optional **AI ranking** (Groq or OpenAI) can highlight the heaviest, safest-to-remove items. Local heuristics always work even without an API key.

---

## Highlights

- **All disks, any user** — not tied to one brand of SSD or one folder  
- **Whole-machine view** — internal storage + externals under `/Volumes`  
- **Clickable category cards** — tap the whole row to select / deselect  
- **Reveal in Finder** — jump to the exact file before you delete  
- **Smart recommendations** — heuristics (+ optional cloud AI)  
- **Safe defaults** — no `sudo`, no `/System`, no `/Applications`, always confirm  
- **One-click launch** — double-click the app or `.command` file  

---

## Quick start

### Easiest: open like a normal Mac app

1. Build once (only needed the first time, or after code changes):

```bash
./scripts/build-app.sh
```

2. Double-click either:

- **`MacSpaceCleaner.command`** — one-click launcher  
- **`dist/MacSpaceCleaner.app`** — standard macOS application  

You can drag `MacSpaceCleaner.app` to the Dock or Desktop.

### From Terminal (optional)

```bash
cd /path/to/MacSpaceCleaner
dotnet run --project src/MacSpaceCleaner/MacSpaceCleaner.csproj -c Release
```

Or after publish:

```bash
open dist/MacSpaceCleaner.app
```

---

## How to use it

1. Click **Scan Mac** — volumes and category sizes fill in  
2. Turn on **Dev junk** if you want developer caches included  
3. Click **Next** — review recommended files and folders  
4. Adjust selection (Recommended / All / None, or click each card)  
5. Use **Finder** on anything you’re unsure about  
6. Click **Clean selected…** → confirm → read the **Deleted** result  

---

## Requirements

- macOS 13+  
- [.NET 9 SDK](https://dotnet.microsoft.com/download) (to build; the published `.app` is self-contained)

### If your system disk is almost full

`dotnet restore` may fail when the Mac has no free space. Point NuGet/temp at **any** disk with room (example — use *your* mount path):

```bash
export NUGET_PACKAGES="/Volumes/YourExternalDisk/nuget-packages"
export TMPDIR="/Volumes/YourExternalDisk/tmp"
mkdir -p "$NUGET_PACKAGES" "$TMPDIR"
```

This only affects building. The app itself always scans **all** mounted volumes.

---

## Optional AI (cloud ranking)

Local scoring always runs. For extra AI ranking of the largest items:

```bash
mkdir -p ~/.config/macspacecleaner

# Preferred — Groq free tier
echo "gsk_..." > ~/.config/macspacecleaner/groq_api_key

# Or OpenAI
echo "sk-..." > ~/.config/macspacecleaner/openai_api_key
```

Also supported: `GROQ_API_KEY`, `OPENAI_API_KEY`.  
Default Groq model: `openai/gpt-oss-20b` (override with `MACSPACECLEANER_GROQ_MODEL`).

---

## Project layout

```
MacSpaceCleaner.command       Double-click launcher
scripts/build-app.sh          Builds dist/MacSpaceCleaner.app
src/MacSpaceCleaner/          Avalonia desktop UI
src/MacSpaceCleaner.Core/     Volume scan, categories, AI/heuristics, safe delete
dist/MacSpaceCleaner.app      Published macOS app (after build)
```

---

## Safety

- Deletes are **permanent** (not always recoverable from Trash)  
- Developer caches come back on the next build — that’s expected  
- Inspect unfamiliar paths with **Finder** before cleaning  
- Keep a few GB free so the system and tooling can breathe  

---

## License

Private repository. All rights reserved unless otherwise stated by the owner.
