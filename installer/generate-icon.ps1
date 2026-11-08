Add-Type -AssemblyName System.Drawing

# Beam brand — Pantone 638C (#00AED8) fill with a deeper 638C-tinted halo for light modes.
$Brand     = [System.Drawing.Color]::FromArgb(0x00, 0xAE, 0xD8)
$BrandDeep = [System.Drawing.Color]::FromArgb(0x00, 0x80, 0xB4)

# The brand mark: "high beam" icon by I Create Stuff from The Noun Project (CC BY 3.0 —
# see ..\assets\ATTRIBUTION.md), stored unmodified as a black silhouette with alpha.
# It is tinted at draw time via a colour matrix so the repo asset stays the upstream art.
$markPath = Join-Path $PSScriptRoot "..\src\Beam.Tray\Assets\high-beam.png"
$mark = [System.Drawing.Bitmap]::new($markPath)

function Get-TintAttributes([System.Drawing.Color]$color) {
    # Identity matrix with constant RGB offsets and alpha passed through.
    $cm = [System.Drawing.Imaging.ColorMatrix]::new()
    $cm.Matrix04 = $color.R / 255.0
    $cm.Matrix14 = $color.G / 255.0
    $cm.Matrix24 = $color.B / 255.0
    $ia = [System.Drawing.Imaging.ImageAttributes]::new()
    $ia.SetColorMatrix($cm)
    return $ia
}

$brandAttr = Get-TintAttributes $Brand
$haloAttr  = Get-TintAttributes $BrandDeep

function Draw-TintedMark([System.Drawing.Graphics]$g, [System.Drawing.Image]$img, [System.Drawing.RectangleF]$dest, [System.Drawing.RectangleF]$src, [System.Drawing.Imaging.ImageAttributes]$attr) {
    # Graphics.DrawImage has no (Image, RectangleF, RectangleF, GraphicsUnit, ImageAttributes)
    # overload, and pwsh mis-binds a RectangleF destination to the Point[] variants — so pass
    # the destination as the 3-point parallelogram (top-left, top-right, bottom-left).
    $pts = [System.Drawing.PointF[]]@(
        [System.Drawing.PointF]::new($dest.X, $dest.Y),
        [System.Drawing.PointF]::new($dest.X + $dest.Width, $dest.Y),
        [System.Drawing.PointF]::new($dest.X, $dest.Y + $dest.Height))
    $g.DrawImage($img, $pts, $src, [System.Drawing.GraphicsUnit]::Pixel, $attr)
}

function Draw-BeamIcon([System.Drawing.Bitmap]$bmp, [int]$size) {
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.Clear([System.Drawing.Color]::Transparent)

    $srcRect = [System.Drawing.RectangleF]::new(0, 0, $mark.Width, $mark.Height)

    # Halo — the silhouette drawn ~8% larger in the deep edge colour, centred, so the
    # mark separates from pale (light-mode) taskbars.
    $halo = [float]($size * 1.08)
    $haloRect = [System.Drawing.RectangleF]::new((($size - $halo) / 2), (($size - $halo) / 2), $halo, $halo)
    Draw-TintedMark $g $mark $haloRect $srcRect $haloAttr

    # Brand fill — Pantone 638C.
    $rect = [System.Drawing.RectangleF]::new(0, 0, $size, $size)
    Draw-TintedMark $g $mark $rect $srcRect $brandAttr

    $g.Dispose()
}

$sizes = @(16, 20, 24, 32, 40, 48, 64, 128, 256)
$images = @()
foreach ($s in $sizes) {
    $bmp = [System.Drawing.Bitmap]::new($s, $s, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    Draw-BeamIcon $bmp $s
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
