#requires -Version 7.0
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateSet('Hardware', 'NightLight', 'Cli', 'FinalState')]
    [string]$Suite
)

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false
$repoRoot = Split-Path -Parent $PSScriptRoot
$evidenceDir = Join-Path $repoRoot 'artifacts\production-tests'
$cli = Join-Path $repoRoot 'src\Beam.Cli\bin\Release\net10.0-windows\Beam.Cli.exe'
$tray = Join-Path $repoRoot 'src\Beam.Tray\bin\Release\net10.0-windows\Beam.exe'
$coreAssembly = Join-Path $repoRoot 'src\Beam.Core\bin\Release\net10.0\Beam.Core.dll'
$windowsAssembly = Join-Path $repoRoot 'src\Beam.Windows\bin\Release\net10.0-windows\Beam.Windows.dll'
$beamDir = Join-Path $env:LOCALAPPDATA 'Beam'
$settingsPath = Join-Path $beamDir 'settings.json'
$journalPath = Join-Path $beamDir 'journal.json'
$logPath = Join-Path $beamDir 'beam.log'
New-Item -ItemType Directory -Path $evidenceDir -Force | Out-Null

function Assert-True([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw $Message }
}

function Wait-For([scriptblock]$Condition, [string]$Message, [int]$Attempts = 100) {
    for ($i = 0; $i -lt $Attempts; $i++) {
        $value = & $Condition
        if ($value) { return $value }
        Start-Sleep -Milliseconds 50
    }
    throw $Message
}

function Invoke-Beam {
    param(
        [Parameter(ValueFromRemainingArguments)]
        [string[]]$Arguments,
        [int]$ExpectedExit = 0,
        [switch]$Json
    )

    $output = (& $cli @Arguments 2>&1 | Out-String).Trim()
    $exitCode = $LASTEXITCODE
    Assert-True ($exitCode -eq $ExpectedExit) "beam $($Arguments -join ' ') exited $exitCode, expected $ExpectedExit. Output: $output"
    $parsed = $null
    if ($Json) {
        try { $parsed = $output | ConvertFrom-Json -Depth 30 }
        catch { throw "beam $($Arguments -join ' ') did not return valid JSON: $output" }
    }

    return [pscustomobject]@{ Output = $output; ExitCode = $exitCode; Json = $parsed }
}

function Get-LiveStatus {
    return (Invoke-Beam -Arguments @('status', '--json') -Json).Json
}

function Save-RunBaseline {
    $offsetPath = Join-Path $evidenceDir 'log-offset.txt'
    if (-not (Test-Path $offsetPath)) {
        $length = (Get-Item $logPath -ErrorAction SilentlyContinue).Length
        if ($null -eq $length) { $length = 0 }
        $length | Set-Content $offsetPath
    }

    $settingsBaseline = Join-Path $evidenceDir 'settings-baseline.json'
    if ((Test-Path $settingsPath) -and -not (Test-Path $settingsBaseline)) {
        Copy-Item $settingsPath $settingsBaseline -Force
    }
}

function Import-WindowsAdapters {
    if (-not ('Beam.Windows.Display.DisplayInventory' -as [type])) {
        Add-Type -Path $coreAssembly
        Add-Type -Path $windowsAssembly
    }
}

function Get-DisplayFingerprint($Status) {
    Import-WindowsAdapters
    $items = @()
    foreach ($display in $Status.displays) {
        $devMode = [Beam.Windows.Display.DisplayInventory]::GetCurrentDevMode($display.gdi)
        $frequency = $null -eq $devMode ? 0 : $devMode.dmDisplayFrequency
        $dpiPercent = 0
        if ($null -ne $display.dpi) {
            $dpiPercent = [Beam.Core.Display.DpiScale]::From(
                $display.dpi.minRel, $display.dpi.currentRel, $display.dpi.maxRel).Value.Current
        }
        $items += "$($display.gdi)|$($display.source)|$($display.signal)|hz=$frequency|dpi=$dpiPercent"
    }
    return $items | Sort-Object
}

