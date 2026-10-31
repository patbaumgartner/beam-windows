Add-Type -AssemblyName System.Drawing

# Beam brand — Pantone 638C (#00AED8) and a deeper 638C-tinted outline for light modes.
$Brand   = [System.Drawing.Color]::FromArgb(0x00, 0xAE, 0xD8)
$BrandDeep = [System.Drawing.Color]::FromArgb(0x00, 0x80, 0xB4)
$White   = [System.Drawing.Color]::White

function Draw-ProjectorIcon([System.Drawing.Bitmap]$bmp, [int]$size) {
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.Clear([System.Drawing.Color]::Transparent)

    $brandBrush = New-Object System.Drawing.SolidBrush $Brand
    $deepBrush  = New-Object System.Drawing.SolidBrush $BrandDeep
    $whiteBrush = New-Object System.Drawing.SolidBrush $White

    # Beam cone — a filled polygon from the emitter (right) widening to the left.
    $cx1 = [float]($size * 0.80); $cy1 = [float]($size * 0.42)
    $cx2 = [float]($size * 0.80); $cy2 = [float]($size * 0.58)
    $cx3 = [float]($size * 0.04); $cy3 = [float]($size * 0.88)
    $cx4 = [float]($size * 0.04); $cy4 = [float]($size * 0.12)
    $cone = [System.Drawing.PointF[]]@(
        (New-Object System.Drawing.PointF($cx1, $cy1)),
        (New-Object System.Drawing.PointF($cx2, $cy2)),
        (New-Object System.Drawing.PointF($cx3, $cy3)),
        (New-Object System.Drawing.PointF($cx4, $cy4))
    )
    $g.FillPolygon($brandBrush, $cone)

    # Deep outline so the cone separates from pale (light-mode) taskbars.
    $outline = New-Object System.Drawing.Pen $BrandDeep, ([float]([Math]::Max(1, $size * 0.05)))
    $outline.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
    $g.DrawPolygon($outline, $cone)

    # White hot core — the inner bright ray that carries the mark on dark taskbars.
    $kx1 = [float]($size * 0.60); $ky1 = [float]($size * 0.47)
    $kx2 = [float]($size * 0.60); $ky2 = [float]($size * 0.53)
    $kx3 = [float]($size * 0.06); $ky3 = [float]($size * 0.68)
    $kx4 = [float]($size * 0.06); $ky4 = [float]($size * 0.32)
    $core = [System.Drawing.PointF[]]@(
        (New-Object System.Drawing.PointF($kx1, $ky1)),
        (New-Object System.Drawing.PointF($kx2, $ky2)),
        (New-Object System.Drawing.PointF($kx3, $ky3)),
        (New-Object System.Drawing.PointF($kx4, $ky4))
    )
    $g.FillPolygon($whiteBrush, $core)

    # Hotspot at the emitter — the lens.
    $lensR = [float]($size * 0.11)
    $lensX = [float]($size * 0.82)
    $lensY = [float]($size * 0.50)
    $g.FillEllipse($deepBrush,  ($lensX - $lensR), ($lensY - $lensR), ($lensR * 2), ($lensR * 2))
    $g.FillEllipse($whiteBrush, ($lensX - $lensR * 0.45), ($lensY - $lensR * 0.45), ($lensR * 0.9), ($lensR * 0.9))

    $brandBrush.Dispose(); $deepBrush.Dispose(); $whiteBrush.Dispose(); $outline.Dispose()
    $g.Dispose()
}

$sizes = @(16, 20, 24, 32, 40, 48, 64, 128, 256)
$images = @()
$dir = [System.IO.Path]::Combine([System.IO.Path]::GetTempPath(), "beamico")
New-Item -ItemType Directory -Force -Path $dir | Out-Null
foreach ($s in $sizes) {
    $bmp = New-Object System.Drawing.Bitmap($s, $s, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    Draw-ProjectorIcon $bmp $s
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $png = $ms.ToArray()
    $bp = New-Object PSObject -Property @{ size=$s; png=$png }
    $images += $bp
    $bmp.Dispose(); $ms.Dispose()
}
# Build ICO: header
$icoPath = Join-Path $PSScriptRoot "..\src\Beam.Tray\Assets\app.ico"
$entries = @()
$dataOffset = 6 + ($images.Count * 16)
$fs = [System.IO.File]::Create($icoPath)
$bw = New-Object System.IO.BinaryWriter($fs)
$bw.Write([uint16]0); $bw.Write([uint16]1); $bw.Write([uint16]$images.Count)
foreach ($img in $images) {
    $s = $img.size
    $w = if ($s -ge 256) { 0 } else { $s }
    $h = if ($s -ge 256) { 0 } else { $s }
    $bw.Write([byte]$w); $bw.Write([byte]$h)
    $bw.Write([byte]0); $bw.Write([byte]0) # colors/reserved
    $pw = 1; $ph = 1
    $bw.Write([uint16]$pw); $bw.Write([uint16]$ph)
    $bw.Write([uint32]$img.png.Length)
    $bw.Write([uint32]$dataOffset)
    $dataOffset += $img.png.Length
}
foreach ($img in $images) { $bw.Write($img.png) }
$bw.Flush(); $bw.Close(); $fs.Close()
Write-Output "Wrote $icoPath with $($images.Count) images, size $((Get-Item $icoPath).Length) bytes"
