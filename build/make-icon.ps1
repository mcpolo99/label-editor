<#
.SYNOPSIS
  Generates the Label Editor logo: white "LE" on a blue rounded tile.
.DESCRIPTION
  Writes src/LabelEditor.App/Assets/app.ico (16-256 px, PNG-compressed entries) and logo-256.png.
  Re-run after changing colours or text; the outputs are committed.
#>
param(
    [string]$Background = "#0B5CAD",
    [string]$Foreground = "#FFFFFF",
    [string]$Text = "LE"
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing
$assets = Join-Path (Split-Path -Parent $PSScriptRoot) "src/LabelEditor.App/Assets"
New-Item -ItemType Directory -Force $assets | Out-Null

function New-LogoPng([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAliasGridFit
    $g.Clear([System.Drawing.Color]::Transparent)

    # Rounded tile (corner radius ~18 % of the size; square at 16 px where rounding just blurs).
    $radius = if ($size -le 16) { 2 } else { [math]::Round($size * 0.18) }
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $radius * 2
    $path.AddArc(0, 0, $d, $d, 180, 90)
    $path.AddArc($size - $d - 1, 0, $d, $d, 270, 90)
    $path.AddArc($size - $d - 1, $size - $d - 1, $d, $d, 0, 90)
    $path.AddArc(0, $size - $d - 1, $d, $d, 90, 90)
    $path.CloseFigure()
    $brush = New-Object System.Drawing.SolidBrush ([System.Drawing.ColorTranslator]::FromHtml($Background))
    $g.FillPath($brush, $path)

    # Text, scaled to fill ~72 % of the tile width and centred optically.
    $fontSize = $size * 0.52
    $font = New-Object System.Drawing.Font "Segoe UI", $fontSize, ([System.Drawing.FontStyle]::Bold), ([System.Drawing.GraphicsUnit]::Pixel)
    $format = [System.Drawing.StringFormat]::GenericTypographic
    $measured = $g.MeasureString($Text, $font, [System.Drawing.PointF]::Empty, $format)
    $scale = [math]::Min(1.0, ($size * 0.74) / $measured.Width)
    if ($scale -lt 1.0) {
        $font.Dispose()
        $font = New-Object System.Drawing.Font "Segoe UI", ($fontSize * $scale), ([System.Drawing.FontStyle]::Bold), ([System.Drawing.GraphicsUnit]::Pixel)
        $measured = $g.MeasureString($Text, $font, [System.Drawing.PointF]::Empty, $format)
    }
    $textBrush = New-Object System.Drawing.SolidBrush ([System.Drawing.ColorTranslator]::FromHtml($Foreground))
    $x = ($size - $measured.Width) / 2
    $y = ($size - $measured.Height) / 2 - ($size * 0.02)
    $g.DrawString($Text, $font, $textBrush, $x, $y, $format)

    $stream = New-Object System.IO.MemoryStream
    $bmp.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
    $g.Dispose(); $bmp.Dispose(); $font.Dispose(); $brush.Dispose(); $textBrush.Dispose(); $path.Dispose()
    return , $stream.ToArray()
}

$sizes = 16, 24, 32, 48, 64, 128, 256
$images = @{}
foreach ($s in $sizes) { $images[$s] = New-LogoPng $s }
[System.IO.File]::WriteAllBytes((Join-Path $assets "logo-256.png"), $images[256])

# ICO container: ICONDIR + ICONDIRENTRY per image + PNG payloads.
$ico = New-Object System.IO.MemoryStream
$w = New-Object System.IO.BinaryWriter $ico
$w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$sizes.Count)
$offset = 6 + (16 * $sizes.Count)
foreach ($s in $sizes) {
    $dim = if ($s -ge 256) { 0 } else { $s }
    $w.Write([byte]$dim); $w.Write([byte]$dim); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([uint16]1); $w.Write([uint16]32)
    $w.Write([uint32]$images[$s].Length); $w.Write([uint32]$offset)
    $offset += $images[$s].Length
}
foreach ($s in $sizes) { $w.Write($images[$s]) }
$w.Flush()
[System.IO.File]::WriteAllBytes((Join-Path $assets "app.ico"), $ico.ToArray())
Write-Host "Wrote $assets/app.ico and logo-256.png"