function Save-NightLightRaw($Controller, [string]$Name = 'nightlight-raw-baseline.json') {
    $records = @($Controller.ReadAll() | Where-Object { $null -ne $_.RawBytes } | ForEach-Object {
        [pscustomobject]@{
            KeyPath = $_.KeyPath
            ValueName = $_.ValueName
            Bytes = [Convert]::ToBase64String($_.RawBytes)
            Active = $_.State.Active
            TransitionCause = [string]$_.State.TransitionCause
        }
    })
    $records | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $evidenceDir $Name)
    return $records
}

function Restore-NightLightRaw($Records) {
    foreach ($record in @($Records)) {
        $key = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($record.KeyPath, $true)
        if ($null -ne $key) {
            try { $key.SetValue($record.ValueName, [Convert]::FromBase64String($record.Bytes), [Microsoft.Win32.RegistryValueKind]::Binary) }
            finally { $key.Dispose() }
        }
    }
}

function Assert-NightLightState($Controller, $Snapshots) {
    $current = @($Controller.ReadAll())
    Assert-True (@($current | Where-Object { $_.Error }).Count -eq 0) 'A Night Light value is unreadable after restore.'
    foreach ($snapshot in @($Snapshots)) {
        $entry = $current | Where-Object { $_.KeyPath -eq $snapshot.KeyPath } | Select-Object -First 1
        Assert-True ($null -ne $entry -and $null -ne $entry.State) "Missing restored Night Light entry: $($snapshot.KeyPath)"
        Assert-True ($entry.State.Active -eq $snapshot.Blob.State.Active) "Night Light active state differs for $($snapshot.KeyPath)"
        Assert-True ($entry.State.TransitionCause -eq $snapshot.Blob.State.TransitionCause) "Night Light transition cause differs for $($snapshot.KeyPath)"
    }
}

function Invoke-NightLightProduction {
    Import-WindowsAdapters
    $controller = [Beam.Windows.NightLight.NightLightController]::new()
    $raw = Save-NightLightRaw $controller
    try {
        $snapshots = $controller.SnapshotAll()
        Assert-True ($snapshots.Count -gt 0) 'No parsable Night Light snapshots were found.'

        $off = $controller.SetActive($false)
        Assert-True $off.Success "Night Light off failed: $($off.Details -join ' | ')"
        Assert-True (@($controller.ReadAll() | Where-Object { $_.State.Active }).Count -eq 0) 'Night Light remained active on at least one value.'

        $restored = $controller.Restore($snapshots)
        Assert-True $restored.Success "Night Light restore failed: $($restored.Details -join ' | ')"
        Assert-NightLightState $controller $snapshots
        Write-Output "Night Light values verified: $($snapshots.Count)"
    }
    finally {
        Restore-NightLightRaw $raw
    }
}

function Send-BeamHotkey {
    if (-not ('BeamProductionKeys' -as [type])) {
        Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class BeamProductionKeys {
    [DllImport("user32.dll")] private static extern void keybd_event(byte key, byte scan, uint flags, UIntPtr extra);
    public static void Toggle() {
        const uint up = 0x0002;
        keybd_event(0x5B, 0, 0, UIntPtr.Zero);
        keybd_event(0x12, 0, 0, UIntPtr.Zero);
        keybd_event(0x50, 0, 0, UIntPtr.Zero);
        keybd_event(0x50, 0, up, UIntPtr.Zero);
        keybd_event(0x12, 0, up, UIntPtr.Zero);
        keybd_event(0x5B, 0, up, UIntPtr.Zero);
    }
}
'@
    }
    [BeamProductionKeys]::Toggle()
}

function Wait-Session([string]$State, [int]$Attempts = 120) {
    for ($i = 0; $i -lt $Attempts; $i++) {
        Start-Sleep -Milliseconds 250
        $status = Get-LiveStatus
        if ($status.session -and $status.session.state -eq $State) { return $status }
    }
    $last = Get-LiveStatus | ConvertTo-Json -Depth 10 -Compress
    throw "Session did not reach $State. Last status: $last"
}

