#requires -Version 5.1
<#
.SYNOPSIS
    Runs the test suite with coverage and reports per-assembly line coverage.

.DESCRIPTION
    Unions the Cobertura reports produced by every test project (a line counts as covered
    if any test project exercised it), prints a per-assembly table, and optionally fails
    when an assembly is below its threshold.

    The gate deliberately covers the portable logic (Beam.Core, Beam.App) and the CLI
    command layer. Beam.Windows and Beam.Tray are thin OS adapters: their interesting code
    needs a real graphics stack, so they are reported for visibility but not gated.

.PARAMETER NoTest
    Reuse the coverage reports already under tests\**\TestResults instead of re-running.

.PARAMETER Enforce
    Exit non-zero when a gated assembly is below its threshold.

.EXAMPLE
    pwsh eng/coverage.ps1
    pwsh eng/coverage.ps1 -Enforce
#>
[CmdletBinding()]
param(
    [switch]$NoTest,
    [switch]$Enforce
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
Push-Location $repoRoot
try {
    if (-not $NoTest) {
        Get-ChildItem tests -Recurse -Directory -Filter TestResults -ErrorAction SilentlyContinue |
            Remove-Item -Recurse -Force -ErrorAction SilentlyContinue

        dotnet test Beam.sln -c Release --collect:"XPlat Code Coverage" --verbosity quiet
        if ($LASTEXITCODE -ne 0) {
            throw "dotnet test failed with exit code $LASTEXITCODE"
        }
    }

    $reports = Get-ChildItem tests -Recurse -Filter coverage.cobertura.xml |
        Where-Object { $_.FullName -match 'TestResults' }

    if (-not $reports) {
        throw 'No coverage reports found. Run without -NoTest first.'
    }

    # class name + line number -> best hit count seen across all reports
    $lineHits = @{}
    foreach ($report in $reports) {
        [xml]$xml = Get-Content -Raw $report.FullName
        foreach ($class in $xml.SelectNodes('//class')) {
            $className = $class.GetAttribute('name')
            foreach ($line in $class.SelectNodes('.//line')) {
                $key = $className + '|' + $line.GetAttribute('number')
                $hits = [int]$line.GetAttribute('hits')
                if (-not $lineHits.ContainsKey($key) -or $lineHits[$key] -lt $hits) {
                    $lineHits[$key] = $hits
                }
            }
        }
    }

    $assemblies = @('Beam.Core', 'Beam.App', 'Beam.Cli', 'Beam.Windows', 'Beam.Tray')
    $stats = @{}
    foreach ($name in $assemblies) {
        $stats[$name] = [pscustomobject]@{ Covered = 0; Total = 0 }
    }

    foreach ($key in $lineHits.Keys) {
        $className = $key.Split('|')[0]
        if ($className -match '\.Tests\.') { continue }

        $parts = $className.Split('.')
        if ($parts.Length -lt 2) { continue }

        $assembly = $parts[0] + '.' + $parts[1]
        if (-not $stats.ContainsKey($assembly)) { continue }

        $stats[$assembly].Total++
        if ($lineHits[$key] -gt 0) {
            $stats[$assembly].Covered++
        }
    }

    $thresholds = @{ 'Beam.Core' = 85.0; 'Beam.App' = 80.0; 'Beam.Cli' = 75.0 }

    Write-Output ''
    Write-Output 'Line coverage (union of all test projects)'
    Write-Output '-------------------------------------------'
    $totalCovered = 0
    $totalLines = 0
    $failures = @()

    foreach ($name in $assemblies) {
        $s = $stats[$name]
        $totalCovered += $s.Covered
        $totalLines += $s.Total

        if ($s.Total -eq 0) {
            Write-Output ('{0,-14} {1,8}' -f $name, 'no data')
            continue
        }

        $percent = 100.0 * $s.Covered / $s.Total
        $gate = ''
        if ($thresholds.ContainsKey($name)) {
            $gate = ' (gate {0:N0}%)' -f $thresholds[$name]
            if ($percent -lt $thresholds[$name]) {
                $gate += ' BELOW'
                $failures += ('{0}: {1:N1}% < {2:N0}%' -f $name, $percent, $thresholds[$name])
            }
        }
        else {
            $gate = ' (adapter; not gated)'
        }

        Write-Output ('{0,-14} {1,5}/{2,-5} {3,6:N1}%{4}' -f $name, $s.Covered, $s.Total, $percent, $gate)
    }

    if ($totalLines -gt 0) {
        Write-Output '-------------------------------------------'
        Write-Output ('{0,-14} {1,5}/{2,-5} {3,6:N1}%' -f 'Overall', $totalCovered, $totalLines, (100.0 * $totalCovered / $totalLines))
    }

    if ($failures.Count -gt 0) {
        Write-Output ''
        foreach ($failure in $failures) {
            Write-Output ('coverage gate failed: ' + $failure)
        }

        if ($Enforce) {
            exit 1
        }
    }
}
finally {
    Pop-Location
}
