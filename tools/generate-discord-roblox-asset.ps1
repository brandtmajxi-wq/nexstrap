<#
  Renders the "roblox" Rich Presence art asset (1024x1024) for the Discord application:
  a tilted white square with a square cut-out on blue, matching the reference image.
  Usage: powershell -File tools/generate-discord-roblox-asset.ps1
#>
Add-Type -AssemblyName System.Drawing
$out = Join-Path $PSScriptRoot '..\branding\roblox-discord-1024.png'
$size = 1024

$bmp = New-Object System.Drawing.Bitmap $size, $size
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.SmoothingMode = 'AntiAlias'
$g.PixelOffsetMode = 'HighQuality'

$g.Clear([System.Drawing.Color]::FromArgb(255, 0x3B, 0x5B, 0xF5))

# Tilted square with a square hole (even-odd fill makes the hole)
$path = New-Object System.Drawing.Drawing2D.GraphicsPath
$path.FillMode = 'Alternate'
$outer = 600; $inner = 170
$path.AddRectangle((New-Object System.Drawing.RectangleF (-$outer / 2), (-$outer / 2), $outer, $outer))
$path.AddRectangle((New-Object System.Drawing.RectangleF (-$inner / 2), (-$inner / 2), $inner, $inner))

$matrix = New-Object System.Drawing.Drawing2D.Matrix
$matrix.Translate($size / 2, $size / 2)
$matrix.Rotate(16)
$path.Transform($matrix)

$g.FillPath((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::White)), $path)
$g.Dispose()

New-Item -ItemType Directory -Force (Split-Path $out) | Out-Null
$bmp.Save($out, [System.Drawing.Imaging.ImageFormat]::Png)
$bmp.Dispose()
Write-Host "Wrote $out"
