# Download Disk Space Inspector

The easiest way to try Disk Space Inspector is from the latest GitHub Release:

[Download the latest release](https://github.com/CurioCrafter/Disk-Space-Inspector/releases/latest)

## Recommended

Use `DiskSpaceInspectorSetup-1.1.0.exe` for a normal per-user install. It installs under your local app data programs folder and does not require admin rights.

## Portable

Use `DiskSpaceInspector-1.1.0-win-x64.zip` if you want to unzip and run the app without installing it.

## What The App Does

Disk Space Inspector scans accessible drives, stores local snapshots, visualizes where space is used, explains relationships such as app-owned caches or generated project folders, and stages cleanup candidates for review.

It does not directly delete files in this release. Cleanup findings are advisory and should be reviewed carefully.

## Trust Notes

- This stable build is unsigned, so Windows SmartScreen may warn.
- Disk Space Inspector does not collect telemetry.
- Disk Space Inspector does not use external advisor services or credential integrations.
- Scan data stays local in `%LOCALAPPDATA%\Disk Space Inspector`.
- Diagnostics exports are local files and redact user-profile paths by default.
- Use at your own risk. Review paths and back up important files before taking cleanup action.

## Verify Checksums

Download `SHA256SUMS.txt` from the release and compare it with:

```powershell
Get-FileHash .\DiskSpaceInspectorSetup-1.1.0.exe -Algorithm SHA256
Get-FileHash .\DiskSpaceInspector-1.1.0-win-x64.zip -Algorithm SHA256
```
