# Packages the published tray + CLI binaries into a portable snapshot zip and a
# SHA256SUMS file. Used by both CI workflows (snapshot on every main push, tag
# releases) and by local snapshot builds.
#
#   .\eng\package-snapshot.ps1                          # version from Directory.Build.props, -> .\artifacts
#   .\eng\package-snapshot.ps1 -Version 0.2.0 -OutputDir .\artifacts

[CmdletBinding()]
param(
    [string]$Version = "",
    [string]$OutputDir = ""
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

if ([string]::IsNullOrWhiteSpace($Version)) {
    $props = [xml](Get-Content (Join-Path $root "Directory.Build.props"))
    $Version = $props.Project.PropertyGroup.Version
}
if ([string]::IsNullOrWhiteSpace($OutputDir)) {
    $OutputDir = Join-Path $root "artifacts"
}
New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null

$trayPublish = Join-Path $root "src\Beam.Tray\bin\Release\net10.0-windows\publish"
$cliPublish  = Join-Path $root "src\Beam.Cli\bin\Release\net10.0-windows\publish"
foreach ($dir in @($trayPublish, $cliPublish)) {
    if (-not (Test-Path (Join-Path $dir "*.exe"))) { throw "Publish output missing: $dir (run dotnet publish -c Release first)" }
}

# Merge both publish outputs into one staging dir (Beam.exe tray + Beam.Cli.exe CLI + shared DLLs).
$stage = Join-Path $env:TEMP "beam-snapshot-$Version"
Remove-Item $stage -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Path $stage -Force | Out-Null
Copy-Item "$trayPublish\*" -Destination $stage -Recurse -Force
Copy-Item "$cliPublish\*"  -Destination $stage -Recurse -Force

$zipName = "beam-$Version-portable.zip"
$zipPath = Join-Path $OutputDir $zipName
if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
Compress-Archive -Path "$stage\*" -DestinationPath $zipPath -CompressionLevel Optimal

# SHA256SUMS covers the portable zip and, when present in the same dir, the installer.
$sums = Join-Path $OutputDir "SHA256SUMS.txt"
$files = Get-ChildItem $OutputDir -File | Where-Object { $_.Name -ne "SHA256SUMS.txt" } | Sort-Object Name
$lines = foreach ($f in $files) {
    $hash = (Get-FileHash $f -Algorithm SHA256).Hash.ToLowerInvariant()
    "$hash  $($f.Name)"
}
Set-Content -Path $sums -Value $lines -Encoding ASCII

Write-Host "==> Snapshot files in $OutputDir"
Get-ChildItem $OutputDir -File | Select-Object Name, @{n="MB";e={[math]::Round($_.Length/1MB,2)}} | Format-Table -AutoSize | Out-String
Write-Host $zipName
