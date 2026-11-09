# Regenerates every raster Beam icon from the single vector source of truth,
# src/Beam.Tray/Assets/high-beam.svg ("high beam" by I Create Stuff, The Noun
# Project, CC BY 3.0 — see ..\assets\ATTRIBUTION.md). The SVG already carries the
# brand colours (Pantone 638C #00AED8 fill, deep #0080B4 halo), so no tinting here.
#
# Outputs:
#   ..\src\Beam.Tray\Assets\app.ico  multi-size icon (exe + installer icon)
#   ..\assets\beam-256.png           plain-cyan 256 px mark (halo dropped)
#
# Rendering runs through render-icon.fsx (dotnet fsi + Svg.NET).

$ErrorActionPreference = 'Stop'

$svgPath = Join-Path $PSScriptRoot "..\src\Beam.Tray\Assets\high-beam.svg"
$fsxPath = Join-Path $PSScriptRoot "render-icon.fsx"

$sizes = @(16, 20, 24, 32, 40, 48, 64, 128, 256)

$tempDir = Join-Path ([System.IO.Path]::GetTempPath()) "beam-icon-gen"
if (Test-Path $tempDir) { Remove-Item $tempDir -Recurse -Force }
New-Item -ItemType Directory -Path $tempDir | Out-Null

try {
    # ICO entries: render each size to a PNG in the temp dir.
    dotnet fsi $fsxPath -- --svg $svgPath --out-dir $tempDir --sizes ($sizes -join ',')
    if ($LASTEXITCODE -ne 0) { throw "render-icon.fsx failed for ICO sizes" }

    $images = foreach ($s in $sizes) {
        $pngPath = Join-Path $tempDir ("icon-{0}.png" -f $s)
        [PSCustomObject]@{ size = $s; png = [System.IO.File]::ReadAllBytes($pngPath) }
    }

    # Pack the PNGs into a multi-size ICO (PNG-compressed entries).
    $icoPath = Join-Path $PSScriptRoot "..\src\Beam.Tray\Assets\app.ico"
    $dataOffset = 6 + ($images.Count * 16)
    $fs = [System.IO.File]::Create($icoPath)
    $bw = [System.IO.BinaryWriter]::new($fs)
    $bw.Write([uint16]0); $bw.Write([uint16]1); $bw.Write([uint16]$images.Count)
    foreach ($img in $images) {
        $s = $img.size
        $w = if ($s -ge 256) { 0 } else { $s }   # 0 means 256
        $h = if ($s -ge 256) { 0 } else { $s }
        $bw.Write([byte]$w); $bw.Write([byte]$h)
        $bw.Write([byte]0); $bw.Write([byte]0)    # colors/reserved
        $bw.Write([uint16]1); $bw.Write([uint16]1) # planes
        $bw.Write([uint32]$img.png.Length)
        $bw.Write([uint32]$dataOffset)
        $dataOffset += $img.png.Length
    }
    foreach ($img in $images) { $bw.Write($img.png) }
    $bw.Flush(); $bw.Close(); $fs.Close()
    Write-Output "Wrote $icoPath with $($images.Count) images, size $((Get-Item $icoPath).Length) bytes"

    # Plain brand mark for docs/assets: 256 px, cyan only (no halo).
    $png256 = Join-Path $PSScriptRoot "..\assets\beam-256.png"
    dotnet fsi $fsxPath -- --svg $svgPath --out $png256 --size 256 --no-halo
    if ($LASTEXITCODE -ne 0) { throw "render-icon.fsx failed for beam-256.png" }
}
finally {
    if (Test-Path $tempDir) { Remove-Item $tempDir -Recurse -Force }
}