function Set-DisplayTopology([ValidateSet('clone', 'extend', 'internal')][string]$Topology) {
    & "$env:WINDIR\System32\DisplaySwitch.exe" "/$Topology"
    Assert-True ($LASTEXITCODE -eq 0) "DisplaySwitch /$Topology failed with exit code $LASTEXITCODE."
}

function Wait-DisplayCount([int]$Count, [int]$Attempts = 80) {
    for ($i = 0; $i -lt $Attempts; $i++) {
        Start-Sleep -Milliseconds 250
        $status = Get-LiveStatus
        if (@($status.displays).Count -eq $Count) { return $status }
    }
    throw "Display count did not reach $Count."
}

function Restore-ActiveSession {
    $status = Get-LiveStatus
    if ($status.session -and $status.session.state -in @('Active', 'Applying', 'Detecting')) {
        Send-BeamHotkey
        Wait-Session 'Idle' | Out-Null
    }
    elseif ($status.session -and $status.session.state -eq 'Restoring') {
        Wait-Session 'Idle' | Out-Null
    }
}

function Assert-DisplayBaseline($Baseline) {
    $current = Get-LiveStatus
    Assert-True (-not $current.mirrored) 'Display topology is still mirrored after Undo.'
    $fingerprintPath = Join-Path $evidenceDir 'display-fingerprint-baseline.json'
    if (Test-Path $fingerprintPath) {
        $before = @(Get-Content $fingerprintPath | Where-Object { $_ -match '\S' })
    }
    else {
        $before = @(Get-DisplayFingerprint $Baseline)
    }
    $after = @(Get-DisplayFingerprint $current)
    Assert-True (($before -join ';') -eq ($after -join ';')) "Display modes/DPI/refresh differ after restore. Before=$($before -join ';') After=$($after -join ';')"
}

function Wait-LogContains([long]$Offset, [string]$Text) {
    Wait-For {
        if (-not (Test-Path $logPath)) { return $false }
        $stream = [System.IO.File]::Open($logPath, 'Open', 'Read', 'ReadWrite')
        try {
            $stream.Position = [Math]::Min($Offset, $stream.Length)
            $reader = [System.IO.StreamReader]::new($stream)
            try { return $reader.ReadToEnd().Contains($Text) } finally { $reader.Dispose() }
        }
        finally { $stream.Dispose() }
    } "Log did not contain expected text: $Text" | Out-Null
}

