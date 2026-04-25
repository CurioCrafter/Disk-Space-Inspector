# Download Disk Space Inspector

The easiest way to try Disk Space Inspector is from the latest GitHub Release:

[Download the latest release](https://github.com/CurioCrafter/Disk-Space-Inspector/releases/latest)

## Recommended

Use `DiskSpaceInspectorSetup-1.0.0.exe` for a normal per-user install. It installs under your local app data programs folder and does not require admin rights.

## Portable

Use `DiskSpaceInspector-1.0.0-win-x64.zip` if you want to unzip and run the app without installing it.

## Trust Notes

- This stable build is unsigned, so Windows SmartScreen may warn.
- Disk Space Inspector does not collect telemetry.
- Disk Space Inspector does not use external advisor services or credential integrations.
- Scan data stays local in `%LOCALAPPDATA%\Disk Space Inspector`.
- Diagnostics exports are local files and redact user-profile paths by default.
- Cleanup is advisory and staged; this release does not directly delete files.

## Verify Checksums

Download `SHA256SUMS.txt` from the release and compare it with:

```powershell
Get-FileHash .\DiskSpaceInspectorSetup-1.0.0.exe -Algorithm SHA256
Get-FileHash .\DiskSpaceInspector-1.0.0-win-x64.zip -Algorithm SHA256
```
