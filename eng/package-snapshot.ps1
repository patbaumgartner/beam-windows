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
$expected = @(
    @{ Dir = $trayPublish; Exe = "Beam.exe" },
    @{ Dir = $cliPublish; Exe = "Beam.Cli.exe" }
)
foreach ($item in $expected) {
    $exe = Join-Path $item.Dir $item.Exe
    if (-not (Test-Path $exe)) { throw "Publish output missing: $exe (run the self-contained publish first)" }
    foreach ($runtimeFile in @("coreclr.dll", "hostfxr.dll", "hostpolicy.dll")) {
        if (-not (Test-Path (Join-Path $item.Dir $runtimeFile))) {
            throw "$($item.Dir) is not a self-contained publish (missing $runtimeFile)"
        }
    }
    $productVersion = (Get-Item $exe).VersionInfo.ProductVersion
    if ($productVersion -ne $Version -and $productVersion -notlike "$Version+*") {
        throw "$exe has product version '$productVersion', expected '$Version'"
    }
}

# Merge fresh outputs into a clean staging dir (Beam.exe tray + Beam.Cli.exe CLI + shared DLLs).
$stage = Join-Path $OutputDir ".beam-stage-$Version"
Remove-Item $stage -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Path $stage -Force | Out-Null
Copy-Item "$trayPublish\*" -Destination $stage -Recurse -Force
Copy-Item "$cliPublish\*"  -Destination $stage -Recurse -Force
Copy-Item (Join-Path $root "LICENSE") -Destination (Join-Path $stage "LICENSE.txt")
Copy-Item (Join-Path $root "assets\ATTRIBUTION.md") -Destination (Join-Path $stage "THIRD-PARTY-NOTICES.txt")
Copy-Item (Join-Path $root "installer\beam.cmd") -Destination (Join-Path $stage "beam.cmd")

$zipName = "beam-$Version-portable.zip"
$zipPath = Join-Path $OutputDir $zipName
if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
try {
    Compress-Archive -Path "$stage\*" -DestinationPath $zipPath -CompressionLevel Optimal
}
finally {
    Remove-Item $stage -Recurse -Force -ErrorAction SilentlyContinue
}

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
