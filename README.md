# Disk Space Inspector

Disk Space Inspector is a Windows-first storage visualizer and cleanup advisor. It scans drives, persists snapshots in SQLite, renders dense treemap/sunburst views, explains what owns space, and stages cleanup recommendations with conservative safety guardrails. The stable release adds a premium Visual Lab with 24+ deterministic storage analytics charts backed by scan data.

![Overview dashboard](docs/screenshots/overview.png)

## Download

Get the latest stable Windows build from GitHub Releases:

[Download Disk Space Inspector](https://github.com/CurioCrafter/Disk-Space-Inspector/releases/latest)

- Installer: `DiskSpaceInspectorSetup-1.0.0.exe`
- Portable ZIP: `DiskSpaceInspector-1.0.0-win-x64.zip`
- Checksums: `SHA256SUMS.txt`

The build is unsigned, so Windows SmartScreen may show a reputation warning. See [download notes](docs/download.md).

## What It Does

- Scans accessible Windows drives without requiring elevation for the basic pass.
- Shows a high-density Overview, Explore workspace, Visualize workspace, Cleanup advisor, Changes view, Insights view, Tutorials, and Privacy & Safety Center.
- Opens with a first-run welcome workspace and an in-app demo mode so people can try the visual workbench without scanning real drives.
- Renders squarified treemaps, sunburst hierarchy, file type breakdowns, age histograms, cleanup potential lanes, and a dedicated Visual Lab.
- Adds 24+ Visual Lab analytics: Pareto curves, heatmaps, scatter plots, radar charts, bubble packs, timeline/waterfall charts, ownership flows, cloud/local views, dependency-cache comparisons, and cleanup effort/value scoring.
- Summarizes a loaded scan with 5-minute findings: safe reclaimable space, top ownership signal, biggest growth, scan gaps, and local advisor candidates.
- Records scan gaps such as access-denied folders instead of silently hiding them.
- Classifies temp files, recycle bin content, browser/app caches, developer artifacts, downloads, package caches, Windows cleanup targets, large files, and blocked system paths.
- Exports local diagnostics reports with user-profile paths redacted by default. No telemetry is collected and no external advisor service is used.
- Includes in-app tutorials plus docs under `docs/tutorials/`.

## Screenshots

![First run welcome](docs/screenshots/first-run.png)

![5-minute findings](docs/screenshots/five-minute-findings.png)

![Visualizer workspace](docs/screenshots/visualize.png)

![Visual Lab](docs/screenshots/visual-lab.png)

![Cleanup advisor](docs/screenshots/cleanup.png)

![Insights and relationships](docs/screenshots/insights.png)

![Guided tutorials](docs/screenshots/tutorials.png)

![Privacy and Safety Center](docs/screenshots/privacy-safety.png)

## Projects

- `src/DiskSpaceInspector.App` - .NET 9 WPF desktop app.
- `src/DiskSpaceInspector.Core` - scanner, cleanup classifier, relationship detection, and visualization layout services.
- `src/DiskSpaceInspector.Storage` - SQLite snapshot persistence.
- `tests/DiskSpaceInspector.Tests` - scanner, classifier, layout, public launch, and storage tests.

## Run

```powershell
dotnet run --project src\DiskSpaceInspector.App\DiskSpaceInspector.App.csproj
```

Launch with seeded demo data for screenshots or UI review:

```powershell
dotnet run --project src\DiskSpaceInspector.App\DiskSpaceInspector.App.csproj -- --demo
```

Open a screenshot-friendly page directly:

```powershell
dotnet run --project src\DiskSpaceInspector.App\DiskSpaceInspector.App.csproj -- --demo --view=visual-lab
dotnet run --project src\DiskSpaceInspector.App\DiskSpaceInspector.App.csproj -- --demo --view=privacy
```

The app runs unelevated and records permission gaps instead of hiding them. Cleanup is staged for review only; this version does not directly delete files.

## Visual Lab

Visual Lab computes charts from the loaded scan rather than using static mock values. Demo mode seeds fake non-personal drives, package caches, WSL/Docker roots, cloud-linked folders, stale media, installer caches, duplicate-like archives, scan gaps, changes, and ownership evidence so every chart has meaningful data.

The default section shows curated best insights. The `Advanced algorithms` expander reveals deeper charts for ownership, package-manager footprint, system/dev roots, stale-data scoring, entropy, and risk lanes.

## Verify

```powershell
dotnet build DiskSpaceInspector.sln --no-restore -m:1 -v:minimal
dotnet test tests\DiskSpaceInspector.Tests\DiskSpaceInspector.Tests.csproj --no-restore -m:1 -v:minimal
```

Use `-m:1` on this machine to avoid transient project-reference file locks during WPF builds.

Create local release artifacts:

```powershell
.\scripts\package-release.ps1 -Version 1.0.0
```

The script always creates the portable ZIP. It also creates the installer when Inno Setup 6 is installed locally; GitHub Actions installs Inno Setup automatically for release builds.

## Tutorials

- [Download and first run](docs/tutorials/download-and-first-run.md)
- [First scan](docs/tutorials/first-scan.md)
- [Visual Lab](docs/tutorials/visual-lab.md)
- [Cleanup safety](docs/tutorials/cleanup-safety.md)

## License

Disk Space Inspector is not MIT licensed. It is owned by Andre and released under the custom Disk Space Inspector Source-Available Use License 1.0 in `LICENSE`.

In short: people may download, inspect, run, and use Disk Space Inspector, but they do not own it and may not claim ownership, relicense it, sell it, or distribute modified versions without Andre's written permission.
