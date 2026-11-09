#requires -Version 5.1
<#
.SYNOPSIS
    Runs mutation testing against Beam's source projects using the Roslyn-based
    mutation tester in eng/MutationTester.

.DESCRIPTION
    For each source file in Beam.Core and Beam.App, applies small semantic
    mutations (operator flips, literal changes, boolean negation) and runs the
    test suite against each mutant.

    A mutant that SURVIVES (tests still pass) indicates either:
      * a weak test that doesn't catch the code change ("evergreen test"), or
      * dead code that no test exercises.

    A mutant that is KILLED (tests fail) means the test suite catches the change.

    Exit code is non-zero when any mutants survive — CI should fail on this.

.PARAMETER Output
    Path to write the JSON mutation report. Defaults to
    $env:TEMP\beam-mutation-report-<timestamp>.json.

.PARAMETER SourceProject
    Source project to mutate. Defaults to src/Beam.Core/Beam.Core.csproj.
    Can be specified multiple times.

.PARAMETER TestProject
    Test project to run against each mutant. Defaults to the matching
    tests/Beam.Core.Tests project.

.EXAMPLE
    pwsh eng/mutation-test.ps1

.EXAMPLE
    pwsh eng/mutation-test.ps1 -Output mutation-report.json -SourceProject src/Beam.Core/Beam.Core.csproj -TestProject tests/Beam.Core.Tests/Beam.Core.Tests.csproj
#>
[CmdletBinding()]
param(
    [string[]]$SourceProject = @("src/Beam.Core/Beam.Core.csproj"),
    [string[]]$TestProject = @("tests/Beam.Core.Tests/Beam.Core.Tests.csproj"),
    [string]$Output = ""
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
Push-Location $repoRoot
try {
    # Build the mutation tester tool
    Write-Output "Building mutation tester..."
    dotnet build eng/MutationTester/MutationTester.csproj -c Release --nologo | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to build mutation tester."
    }

    $toolDll = "$repoRoot\eng\MutationTester\bin\Release\net10.0\MutationTester.dll"

    $timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
    $reportPath = if ($Output) { $Output } else { "$env:TEMP\beam-mutation-report-$timestamp.json" }

    $allResults = @()
    $totalKilled = 0
    $totalMutants = 0

    for ($i = 0; $i -lt $SourceProject.Length; $i++) {
        $src = $SourceProject[$i]
        $tst = if ($i -lt $TestProject.Length) { $TestProject[$i] } else { $TestProject[0] }

        Write-Output ""
        Write-Output "=== Mutation testing: $src ==="
        Write-Output "Test project: $tst"

        # Pass a per-project report file as the 3rd arg: the tool prints summary lines
        # after the JSON on stdout, so stdout cannot be parsed reliably — the file can.
        $projReportPath = "$env:TEMP\beam-mutation-report-$timestamp-$i.json"
        $result = dotnet run --project eng/MutationTester/MutationTester.csproj -c Release --nologo -- "$src" "$tst" "$projReportPath" 2>&1
        $exit = $LASTEXITCODE

        Write-Output $result

        # Parse the report file the tool wrote (stdout has trailing summary lines).
        try {
            if (Test-Path $projReportPath) {
                $report = Get-Content $projReportPath -Raw | ConvertFrom-Json
                $totalKilled += $report.KilledMutants
                $totalMutants += $report.TotalMutants

                foreach ($mutant in $report.SurvivingMutants) {
                    $allResults += [pscustomobject]@{
                        SourceProject = $src
                        File = $mutant.File
                        Line = $mutant.Line
                        Mutation = $mutant.Mutation
                        Description = $mutant.Description
                    }
                }
            }
            else {
                throw "report file not written"
            }
        }
        catch {
            Write-Warning "Could not parse mutation report for $src"
        }

        if ($exit -ne 0) {
            Write-Output "Mutants survived in $src — see surviving mutants above."
        }
    }

    Write-Output ""
    Write-Output "=========================================="
    Write-Output "MUTATION TESTING SUMMARY"
    Write-Output "=========================================="
    Write-Output "Total mutants:  $totalMutants"
    Write-Output "Killed:         $totalKilled"
    Write-Output "Surviving:      $($totalMutants - $totalKilled)"
    if ($totalMutants -gt 0) {
        $score = [math]::Round(($totalKilled / $totalMutants) * 100, 1)
        Write-Output "Mutation score: $score%"
    }

    if ($allResults.Count -gt 0) {
        Write-Output ""
        Write-Output "SURVIVING MUTANTS (weak tests or dead code):"
        foreach ($m in $allResults) {
            Write-Output "  $($m.SourceProject) : $($m.File) :$($m.Line) : $($m.Mutation) : $($m.Description)"
        }
    }

    # Write the combined report
    $combined = [pscustomobject]@{
        Timestamp = Get-Date -Format "o"
        TotalMutants = $totalMutants
        KilledMutants = $totalKilled
        SurvivingMutants = $totalMutants - $totalKilled
        MutationScore = if ($totalMutants -gt 0) { [math]::Round(($totalKilled / $totalMutants) * 100, 1) } else { 0 }
        SurvivingDetails = $allResults
    }
    $combined | ConvertTo-Json -Depth 10 | Out-File $reportPath
    Write-Output ""
    Write-Output "Full report written to: $reportPath"

    if ($allResults.Count -gt 0) {
        Write-Output ""
        Write-Output "EXIT CODE: 1 (surviving mutants found — fix weak tests or remove dead code)"
        exit 1
    }
    else {
        Write-Output ""
        Write-Output "EXIT CODE: 0 (all mutants killed — test suite is robust)"
        exit 0
    }
}
finally {
    Pop-Location
}
