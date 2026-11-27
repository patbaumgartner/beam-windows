#requires -Version 7.0
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false
$repoRoot = Split-Path -Parent $PSScriptRoot
$probe = Join-Path $repoRoot 'eng\ProductionUiProbe\bin\Release\net10.0-windows\ProductionUiProbe.exe'
$beam = Join-Path $repoRoot 'src\Beam.Tray\bin\Release\net10.0-windows\Beam.exe'
$evidenceDir = Join-Path $repoRoot 'artifacts\production-tests'
$settingsPath = Join-Path $env:LOCALAPPDATA 'Beam\settings.json'
$runKeyPath = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$settingsBackup = Join-Path $evidenceDir 'ui-settings-backup.json'
New-Item -ItemType Directory -Path $evidenceDir -Force | Out-Null
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName System.IO.Compression.FileSystem
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class BeamUiMouse {
    [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
    [StructLayout(LayoutKind.Sequential)] public struct MOUSEINPUT { public int dx, dy; public uint mouseData, dwFlags, time; public UIntPtr dwExtraInfo; }
    [StructLayout(LayoutKind.Sequential)] public struct INPUT { public uint type; public MOUSEINPUT mi; }
    [DllImport("user32.dll", SetLastError=true)] private static extern uint SendInput(uint count, INPUT[] inputs, int size);
    public static bool LeftClick(int x, int y) {
        if (!SetCursorPos(x, y)) return false;
        var inputs = new[] {
            new INPUT { type = 0, mi = new MOUSEINPUT { dwFlags = 0x0002 } },
            new INPUT { type = 0, mi = new MOUSEINPUT { dwFlags = 0x0004 } }
        };
        return SendInput(2, inputs, Marshal.SizeOf<INPUT>()) == 2;
    }
}
'@

function Click-Element($Element) {
    $rect = $Element.Current.BoundingRectangle
    $clicked = [BeamUiMouse]::LeftClick(
        [int]($rect.X + $rect.Width / 2), [int]($rect.Y + $rect.Height / 2))
    Assert-True $clicked 'Win32 could not deliver a left click.'
}

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

function Find-Window([string]$Title, [int]$Attempts = 100) {
    $root = [System.Windows.Automation.AutomationElement]::RootElement
    $condition = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, $Title)
    for ($i = 0; $i -lt $Attempts; $i++) {
        $window = $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)
        if ($window) { return $window }
        Start-Sleep -Milliseconds 50
    }
    throw "Window not found: $Title"
}

function Resolve-ControlType([string]$Name) {
    return [System.Windows.Automation.ControlType].GetField($Name).GetValue($null)
}

function Get-Controls($Window, [string]$ControlType) {
    $condition = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
        (Resolve-ControlType $ControlType))
    $found = $Window.FindAll([System.Windows.Automation.TreeScope]::Descendants, $condition)
    $items = @()
    for ($i = 0; $i -lt $found.Count; $i++) { $items += $found.Item($i) }
    return $items
}

function Find-NamedControl($Window, [string]$Name, [string]$ControlType = 'Button') {
    $condition = New-Object System.Windows.Automation.AndCondition(
        (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, $Name)),
        (New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
            (Resolve-ControlType $ControlType))))
    $control = $Window.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)
    if (-not $control) {
        $typeCondition = New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
            (Resolve-ControlType $ControlType))
        $candidates = $Window.FindAll([System.Windows.Automation.TreeScope]::Descendants, $typeCondition)
        for ($i = 0; $i -lt $candidates.Count; $i++) {
            if ($candidates.Item($i).Current.Name.StartsWith($Name)) {
                $control = $candidates.Item($i)
                break
            }
        }
    }
    Assert-True ($null -ne $control) "Control not found: $Name ($ControlType)"
    return $control
}

function Invoke-NamedButton($Window, [string]$Name) {
    $button = Find-NamedControl $Window $Name 'Button'
    $button.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
}

function Get-Edits($Window) {
    return @(Get-Controls $Window 'Edit')
}

function Set-EditValue($Edit, [string]$Value) {
    $Edit.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue($Value)
}

