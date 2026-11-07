# Generates app.ico (multi-size, PNG-compressed) used as the exe icon.
# Run once, or again if you change the design:  powershell -ExecutionPolicy Bypass -File make-icon.ps1
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

function New-IconPng([int]$s) {
    $bmp = New-Object System.Drawing.Bitmap $s, $s
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.Clear([System.Drawing.Color]::Transparent)

    # rounded dark background
    $r = [single]($s * 0.2); $d = $r * 2; $m = [single]($s * 0.03); $w = [single]($s - $m * 2)
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $path.AddArc($m, $m, $d, $d, 180, 90)
    $path.AddArc($m + $w - $d, $m, $d, $d, 270, 90)
    $path.AddArc($m + $w - $d, $m + $w - $d, $d, $d, 0, 90)
    $path.AddArc($m, $m + $w - $d, $d, $d, 90, 90)
    $path.CloseFigure()
    $bg = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 20, 22, 27))
    $g.FillPath($bg, $path)

    # four meter bars
    $colors = @(
        [System.Drawing.Color]::FromArgb(126, 217, 87),
        [System.Drawing.Color]::FromArgb(245, 225, 74),
        [System.Drawing.Color]::FromArgb(255, 166, 48),
        [System.Drawing.Color]::FromArgb(92, 200, 255))
    $heights = @(0.30, 0.58, 0.42, 0.70)
    $barW = [single]($s * 0.15); $gap = [single]($s * 0.06)
    $x0 = [single](($s - ($barW * 4 + $gap * 3)) / 2); $base = [single]($s * 0.84)
    for ($i = 0; $i -lt 4; $i++) {
        $h = [single]($s * $heights[$i])
        $b = New-Object System.Drawing.SolidBrush $colors[$i]
        $g.FillRectangle($b, [single]($x0 + $i * ($barW + $gap)), [single]($base - $h), $barW, $h)
        $b.Dispose()
    }
    $g.Dispose()
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    return ,$ms.ToArray()
}

$sizes = 16, 24, 32, 48, 64, 256
$images = @(); foreach ($s in $sizes) { $images += ,(New-IconPng $s) }

$out = Join-Path $PSScriptRoot 'app.ico'
$fs = [System.IO.File]::Create($out)
$bw = New-Object System.IO.BinaryWriter $fs
$bw.Write([uint16]0); $bw.Write([uint16]1); $bw.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $dim = if ($sizes[$i] -ge 256) { 0 } else { $sizes[$i] }
    $bw.Write([byte]$dim); $bw.Write([byte]$dim); $bw.Write([byte]0); $bw.Write([byte]0)
    $bw.Write([uint16]1); $bw.Write([uint16]32)
    $bw.Write([uint32]$images[$i].Length); $bw.Write([uint32]$offset)
    $offset += $images[$i].Length
}
foreach ($img in $images) { $bw.Write($img) }
$bw.Close()
Write-Host "Wrote: $out"
