# Converts Icon.png (project root) into a multi-size src\app.ico, clipping the white corners
# outside the icon's rounded square so they become transparent.
param([string]$Png = (Join-Path $PSScriptRoot '..\Icon.png'), [string]$Out = (Join-Path $PSScriptRoot 'app.ico'))
Add-Type -AssemblyName System.Drawing
$src = [System.Drawing.Image]::FromFile((Resolve-Path $Png))

function Draw([int]$s) {
    $bmp = New-Object System.Drawing.Bitmap $s, $s, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'; $g.InterpolationMode = 'HighQualityBicubic'; $g.PixelOffsetMode = 'HighQuality'
    $g.Clear([System.Drawing.Color]::Transparent)
    $inset = $s * 0.012; $w = $s - 2 * $inset; $r = $w * 0.175
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $path.AddArc($inset, $inset, 2 * $r, 2 * $r, 180, 90)
    $path.AddArc($inset + $w - 2 * $r, $inset, 2 * $r, 2 * $r, 270, 90)
    $path.AddArc($inset + $w - 2 * $r, $inset + $w - 2 * $r, 2 * $r, 2 * $r, 0, 90)
    $path.AddArc($inset, $inset + $w - 2 * $r, 2 * $r, 2 * $r, 90, 90)
    $path.CloseFigure()
    $g.SetClip($path)
    $g.DrawImage($src, 0, 0, $s, $s)
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