function Invoke-HardwareProduction {
    Assert-True ((Test-Path $cli) -and (Test-Path $tray)) 'Release binaries are missing; run dotnet build Beam.sln -c Release first.'
    $initial = Get-LiveStatus
    Assert-True (@($initial.displays | Where-Object { -not $_.internalPanel }).Count -gt 0) 'An active external display or projector is required.'
    Assert-True (-not $initial.mirrored) 'Start the hardware suite from Extend topology.'
    Assert-True (-not $initial.session -or $initial.session.state -eq 'Idle') 'Start the hardware suite with an Idle session.'

    $sourceTray = Get-Process Beam -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $tray }
    if (-not $sourceTray) {
        Assert-True (-not (Get-Process Beam -ErrorAction SilentlyContinue)) 'Stop the installed/old Beam tray before testing the source build.'
        $startupLogOffset = (Get-Item $logPath -ErrorAction SilentlyContinue).Length
        if ($null -eq $startupLogOffset) { $startupLogOffset = 0 }
        $sourceTray = Start-Process $tray -ArgumentList '--hidden' -PassThru
        for ($i = 0; $i -lt 60; $i++) {
            Start-Sleep -Milliseconds 250
            $status = Get-LiveStatus
            if ($status.session -and $status.session.pid -eq $sourceTray.Id) { break }
        }
        Wait-LogContains $startupLogOffset 'Global hotkey registered'
    }

    $baseline = Get-LiveStatus
    $baseline | ConvertTo-Json -Depth 20 | Set-Content (Join-Path $evidenceDir 'display-baseline.json')
    Get-DisplayFingerprint $baseline | Set-Content (Join-Path $evidenceDir 'display-fingerprint-baseline.json')
    $externalCount = @($baseline.displays).Count
    try {
        Send-BeamHotkey
        $active = Wait-Session 'Active'
        Assert-True $active.mirrored 'Manual Mirror apply did not enter clone topology.'
        Assert-True (@($active.displays | Where-Object { $_.aspect -ne '16:9' -and $_.source -ne '0x0' }).Count -eq 0) 'Not every active source reached 16:9.'
        Assert-True ($active.session.nightLightDisabled -eq $true) 'Live status does not confirm Night Light was disabled.'
        Assert-True ($null -ne $active.session.appliedDpiPercent) 'Live status has no applied DPI value.'
        $active | ConvertTo-Json -Depth 20 | Set-Content (Join-Path $evidenceDir 'display-active.json')

        Send-BeamHotkey
        $idle = Wait-Session 'Idle'
        Assert-DisplayBaseline $baseline
        Assert-True ($idle.session.reason -eq 'restore complete') "Unexpected restore result: $($idle.session.reason)"
        Assert-True (-not (Test-Path $journalPath)) 'Recovery journal remains after a successful Undo.'
        Write-Output 'Manual Mirror Apply/Undo passed.'

        Set-DisplayTopology 'clone'
        Wait-DisplayCount $externalCount | Out-Null
        $clone = Get-LiveStatus
        if (-not $clone.needsFix) {
            $spike = Join-Path $repoRoot 'spikes\S6.DuplicateApply\bin\Release\net10.0-windows\S6.DuplicateApply.exe'
            Assert-True (Test-Path $spike) 'The S6 mode probe is missing; build Beam.sln first.'
            & $spike mode '\\.\DISPLAY1' 3840x2400 --yes | Out-Null
        }

        $automatic = Wait-Session 'Active'
        Assert-True $automatic.mirrored 'Automatic duplicate detection did not produce Mirror mode.'
        Assert-True (@($automatic.displays | Where-Object { $_.aspect -ne '16:9' -and $_.source -ne '0x0' }).Count -eq 0) 'Automatic apply left a non-16:9 source.'
        Send-BeamHotkey
        Wait-Session 'Idle' | Out-Null
        Assert-DisplayBaseline $baseline
        Write-Output 'Automatic duplicate detection passed.'

        Send-BeamHotkey
        Wait-Session 'Active' | Out-Null
        Set-DisplayTopology 'internal'
        Wait-Session 'Idle' | Out-Null
        Assert-True (-not (Test-Path $journalPath)) 'Disconnect restore left a recovery journal.'
        Write-Output 'Disconnect recovery passed.'
    }
    finally {
        Restore-ActiveSession
        if (@((Get-LiveStatus).displays).Count -ne $externalCount) {
            Set-DisplayTopology 'extend'
            Wait-DisplayCount $externalCount | Out-Null
        }
        Assert-DisplayBaseline $baseline
    }

    Write-Output "Hardware cycle verified on: $(@($baseline.displays | Where-Object { -not $_.internalPanel } | ForEach-Object { $_.name }) -join ', ')"
    Write-Output 'HARDWARE PRODUCTION PASS'
}