function Get-EditValue($Edit) {
    return $Edit.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value
}

function Set-CheckBox($Window, [string]$Name, [bool]$Desired) {
    $box = Find-NamedControl $Window $Name 'CheckBox'
    $pattern = $box.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern)
    $checked = $pattern.Current.ToggleState -eq [System.Windows.Automation.ToggleState]::On
    if ($checked -ne $Desired) { Click-Element $box }
    Wait-For { ($pattern.Current.ToggleState -eq [System.Windows.Automation.ToggleState]::On) -eq $Desired } "Checkbox did not change: $Name" | Out-Null
}

function Select-ComboItem($Window, [int]$ComboIndex, [string]$ItemPrefix) {
    $combo = @(Get-Controls $Window 'ComboBox')[$ComboIndex]
    Assert-True ($null -ne $combo) "ComboBox $ComboIndex was not found."
    $combo.SetFocus()
    $expand = $combo.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern)
    $expand.Expand()
    $root = [System.Windows.Automation.AutomationElement]::RootElement
    $item = Wait-For {
        $all = $root.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
        for ($i = 0; $i -lt $all.Count; $i++) {
            $candidate = $all.Item($i)
            if ($candidate.Current.Name.StartsWith($ItemPrefix) -and
                $candidate.Current.ControlType -eq [System.Windows.Automation.ControlType]::ListItem -and
                ($candidate.GetSupportedPatterns() -contains [System.Windows.Automation.SelectionItemPattern]::Pattern)) {
                return $candidate
            }
        }
        return $null
    } "Combo item not found: $ItemPrefix"
    $selected = $combo.GetCurrentPattern([System.Windows.Automation.SelectionPattern]::Pattern).Current.GetSelection()
    if ($selected.Length -eq 1 -and $selected[0].Current.Name.StartsWith($ItemPrefix)) {
        $expand.Collapse()
        return
    }

    $item.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    if ($expand.Current.ExpandCollapseState -eq [System.Windows.Automation.ExpandCollapseState]::Expanded) {
        $expand.Collapse()
    }
}

function Read-Settings {
    return Get-Content $settingsPath -Raw | ConvertFrom-Json
}

function Wait-Setting([string]$Property, $Expected) {
    Wait-For {
        $value = (Read-Settings).$Property
        return $value -eq $Expected
    } "Setting $Property did not become $Expected" | Out-Null
}

function Start-Probe([string]$Command, [string]$Title) {
    $process = Start-Process $probe -ArgumentList $Command -PassThru
    $window = Find-Window $Title
    return [pscustomobject]@{ Process = $process; Window = $window }
}

function Close-Probe($Probe, [string]$Title) {
    try {
        $window = Find-Window $Title 10
        Invoke-NamedButton $window 'Close'
    }
    catch {
        if ($Probe.Process -and -not $Probe.Process.HasExited) { Stop-Process -Id $Probe.Process.Id -ErrorAction SilentlyContinue }
    }
    if ($Probe.Process) { $Probe.Process.WaitForExit(10000) | Out-Null }
}

function Save-FileDialog([string]$Path) {
    $dialog = Find-Window 'Save As'
    $fileName = Find-NamedControl $dialog 'File name:' 'Edit'
    Set-EditValue $fileName $Path
    Invoke-NamedButton $dialog 'Save'
    Wait-For { Test-Path $Path } "Save dialog did not create $Path" | Out-Null
}

function Invoke-ProbeCommand([string]$Command) {
    $output = & $probe $Command | Out-String
    Assert-True ($LASTEXITCODE -eq 0) "Probe $Command failed: $output"
    return $output.Trim()
}

