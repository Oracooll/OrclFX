# Converts Icon.png (project root) into a multi-size src\app.ico.
# A transparent image (the folder icon) is cropped to its visible part, so the small sizes stay legible.
# Near-solid pixels are made fully solid, and faint specks are made fully transparent. An image without
# transparency (the original rounded-square icon) has the white corners outside its rounded square clipped.
param([string]$Png = (Join-Path $PSScriptRoot '..\Icon.png'), [string]$Out = (Join-Path $PSScriptRoot 'app.ico'))
Add-Type -AssemblyName System.Drawing
$src = New-Object System.Drawing.Bitmap ([System.Drawing.Image]::FromFile((Resolve-Path $Png)))
$full = New-Object System.Drawing.Rectangle 0, 0, $src.Width, $src.Height

# Clean up the alpha channel and find the visible area.
$d = $src.LockBits($full, 'ReadWrite', 'Format32bppArgb')
$n = $d.Stride * $src.Height; $px = New-Object byte[] $n
[Runtime.InteropServices.Marshal]::Copy($d.Scan0, $px, 0, $n)
$transparent = $false; $minX = $src.Width; $minY = $src.Height; $maxX = -1; $maxY = -1
for ($y = 0; $y -lt $src.Height; $y++) {
    $row = $y * $d.Stride
    for ($x = 0; $x -lt $src.Width; $x++) {
        $i = $row + $x * 4 + 3; $a = $px[$i]
        if ($a -lt 255) { $transparent = $true }
        if ($a -le 8) { $px[$i] = 0; continue }
        if ($a -ge 245) { $px[$i] = 255 }
        if ($a -gt 40) { if ($x -lt $minX) { $minX = $x }; if ($x -gt $maxX) { $maxX = $x }; if ($y -lt $minY) { $minY = $y }; if ($y -gt $maxY) { $maxY = $y } }
    }
}
[Runtime.InteropServices.Marshal]::Copy($px, 0, $d.Scan0, $n)
$src.UnlockBits($d)

# The square to draw from: the visible area plus a 3% margin, or the whole image.
$crop = $full
if ($transparent -and $maxX -ge 0) {
    $side = [Math]::Max($maxX - $minX, $maxY - $minY) * 1.06
    $cx = ($minX + $maxX) / 2; $cy = ($minY + $maxY) / 2
    $crop = New-Object System.Drawing.Rectangle ([int]($cx - $side / 2)), ([int]($cy - $side / 2)), ([int]$side), ([int]$side)
}

function Draw([int]$s) {
    $bmp = New-Object System.Drawing.Bitmap $s, $s, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'; $g.InterpolationMode = 'HighQualityBicubic'; $g.PixelOffsetMode = 'HighQuality'
    $g.Clear([System.Drawing.Color]::Transparent)
    if (-not $transparent) {
        $inset = $s * 0.012; $w = $s - 2 * $inset; $r = $w * 0.175
        $path = New-Object System.Drawing.Drawing2D.GraphicsPath
        $path.AddArc($inset, $inset, 2 * $r, 2 * $r, 180, 90)
        $path.AddArc($inset + $w - 2 * $r, $inset, 2 * $r, 2 * $r, 270, 90)
        $path.AddArc($inset + $w - 2 * $r, $inset + $w - 2 * $r, 2 * $r, 2 * $r, 0, 90)
        $path.AddArc($inset, $inset + $w - 2 * $r, 2 * $r, 2 * $r, 90, 90)
        $path.CloseFigure()
        $g.SetClip($path)
    }
    $attr = New-Object System.Drawing.Imaging.ImageAttributes
    $attr.SetWrapMode([System.Drawing.Drawing2D.WrapMode]::TileFlipXY) # no dark fringe at the edges
    $g.DrawImage($src, (New-Object System.Drawing.Rectangle 0, 0, $s, $s), $crop.X, $crop.Y, $crop.Width, $crop.Height, [System.Drawing.GraphicsUnit]::Pixel, $attr)
    $g.Dispose()
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    return ,$ms.ToArray()
}

$sizes = 16, 20, 24, 32, 40, 48, 64, 128, 256
$images = @(); foreach ($s in $sizes) { $images += ,(Draw $s) }
$src.Dispose()
$fs = [System.IO.File]::Create($Out)
$bw = New-Object System.IO.BinaryWriter $fs
$bw.Write([UInt16]0); $bw.Write([UInt16]1); $bw.Write([UInt16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]; $d = $images[$i]
    $bw.Write([byte]($s % 256)); $bw.Write([byte]($s % 256)); $bw.Write([byte]0); $bw.Write([byte]0)
    $bw.Write([UInt16]1); $bw.Write([UInt16]32); $bw.Write([UInt32]$d.Length); $bw.Write([UInt32]$offset)
    $offset += $d.Length
}
foreach ($d in $images) { $bw.Write($d) }
$bw.Close()
