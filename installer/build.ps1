# Builds the Beam installer locally.
#
#   .\installer\build.ps1            # publish + compile installer to .\artifacts
#   .\installer\build.ps1 -Version 0.2.0 -OutputDir C:\temp\out
#
# Requires: .NET 10 SDK (see global.json) and Inno Setup 6.3+.

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

# .NET 10 SDK may be installed per-user (no admin on this machine).
$dotnet = "dotnet"
if ($env:LOCALAPPDATA -and (Test-Path "$env:LOCALAPPDATA\Microsoft\dotnet\dotnet.exe")) {
    $dotnet = "$env:LOCALAPPDATA\Microsoft\dotnet\dotnet.exe"
}

$publishDirs = @(
    (Join-Path $root "src\Beam.Tray\bin\Release\net10.0-windows\publish"),
    (Join-Path $root "src\Beam.Cli\bin\Release\net10.0-windows\publish")
)
foreach ($dir in $publishDirs) {
    if (Test-Path $dir) { Remove-Item $dir -Recurse -Force }
}

Write-Host "==> Publishing self-contained Beam.Tray (version $Version)"
& $dotnet publish src\Beam.Tray -c Release -r win-x64 --self-contained true -p:Version=$Version -o $publishDirs[0]
if ($LASTEXITCODE -ne 0) { throw "dotnet publish Beam.Tray failed" }

Write-Host "==> Publishing self-contained Beam.Cli (version $Version)"
& $dotnet publish src\Beam.Cli -c Release -r win-x64 --self-contained true -p:Version=$Version -o $publishDirs[1]
if ($LASTEXITCODE -ne 0) { throw "dotnet publish Beam.Cli failed" }

# Locate ISCC.exe (Inno Setup 7 preferred, 6.3+ supported).
$iscc = @(
    "${env:ProgramFiles(x86)}\Inno Setup 7\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 7\ISCC.exe",
    "$env:LOCALAPPDATA\Programs\Inno Setup 7\ISCC.exe",
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1

if (-not $iscc) {
    throw "Inno Setup 6.3+ not found. Install it (e.g. winget install JRSoftware.InnoSetup)."
}

if ([string]::IsNullOrWhiteSpace($OutputDir)) {
    $OutputDir = Join-Path $root "artifacts"
}
New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null

Write-Host "==> Compiling installer with $iscc"
& $iscc "/DMyAppVersion=$Version" "/DOutputDir=$OutputDir" (Join-Path $root "installer\Beam.iss")
if ($LASTEXITCODE -ne 0) { throw "ISCC failed" }

$built = Get-ChildItem (Join-Path $OutputDir "BeamSetup-$Version.exe")
Write-Host ""
Write-Host "Installer ready: $($built.FullName) ($([math]::Round($built.Length / 1MB, 2)) MB)"
