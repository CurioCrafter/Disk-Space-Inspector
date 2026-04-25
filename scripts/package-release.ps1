param(
    [string]$Version = "0.5.0-preview.1",
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [switch]$SkipInstaller
)

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$publishDir = Join-Path $root "artifacts\publish\DiskSpaceInspector-$Version-$Runtime"
$releaseDir = Join-Path $root "artifacts\release"
$zipPath = Join-Path $releaseDir "DiskSpaceInspector-$Version-$Runtime.zip"
$checksumsPath = Join-Path $releaseDir "SHA256SUMS.txt"

New-Item -ItemType Directory -Force -Path $publishDir, $releaseDir | Out-Null

dotnet publish (Join-Path $root "src\DiskSpaceInspector.App\DiskSpaceInspector.App.csproj") `
    --configuration $Configuration `
    --runtime $Runtime `
    --self-contained true `
    -p:PublishSingleFile=false `
    -p:PublishReadyToRun=true `
    -p:Version=$Version `
    -p:InformationalVersion=$Version `
    --output $publishDir

Copy-Item (Join-Path $root "README.md") -Destination (Join-Path $publishDir "README.md") -Force
Copy-Item (Join-Path $root "LICENSE") -Destination (Join-Path $publishDir "LICENSE") -Force

if (Test-Path $zipPath) {
    Remove-Item $zipPath -Force
}

Compress-Archive -Path (Join-Path $publishDir "*") -DestinationPath $zipPath -Force

$installerPath = Join-Path $releaseDir "DiskSpaceInspectorSetup-$Version.exe"
if (-not $SkipInstaller) {
    $iscc = Get-Command iscc.exe -ErrorAction SilentlyContinue
    if ($iscc) {
        & $iscc.Source `
            "/DMyAppVersion=$Version" `
            "/DSourceDir=$publishDir" `
            "/DOutputDir=$releaseDir" `
            (Join-Path $root "installer\DiskSpaceInspector.iss")
    } else {
        Write-Warning "Inno Setup compiler not found. ZIP was created; install Inno Setup 6 or run CI to build the installer."
    }
}

$artifacts = Get-ChildItem $releaseDir -File |
    Where-Object { $_.Name -like "*.zip" -or $_.Name -like "*.exe" } |
    Sort-Object Name

$hashLines = foreach ($artifact in $artifacts) {
    $hash = Get-FileHash -Algorithm SHA256 -Path $artifact.FullName
    "$($hash.Hash.ToLowerInvariant())  $($artifact.Name)"
}

$hashLines | Set-Content -Path $checksumsPath -Encoding ASCII

Write-Host "Release artifacts:"
Get-ChildItem $releaseDir -File | Select-Object Name, Length, LastWriteTime
