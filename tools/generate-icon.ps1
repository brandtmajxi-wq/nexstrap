<#
  Renders the Nexstrap mark into PNG + multi-size ICO files.
  The geometry mirrors Resources/Themes/Branding.xaml (100x100 design grid):
    - rounded charcoal tile
    - "N" built from a left leg, a diagonal and a short right leg
    - a detached square "spark" in the top-right corner
  Usage: powershell -File tools/generate-icon.ps1
#>
param(
    [string]$OutDir = (Join-Path $PSScriptRoot '..\src\Nexstrap\Resources\Branding')
)

Add-Type -AssemblyName System.Drawing
New-Item -ItemType Directory -Force $OutDir | Out-Null

function New-RoundedRect([single]$x, [single]$y, [single]$w, [single]$h, [single]$r) {
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $r * 2
    $p.AddArc($x, $y, $d, $d, 180, 90)
    $p.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $p.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
    $p.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $p.CloseFigure()
    return $p
}

function Render-Mark([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.PixelOffsetMode = 'HighQuality'
    $g.Clear([System.Drawing.Color]::Transparent)
    $s = $size / 100.0
    $g.ScaleTransform($s, $s)

    # Tile
    $tile = New-RoundedRect 2 2 96 96 24
    $tileBrush = New-Object System.Drawing.Drawing2D.LinearGradientBrush (New-Object System.Drawing.PointF 0, 0), (New-Object System.Drawing.PointF 100, 100), ([System.Drawing.Color]::FromArgb(255, 0x1C, 0x1D, 0x24)), ([System.Drawing.Color]::FromArgb(255, 0x10, 0x11, 0x15))
    $g.FillPath($tileBrush, $tile)
    $pen = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(60, 255, 255, 255)), 1.5
    $g.DrawPath($pen, $tile)

    # Mark
    $mark = New-Object System.Drawing.Drawing2D.GraphicsPath
    $mark.FillMode = 'Winding'
    $mark.AddPath((New-RoundedRect 22 22 14 56 3), $false)
    $mark.AddPolygon([System.Drawing.PointF[]]@(
        (New-Object System.Drawing.PointF 22, 22), (New-Object System.Drawing.PointF 32, 22),
        (New-Object System.Drawing.PointF 78, 68), (New-Object System.Drawing.PointF 78, 78),
        (New-Object System.Drawing.PointF 68, 78), (New-Object System.Drawing.PointF 22, 32)))
    $mark.AddPath((New-RoundedRect 64 44 14 34 3), $false)
    $markBrush = New-Object System.Drawing.Drawing2D.LinearGradientBrush (New-Object System.Drawing.PointF 20, 80), (New-Object System.Drawing.PointF 80, 20), ([System.Drawing.Color]::FromArgb(255, 0x7C, 0x6C, 0xFF)), ([System.Drawing.Color]::FromArgb(255, 0x22, 0xD3, 0xEE))
    $g.FillPath($markBrush, $mark)

    $spark = New-RoundedRect 64 22 14 14 3
    $g.FillPath((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 0x22, 0xD3, 0xEE))), $spark)

    $g.Dispose()
    return $bmp
}

# PNG for in-app use
$png = Render-Mark 256
$png.Save((Join-Path $OutDir 'Nexstrap-256.png'), [System.Drawing.Imaging.ImageFormat]::Png)
$png.Dispose()

# Large PNG for the Discord application's "nexstrap" Rich Presence art asset (not embedded in the app)
$discordDir = Join-Path $PSScriptRoot '..\branding'
New-Item -ItemType Directory -Force $discordDir | Out-Null
$big = Render-Mark 1024
$big.Save((Join-Path $discordDir 'nexstrap-discord-1024.png'), [System.Drawing.Imaging.ImageFormat]::Png)
$big.Dispose()

# ICO with PNG-compressed entries (supported since Windows Vista)
$sizes = 16, 24, 32, 48, 64, 128, 256
$images = foreach ($sz in $sizes) {
    $b = Render-Mark $sz
    $ms = New-Object System.IO.MemoryStream
    $b.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $b.Dispose()
    , $ms.ToArray()
}

$ico = New-Object System.IO.MemoryStream
$w = New-Object System.IO.BinaryWriter $ico
$w.Write([UInt16]0); $w.Write([UInt16]1); $w.Write([UInt16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $sz = $sizes[$i]; $data = $images[$i]
    $dim = if ($sz -ge 256) { 0 } else { $sz }
    $w.Write([byte]$dim); $w.Write([byte]$dim); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([UInt16]1); $w.Write([UInt16]32)
    $w.Write([UInt32]$data.Length); $w.Write([UInt32]$offset)
    $offset += $data.Length
}
foreach ($data in $images) { $w.Write($data) }
$w.Flush()
[System.IO.File]::WriteAllBytes((Join-Path $OutDir 'Nexstrap.ico'), $ico.ToArray())
Write-Host "Icon written to $OutDir"