function Invoke-CliProduction {
    $sourceTray = Get-Process Beam -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $tray }
    if ($sourceTray) {
        $refused = Invoke-Beam -Arguments @('apply', '--json') -Json -ExpectedExit 1
        Assert-True (-not $refused.Json.success -and $refused.Json.error.Contains('tray')) 'Live-tray ownership refusal failed.'
        $sourceTray | Stop-Process -ErrorAction SilentlyContinue
        Wait-For { $null -eq (Get-LiveStatus).session } 'Source tray did not exit cleanly.' | Out-Null
    }

    $help = Invoke-Beam -Arguments @('help')
    Assert-True ($help.Output.Contains('beam status')) 'Text help is incomplete.'
    $helpJson = Invoke-Beam -Arguments @('help', '--json') -Json
    Assert-True ($helpJson.Json.success -and @($helpJson.Json.commands).Count -eq 6) 'JSON help contract failed.'

    $status = Invoke-Beam -Arguments @('status')
    Assert-True ($status.Output.Contains('Mirrored:')) 'Text status is incomplete.'
    $statusJson = Invoke-Beam -Arguments @('status', '--json') -Json
    Assert-True $statusJson.Json.success 'JSON status failed.'
    $devices = Invoke-Beam -Arguments @('devices')
    Assert-True ($devices.Output.Contains('[active]')) 'Text devices has no active row.'
    $devicesJson = Invoke-Beam -Arguments @('devices', '--json') -Json
    Assert-True (@($devicesJson.Json.devices | Where-Object { $_.active }).Count -eq @($statusJson.Json.displays).Count) 'Active device counts disagree.'

    $expectedDryRunExit = if ($statusJson.Json.needsFix) { 0 } else { 3 }
    $dryRun = Invoke-Beam -Arguments @('apply', '--dry-run', '--json') -Json -ExpectedExit $expectedDryRunExit
    Assert-True ($dryRun.Json.success -eq [bool]$statusJson.Json.needsFix) 'Dry-run result does not match the live trigger.'
    $badCommand = Invoke-Beam -Arguments @('not-a-command', '--json') -Json -ExpectedExit 2
    Assert-True (-not $badCommand.Json.success) 'Unknown command did not return structured failure.'
    $badOption = Invoke-Beam -Arguments @('status', '--not-an-option', '--json') -Json -ExpectedExit 2
    Assert-True (-not $badOption.Json.success) 'Unknown option did not return structured failure.'
    $badDevice = Invoke-Beam -Arguments @('dpi', 'read', '--device', '\\.\NOT_A_DISPLAY', '--json') -Json -ExpectedExit 1
    Assert-True (-not $badDevice.Json.success) 'Invalid DPI device did not return exit code 1.'

    $dpi = Invoke-Beam -Arguments @('dpi', 'read', '--json') -Json
    Assert-True ($dpi.Json.current -ge 100) 'DPI read returned an invalid value.'
    $dpiSet = Invoke-Beam -Arguments @('dpi', 'set', [string]$dpi.Json.current, '--json') -Json
    Assert-True $dpiSet.Json.success 'No-op DPI set was not verified.'

    Import-WindowsAdapters
    $controller = [Beam.Windows.NightLight.NightLightController]::new()
    $raw = Save-NightLightRaw $controller 'nightlight-cli-baseline.json'
    try {
        $off = Invoke-Beam -Arguments @('nightlight', 'off', '--json') -Json
        Assert-True $off.Json.success 'CLI Night Light off failed.'
        $on = Invoke-Beam -Arguments @('nightlight', 'on', '--json') -Json
        Assert-True $on.Json.success 'CLI Night Light on failed.'
        $restoreNightLight = Invoke-Beam -Arguments @('restore', '--nightlight', '--json') -Json
        Assert-True $restoreNightLight.Json.success 'CLI restore --nightlight failed.'
    }
    finally {
        Restore-NightLightRaw $raw
    }

    $restore = Invoke-Beam -Arguments @('restore', '--json') -Json
    Assert-True $restore.Json.success 'CLI restore failed.'
    Write-Output 'CLI commands, JSON, reachable exit codes, DPI and Night Light verified.'
    Write-Output 'CLI PRODUCTION PASS'
}

