#requires -Version 7.0
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false
$repoRoot = Split-Path -Parent $PSScriptRoot
Push-Location $repoRoot
try {
    dotnet build Beam.sln -c Release --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Release build failed.' }

    dotnet build eng/ProductionUiProbe/ProductionUiProbe.csproj -c Release --nologo
    if ($LASTEXITCODE -ne 0) { throw 'UI probe build failed.' }

    dotnet format Beam.sln --verify-no-changes --no-restore
    if ($LASTEXITCODE -ne 0) { throw 'Formatting verification failed.' }

    & (Join-Path $repoRoot 'eng\coverage.ps1') -NoTest -Enforce
    if ($LASTEXITCODE -ne 0) { throw 'Coverage gate failed.' }

    & (Join-Path $repoRoot 'eng\mutation-test.ps1') -MaxMutants 50
    if ($LASTEXITCODE -ne 0) { throw 'Mutation gate failed.' }

    & (Join-Path $repoRoot 'eng\check-links.ps1')
    if ($LASTEXITCODE -ne 0) { throw 'Documentation link check failed.' }

    Write-Output 'RELEASE QUALITY PASS'
}
finally {
    Pop-Location
}
