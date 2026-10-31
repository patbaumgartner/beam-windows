# Builds the Beam installer locally.
#
#   .\installer\build.ps1            # publish + compile installer to .\artifacts
#   .\installer\build.ps1 -Version 0.2.0 -OutputDir C:\temp\out
#
# Requires: .NET 10 SDK (see global.json) and Inno Setup 6.

[CmdletBinding()]
param(
    [string]$Version = "0.1.0",
    [string]$OutputDir = ""
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

# .NET 10 SDK may be installed per-user (no admin on this machine).
$dotnet = "dotnet"
if ($env:LOCALAPPDATA -and (Test-Path "$env:LOCALAPPDATA\Microsoft\dotnet\dotnet.exe")) {
    $dotnet = "$env:LOCALAPPDATA\Microsoft\dotnet\dotnet.exe"
}

Write-Host "==> Publishing Beam.Tray (version $Version)"
& $dotnet publish src\Beam.Tray -c Release -p:Version=$Version
if ($LASTEXITCODE -ne 0) { throw "dotnet publish Beam.Tray failed" }

Write-Host "==> Publishing Beam.Cli (version $Version)"
& $dotnet publish src\Beam.Cli -c Release -p:Version=$Version
if ($LASTEXITCODE -ne 0) { throw "dotnet publish Beam.Cli failed" }

# Locate ISCC.exe (Inno Setup 6).
$iscc = @(
    "$env:ProgramFiles(x86)\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1

if (-not $iscc) {
    throw "Inno Setup 6 not found. Install it (e.g. winget install JRSoftware.InnoSetup)."
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