function Invoke-FinalStateProduction {
    $status = Get-LiveStatus
    Assert-True (-not $status.session -or $status.session.state -eq 'Idle') "Live session is not Idle: $($status.session.state)"
    if (Test-Path $journalPath) {
        $journal = Get-Content $journalPath -Raw | ConvertFrom-Json
        Assert-True (-not $journal.IsActive) 'An active recovery journal remains.'
    }

    $settingsBaselinePath = Join-Path $evidenceDir 'settings-baseline.json'
    if ((Test-Path $settingsBaselinePath) -and (Test-Path $settingsPath)) {
        $beforeSettings = Get-Content $settingsBaselinePath -Raw | ConvertFrom-Json
        $afterSettings = Get-Content $settingsPath -Raw | ConvertFrom-Json
        $policyProperties = @(
            'IsEnabled', 'UseMode', 'UseDpi', 'UseNightLight', 'KeepAwake', 'DoNotDisturb',
            'AudioToHdmi', 'Hotkey', 'ProjectorTopology', 'TimedPauseMinutes',
            'RestoreOnDisconnect', 'DpiTargetPercent', 'PreferNativeMode', 'IgnoredDisplays')
        foreach ($property in $policyProperties) {
            $beforeValue = $beforeSettings.$property | ConvertTo-Json -Compress
            $afterValue = $afterSettings.$property | ConvertTo-Json -Compress
            Assert-True ($beforeValue -eq $afterValue) "Setting $property changed during production tests."
        }
    }

    $displayBaselinePath = Join-Path $evidenceDir 'display-baseline.json'
    if (Test-Path $displayBaselinePath) {
        $baseline = Get-Content $displayBaselinePath -Raw | ConvertFrom-Json
        if (@($baseline.displays).Count -eq @($status.displays).Count) { Assert-DisplayBaseline $baseline }
    }

    $rawBaselinePath = Join-Path $evidenceDir 'nightlight-raw-baseline.json'
    if (Test-Path $rawBaselinePath) {
        Import-WindowsAdapters
        $controller = [Beam.Windows.NightLight.NightLightController]::new()
        $raw = Get-Content $rawBaselinePath -Raw | ConvertFrom-Json
        foreach ($record in @($raw)) {
            $entry = $controller.Read($record.KeyPath)
            Assert-True ($null -ne $entry.State) "Night Light key is missing or unreadable: $($record.KeyPath)"
            Assert-True ($entry.State.Active -eq $record.Active) "Night Light active state differs: $($record.KeyPath)"
            Assert-True ([string]$entry.State.TransitionCause -eq $record.TransitionCause) "Night Light transition cause differs: $($record.KeyPath)"
        }
    }

    $offsetPath = Join-Path $evidenceDir 'log-offset.txt'
    if ((Test-Path $offsetPath) -and (Test-Path $logPath)) {
        $offset = [long](Get-Content $offsetPath)
        $stream = [System.IO.File]::Open($logPath, 'Open', 'Read', 'ReadWrite')
        try {
            $stream.Position = [Math]::Min($offset, $stream.Length)
            $reader = [System.IO.StreamReader]::new($stream)
            try { $newLog = $reader.ReadToEnd() } finally { $reader.Dispose() }
        }
        finally { $stream.Dispose() }
        Assert-True ($newLog -notmatch '\[ERROR\]') 'The production run wrote an ERROR log entry.'
        Assert-True ($newLog -notmatch 'Restore incomplete') 'The production run left an incomplete restore.'
        Assert-True (-not $newLog.Contains($env:USERNAME)) 'Raw production logs contain the user name.'
        Assert-True (-not $newLog.Contains($env:COMPUTERNAME)) 'Raw production logs contain the machine name.'
    }

    Write-Output 'FINAL STATE PASS'
}

Save-RunBaseline
switch ($Suite) {
    'Hardware' { Invoke-HardwareProduction }
    'NightLight' {
        Invoke-NightLightProduction
        Write-Output 'NIGHT LIGHT PRODUCTION PASS'
    }
    'Cli' { Invoke-CliProduction }
    'FinalState' { Invoke-FinalStateProduction }
}
