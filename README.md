# Disk Space Inspector

Disk Space Inspector is a Windows-first storage visualizer and cleanup advisor. It scans drives, persists snapshots in SQLite, renders dense treemap/sunburst views, explains what owns space, and stages cleanup recommendations with conservative safety guardrails.

![Overview dashboard](docs/screenshots/overview.png)

## What It Does

- Scans accessible Windows drives without requiring elevation for the basic pass.
- Shows a high-density Overview, Explore workspace, Visualize workspace, Cleanup advisor, Changes view, and Insights view.
- Renders squarified treemaps, sunburst hierarchy, file type breakdowns, age histograms, and cleanup potential lanes.
- Records scan gaps such as access-denied folders instead of silently hiding them.
- Classifies temp files, recycle bin content, browser/app caches, developer artifacts, downloads, package caches, Windows cleanup targets, large files, and blocked system paths.
- Uses Codex CLI ChatGPT login for optional AI cleanup recommendations without reading or storing Codex credentials.

## Screenshots

![Visualizer workspace](docs/screenshots/visualize.png)

![Cleanup advisor](docs/screenshots/cleanup.png)

## Projects

- `src/DiskSpaceInspector.App` - .NET 9 WPF desktop app.
- `src/DiskSpaceInspector.Core` - scanner, cleanup classifier, relationship detection, and visualization layout services.
- `src/DiskSpaceInspector.Storage` - SQLite snapshot persistence.
- `tests/DiskSpaceInspector.Tests` - scanner, classifier, layout, AI safety, and storage tests.

## Run

```powershell
dotnet run --project src\DiskSpaceInspector.App\DiskSpaceInspector.App.csproj
```

Launch with seeded demo data for screenshots or UI review:

```powershell
dotnet run --project src\DiskSpaceInspector.App\DiskSpaceInspector.App.csproj -- --demo
```

The app runs unelevated and records permission gaps instead of hiding them. Cleanup is staged for review only; this version does not directly delete files.

## Codex AI Cleanup Advisor

Disk Space Inspector can ask Codex to rank and explain cleanup candidates after a scan. The app delegates sign-in to the Codex CLI so you can use ChatGPT/Codex OAuth instead of pasting an API key.

```powershell
npm i -g @openai/codex
codex login
```

In the Cleanup tab, use `Login with Codex`, `Check Codex status`, then `Ask Codex AI`. Disk Space Inspector never reads or stores `~/.codex/auth.json`; Codex owns OAuth, credential caching, and model execution through `codex login status` and `codex exec`.

AI recommendations are advisory. They can only reference paths already found by Disk Space Inspector, and blocked/system cleanup findings cannot be converted into executable cleanup actions by Codex.

## Verify

```powershell
dotnet build DiskSpaceInspector.sln --no-restore -m:1 -v:minimal
dotnet test tests\DiskSpaceInspector.Tests\DiskSpaceInspector.Tests.csproj --no-restore -m:1 -v:minimal
```

Use `-m:1` on this machine to avoid transient project-reference file locks during WPF builds.

## License

Disk Space Inspector is not MIT licensed. It is owned by Andre and released under the custom Disk Space Inspector Source-Available Use License 1.0 in `LICENSE`.

In short: people may download, inspect, run, and use Disk Space Inspector, but they do not own it and may not claim ownership, relicense it, sell it, or distribute modified versions without Andre's written permission.