$originalRunValue = Get-ItemProperty -Path $runKeyPath -Name 'Beam' -ErrorAction SilentlyContinue
Copy-Item $settingsPath $settingsBackup -Force
Get-Process ProductionUiProbe -ErrorAction SilentlyContinue | Stop-Process -ErrorAction SilentlyContinue
Get-Process Beam -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $beam } | Stop-Process -ErrorAction SilentlyContinue
$startedBeam = @()
try {
    Assert-True ((Test-Path $probe) -and (Test-Path $beam)) 'Build the UI probe and tray first.'

    $settings = Start-Probe '--settings' 'Beam settings'
    $window = $settings.Window
    $edits = Get-Edits $window
    Assert-True ($edits.Count -ge 2) 'Settings editors are missing.'
    Set-EditValue $edits[0] 'Win+P'
    $edits[1].SetFocus()
    Wait-Setting 'Hotkey' 'Win+Alt+P'
    Set-EditValue $edits[0] 'Win+Alt+F9'
    @(Get-Controls $window 'ComboBox')[0].SetFocus()
    Wait-Setting 'Hotkey' 'Win+Alt+F9'
    Select-ComboItem $window 0 'Pause for 1 h'
    Wait-Setting 'TimedPauseMinutes' 60
    Select-ComboItem $window 1 'Extend'
    Wait-Setting 'ProjectorTopology' 'Extend'
    Set-CheckBox $window 'Control DPI' $false
    Wait-Setting 'UseDpi' $false
    Set-CheckBox $window 'Control DPI' $true
    Wait-Setting 'UseDpi' $true
    Set-CheckBox $window 'Auto-apply' $false
    Wait-For { @((Read-Settings).KnownDevices | Where-Object { -not $_.AutoApply }).Count -gt 0 } 'Device Auto-apply did not persist off' | Out-Null
    Set-CheckBox $window 'Auto-apply' $true
    Wait-For { @((Read-Settings).KnownDevices | Where-Object { -not $_.AutoApply }).Count -eq 0 } 'Device Auto-apply did not persist on' | Out-Null
    Invoke-NamedButton $window 'Re-evaluate now'
    Invoke-NamedButton $window 'Reset safe mode'

    $settingsExport = Join-Path $evidenceDir 'settings-export.json'
    Remove-Item $settingsExport -ErrorAction SilentlyContinue
    Invoke-NamedButton $window 'Export settings'
    Save-FileDialog $settingsExport
    $exported = Get-Content $settingsExport -Raw | ConvertFrom-Json
    Assert-True ($exported.Hotkey -eq 'Win+Alt+F9' -and $exported.ProjectorTopology -eq 'Extend') 'Settings export is incomplete.'
    Close-Probe $settings 'Beam settings'

    $reopened = Start-Probe '--settings' 'Beam settings'
    $reopenedEdits = Get-Edits $reopened.Window
    Assert-True ((Get-EditValue $reopenedEdits[0]) -eq 'Win+Alt+F9') 'Hotkey did not survive reopen.'
    $combos = @(Get-Controls $reopened.Window 'ComboBox')
    Assert-True ($combos[0].GetCurrentPattern([System.Windows.Automation.SelectionPattern]::Pattern).Current.GetSelection()[0].Current.Name -eq 'Pause for 1 h') 'Pause preset did not survive reopen.'
    Assert-True ($combos[1].GetCurrentPattern([System.Windows.Automation.SelectionPattern]::Pattern).Current.GetSelection()[0].Current.Name.StartsWith('Extend')) 'Projector topology did not survive reopen.'
    Close-Probe $reopened 'Beam settings'
    Write-Output 'Settings controls and persistence passed.'

    $diagnostics = Start-Probe '--diagnostics' 'Beam diagnostics'
    $diagWindow = $diagnostics.Window
    Invoke-NamedButton $diagWindow 'Copy summary'
    $summary = Wait-For {
        $text = Get-Clipboard -Raw
        return $text.Contains('Session state') ? $text : $null
    } 'Diagnostics summary did not reach the clipboard.'
    Assert-True ($summary.Contains('Beam') -and $summary.Contains('Session state')) 'Copied diagnostics summary is incomplete.'
    Assert-True (-not $summary.Contains($env:USERNAME) -and -not $summary.Contains($env:COMPUTERNAME)) 'Copied summary contains private names.'
    Invoke-NamedButton $diagWindow 'Re-evaluate now'
    Invoke-NamedButton $diagWindow 'Reset safe mode'

    $bundleInput = Join-Path $evidenceDir 'diagnostics.zip'
    Remove-Item $bundleInput -ErrorAction SilentlyContinue
    $beforeZips = @(Get-ChildItem $evidenceDir -Filter 'beam-diagnostics-*.zip' -ErrorAction SilentlyContinue | ForEach-Object { $_.FullName })
    Invoke-NamedButton $diagWindow 'Export bundle'
    $dialog = Find-Window 'Save As'
    $dialogFileName = Find-NamedControl $dialog 'File name:' 'Edit'
    Set-EditValue $dialogFileName $bundleInput
    Invoke-NamedButton $dialog 'Save'
    $zipPath = Wait-For {
        $candidate = Get-ChildItem $evidenceDir -Filter 'beam-diagnostics-*.zip' -ErrorAction SilentlyContinue |
            Where-Object { $_.FullName -notin $beforeZips } | Select-Object -First 1 -ExpandProperty FullName
        if ($candidate) {
            try {
                $probeZip = [System.IO.Compression.ZipFile]::OpenRead($candidate)
                $probeZip.Dispose()
                return $candidate
            }
            catch [System.IO.IOException] {
                return $null
            }
        }
        return $null
    } 'Diagnostics bundle was not exported.'
    $zip = [System.IO.Compression.ZipFile]::OpenRead($zipPath)
    try {
        Assert-True ($zip.Entries.Count -ge 2) 'Diagnostics bundle is incomplete.'
        foreach ($entry in $zip.Entries) {
            $reader = [System.IO.StreamReader]::new($entry.Open())
            try { $text = $reader.ReadToEnd() } finally { $reader.Dispose() }
            Assert-True (-not $text.Contains($env:USERNAME) -and -not $text.Contains($env:COMPUTERNAME)) "Diagnostics entry contains private names: $($entry.FullName)"
        }
    }
    finally { $zip.Dispose() }
    Close-Probe $diagnostics 'Beam diagnostics'
    Write-Output 'Diagnostics actions and sanitized export passed.'

    $pause = Invoke-ProbeCommand '--pause-cycle'
    Assert-True ($pause.Contains('PAUSED=Paused') -and $pause.Contains('RESUMED=Idle')) "Pause/resume failed: $pause"

    $startupBefore = Invoke-ProbeCommand '--startup-check'
    $startupToggled = Invoke-ProbeCommand '--startup-toggle'
    $startupAfter = Invoke-ProbeCommand '--startup-check'
    Assert-True ($startupBefore -ne $startupAfter) 'Startup toggle did not change the Run-key state.'
    Invoke-ProbeCommand '--startup-toggle' | Out-Null
    $startupRestored = Invoke-ProbeCommand '--startup-check'
    Assert-True ($startupRestored -eq $startupBefore) 'Startup toggle did not restore the original state.'
    Write-Output 'Pause/resume and startup toggle passed.'

    $first = Start-Process $beam -ArgumentList '--hidden' -PassThru
    $startedBeam += $first
    $second = Start-Process $beam -ArgumentList '--hidden' -PassThru
    $startedBeam += $second
    Wait-For { $second.HasExited } 'Second Beam instance did not exit.' | Out-Null
    $running = @(Get-Process Beam -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $beam })
    Assert-True ($running.Count -eq 1 -and $running[0].Id -eq $first.Id) 'Single-instance guard failed.'
    Write-Output 'Single-instance behavior passed.'

    Write-Output 'UI PRODUCTION PASS'
}
finally {
    Get-Process ProductionUiProbe -ErrorAction SilentlyContinue | Stop-Process -ErrorAction SilentlyContinue
    foreach ($process in $startedBeam) {
        if ($process -and -not $process.HasExited) { Stop-Process -Id $process.Id -ErrorAction SilentlyContinue }
    }
    Copy-Item $settingsBackup $settingsPath -Force
    if ($null -ne $originalRunValue) {
        Set-ItemProperty -Path $runKeyPath -Name 'Beam' -Value $originalRunValue.Beam
    }
    else {
        Remove-ItemProperty -Path $runKeyPath -Name 'Beam' -ErrorAction SilentlyContinue
    }
}
