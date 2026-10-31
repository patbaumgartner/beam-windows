#requires -Version 7.0
<#
.SYNOPSIS
    Verifies that every relative link and image reference in Markdown resolves.

.DESCRIPTION
    Documentation rots fastest where nobody looks. This checks the whole repository
    (excluding build output and .git) so a renamed file cannot leave dangling links.

    Only relative links are checked. Absolute URLs, anchors, and mail links are ignored:
    network access would make CI flaky.

.PARAMETER Root
    Repository root. Defaults to the parent of this script's directory.

.EXAMPLE
    pwsh eng/check-links.ps1
#>
[CmdletBinding()]
param(
    [string]$Root = (Split-Path -Parent $PSScriptRoot)
)

$ErrorActionPreference = 'Stop'

$excluded = @('bin', 'obj', '.git', 'node_modules', 'TestResults', 'artifacts')

$files = Get-ChildItem -Path $Root -Recurse -File -Filter *.md |
    Where-Object {
        $relative = $_.FullName.Substring($Root.Length).TrimStart([IO.Path]::DirectorySeparatorChar)
        $segments = $relative -split '[\\/]'
        -not ($segments | Where-Object { $excluded -contains $_ })
    }

# [text](target) and ![alt](target); the target stops at whitespace or the closing paren.
$linkPattern = [regex]'(?<!!)\[[^\]]*\]\((?<target>[^()\s]+)(?:\s+"[^"]*")?\)|!\[[^\]]*\]\((?<target>[^()\s]+)(?:\s+"[^"]*")?\)'

$problems = New-Object System.Collections.Generic.List[string]
$checked = 0

foreach ($file in $files) {
    $inFence = $false
    $lineNumber = 0

    foreach ($line in [IO.File]::ReadAllLines($file.FullName)) {
        $lineNumber++

        # Ignore fenced code blocks: they may contain prose with parentheses.
        if ($line -match '^\s*(```|~~~)') {
            $inFence = -not $inFence
            continue
        }

        if ($inFence) {
            continue
        }

        foreach ($match in $linkPattern.Matches($line)) {
            $target = $match.Groups['target'].Value

            if ([string]::IsNullOrWhiteSpace($target)) { continue }
            if ($target -match '^(#|https?://|mailto:|tel:|ftp://)') { continue }
            if ($target.StartsWith('<') -or $target.Contains('{{')) { continue }

            $path = $target.Split('#')[0]
            if ([string]::IsNullOrWhiteSpace($path)) { continue }

            $path = [Uri]::UnescapeDataString($path)

            # Directory-relative resolution; links are always relative to the file.
            $resolved = Join-Path $file.DirectoryName $path
            $checked++

            if (-not (Test-Path -LiteralPath $resolved)) {
                $relativeFile = $file.FullName.Substring($Root.Length).TrimStart([IO.Path]::DirectorySeparatorChar)
                $problems.Add("${relativeFile}:${lineNumber} -> $target")
            }
        }
    }
}

Write-Output "Checked $checked relative Markdown link(s) in $($files.Count) file(s)."

if ($problems.Count -gt 0) {
    Write-Output ''
    Write-Output 'Broken links:'
    foreach ($problem in $problems) {
        Write-Output "  $problem"
    }

    exit 1
}

Write-Output 'All relative links resolve.'
