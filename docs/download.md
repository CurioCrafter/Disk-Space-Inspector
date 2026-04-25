# Download Disk Space Inspector

The easiest way to try Disk Space Inspector is from the latest GitHub Release:

[Download the latest release](https://github.com/CurioCrafter/Disk-Space-Inspector/releases/latest)

## Recommended

Use `DiskSpaceInspectorSetup-0.5.0-preview.1.exe` for a normal per-user install. It installs under your local app data programs folder and does not require admin rights.

## Portable

Use `DiskSpaceInspector-0.5.0-preview.1-win-x64.zip` if you want to unzip and run the app without installing it.

## Trust Notes

- Preview builds are unsigned, so Windows SmartScreen may warn.
- Disk Space Inspector does not collect telemetry.
- Scan data stays local in `%LOCALAPPDATA%\Disk Space Inspector`.
- Diagnostics exports are local files and redact user-profile paths by default.
- Cleanup is advisory and staged; this preview does not directly delete files.

## Verify Checksums

Download `SHA256SUMS.txt` from the release and compare it with:

```powershell
Get-FileHash .\DiskSpaceInspectorSetup-0.5.0-preview.1.exe -Algorithm SHA256
Get-FileHash .\DiskSpaceInspector-0.5.0-preview.1-win-x64.zip -Algorithm SHA256
```
