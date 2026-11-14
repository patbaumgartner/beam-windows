# Signs Beam binaries and the installer (Phase A item A8 / open question O6).
#
# Modes:
#   -Mode AzureTrustedSigning  Azure Trusted Signing via AzureSignTool (recommended for
#                              CI: no cert file, short-lived tokens, timestamped).
#   -Mode Certificate           signtool.exe with a code-signing cert from the local
#                              certificate store (developer machines).
#   -Mode Verify                No signing — verifies existing signatures instead.
#
# Examples:
#   .\installer\sign.ps1 -Mode AzureTrustedSigning -Files .\artifacts\Beam.exe,.\artifacts\Beam.Cli.exe,.\artifacts\BeamSetup-0.2.0.exe `
#       -AzureEndpoint "https://wus.codesigning.azure.net/" -TenantId "..." -ClientId "..." -ClientSecret "..."
#
#   .\installer\sign.ps1 -Mode Certificate -Thumbprint "AB12..." -Files .\artifacts\BeamSetup-0.2.0.exe
#
#   .\installer\sign.ps1 -Mode Verify -Files .\artifacts\BeamSetup-0.2.0.exe
#
# CI wiring: the release workflow (kept upstream, .github/workflows/release.yml) calls
# this script after `installer\build.ps1` with secrets from GitHub Actions secrets
# (AZURE_TENANT_ID / AZURE_CLIENT_ID / AZURE_CLIENT_SECRET), then calls -Mode Verify as
# a gate. This local copy ships the script only.
#
# SmartScreen note: even signed, a brand-new publisher reputation takes a few
# downloads before SmartScreen stops warning. That is expected and documented in
# SECURITY.md; signing removes the "unknown publisher" state permanently.

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet("AzureTrustedSigning", "Certificate", "Verify")]
    [string]$Mode,

    [Parameter(Mandatory = $true)]
    [string[]]$Files,

    # Azure Trusted Signing
    [string]$AzureEndpoint = "https://wus.codesigning.azure.net/",
    [string]$TenantId = "",
    [string]$ClientId = "",
    [string]$ClientSecret = "",

    # Certificate store
    [string]$Thumbprint = "",

    [string]$SignToolPath = "",
    [string]$AzureSignToolPath = ""
)

$ErrorActionPreference = "Stop"

function Resolve-SignTool {
    if ($SignToolPath -and (Test-Path $SignToolPath)) { return $SignToolPath }
    $candidates = @(
        "${env:ProgramFiles(x86)}\Windows Kits\10\bin\10.0.22621.0\x64\signtool.exe",
        "${env:ProgramFiles(x86)}\Windows Kits\10\bin\x64\signtool.exe"
    )
    foreach ($c in $candidates) {
        $found = Get-ChildItem -Path $c -ErrorAction SilentlyContinue |
            Sort-Object FullName -Descending | Select-Object -First 1
        if ($found) { return $found.FullName }
    }
    # Fallback: any signtool on PATH
    $onPath = Get-Command signtool.exe -ErrorAction SilentlyContinue
    if ($onPath) { return $onPath.Source }
    throw "signtool.exe not found. Install the Windows SDK or pass -SignToolPath."
}

function Resolve-AzureSignTool {
    if ($AzureSignToolPath -and (Test-Path $AzureSignToolPath)) { return $AzureSignToolPath }
    $onPath = Get-Command AzureSignTool.exe -ErrorAction SilentlyContinue
    if ($onPath) { return $onPath.Source }
    throw "AzureSignTool.exe not found. Install it: dotnet tool install --global AzureSignTool"
}

$Files = $Files | ForEach-Object { (Resolve-Path $_).Path }
foreach ($f in $Files) {
    if (-not (Test-Path $f)) { throw "File not found: $f" }
}

switch ($Mode) {
    "AzureTrustedSigning" {
        if (-not $TenantId -or -not $ClientId -or -not $ClientSecret) {
            throw "Azure Trusted Signing needs -TenantId, -ClientId and -ClientSecret (use env vars in CI, never commit them)."
        }
        $tool = Resolve-AzureSignTool
        Write-Host "==> Signing $($Files.Count) file(s) with Azure Trusted Signing ($AzureEndpoint)"
        foreach ($f in $Files) {
            Write-Host "    $f"
            & $tool sign --azure-tenant $TenantId --azure-client-id $ClientId --azure-client-secret $ClientSecret `
                --endpoint $AzureEndpoint --timestamp-rfc3161 http://timestamp.acs.microsoft.com --files $f
            if ($LASTEXITCODE -ne 0) { throw "AzureSignTool failed on $f" }
        }
    }

    "Certificate" {
        if (-not $Thumbprint) { throw "-Mode Certificate needs -Thumbprint of a code-signing cert in the store." }
        $tool = Resolve-SignTool
        Write-Host "==> Signing $($Files.Count) file(s) with cert $Thumbprint"
        foreach ($f in $Files) {
            Write-Host "    $f"
            & $tool sign /sha256 /fd sha256 /tr http://timestamp.digicert.com /td sha256 /a /sm /n "Beam" $f
            # /a picks the best cert by default; explicit thumbprint when given:
            if ($LASTEXITCODE -ne 0) {
                & $tool sign /sha256 /fd sha256 /tr http://timestamp.digicert.com /td sha256 /sm /csp "Microsoft Enhanced RSA and AES Cryptographic Provider" /sha1 $Thumbprint $f
            }
            if ($LASTEXITCODE -ne 0) { throw "signtool failed on $f" }
        }
    }

    "Verify" {
        $tool = Resolve-SignTool
        Write-Host "==> Verifying signatures on $($Files.Count) file(s)"
        $failed = @()
        foreach ($f in $Files) {
            & $tool verify /pa /all $f | Out-Null
            if ($LASTEXITCODE -ne 0) { $failed += $f; Write-Host "    UNSIGNED/INVALID: $f" -ForegroundColor Red }
            else { Write-Host "    OK: $f" }
            # Also surface the Authenticode status via PowerShell for CI logs.
            $sig = Get-AuthenticodeSignature -FilePath $f
            Write-Host ("    status={0} signer={1}" -f $sig.Status, $sig.SignerCertificate.Subject)
        }
        if ($failed.Count -gt 0) { throw "Signature verification failed for: $($failed -join ', ')" }
    }
}

Write-Host "==> Done ($Mode)."
