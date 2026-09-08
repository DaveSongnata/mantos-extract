# gen_icons.ps1 — derives the MantosExtract app icon (16/24/32/48/256 PNGs + mantosextract.ico)
# from the approved brand mark (brand-mark.png — the standalone "X" glyph from the real logo
# family Dave approved 2026-09-08: brand-mark.png/brand-wordmark.png/brand-lockup.png, colors
# measured by real pixel sampling, not guessed: #FCB400 orange dominant, #0078FC blue accent).
#
# Rewritten 2026-09-08 (Dave asked for a new color palette + new mark, replacing the 2026-09-04
# "camisa + corte diagonal" art entirely) — the OLD source-logo.jpg used hand-measured connected-
# component bounding boxes specific to THAT composition (corner registration marks to crop out
# etc.). brand-mark.png is already a clean, self-contained, transparent PNG glyph with nothing to
# segment out, so the approach is simpler: trim the transparent padding to the glyph's real
# bounds, re-pad to a square canvas with a small margin, then resize down — no color-keying, no
# blob segmentation, because there's no background/corner-mark noise to remove in this source.
#
#   powershell -ExecutionPolicy Bypass -File gen_icons.ps1 [outDir]
Add-Type -AssemblyName System.Drawing
$ErrorActionPreference = 'Stop'

$outDir = $args[0]
if (-not $outDir) { $outDir = $PSScriptRoot }
if (-not (Test-Path $outDir)) { New-Item -ItemType Directory -Force -Path $outDir | Out-Null }

$srcPath = Join-Path $PSScriptRoot 'brand-mark.png'
if (-not (Test-Path $srcPath)) { throw "brand-mark.png nao encontrado em $PSScriptRoot" }

# ---- trim transparent padding down to the glyph's real bounding box -------------------------
function Get-OpaqueBounds([System.Drawing.Bitmap]$bmp, [int]$alphaThreshold) {
  $minX = $bmp.Width; $maxX = -1; $minY = $bmp.Height; $maxY = -1
  for ($y = 0; $y -lt $bmp.Height; $y++) {
    for ($x = 0; $x -lt $bmp.Width; $x++) {
      if ($bmp.GetPixel($x, $y).A -gt $alphaThreshold) {
        if ($x -lt $minX) { $minX = $x }
        if ($x -gt $maxX) { $maxX = $x }
        if ($y -lt $minY) { $minY = $y }
        if ($y -gt $maxY) { $maxY = $y }
      }
    }
  }
  if ($maxX -lt 0) { throw "brand-mark.png parece totalmente transparente" }
  return @{ X = $minX; Y = $minY; W = ($maxX - $minX + 1); H = ($maxY - $minY + 1) }
}

function New-CroppedResize([System.Drawing.Image]$src, [hashtable]$box, [int]$size) {
  $bmp = New-Object System.Drawing.Bitmap($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
  $g = [System.Drawing.Graphics]::FromImage($bmp)
  $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
  $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
  $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
  $g.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
  $srcRect = New-Object System.Drawing.Rectangle($box.X, $box.Y, $box.W, $box.H)
  $dstRect = New-Object System.Drawing.Rectangle(0, 0, $size, $size)
  $g.DrawImage($src, $dstRect, $srcRect, [System.Drawing.GraphicsUnit]::Pixel)
  $g.Dispose()
  return $bmp
}

$srcBmp = New-Object System.Drawing.Bitmap($srcPath)
$trimmed = Get-OpaqueBounds $srcBmp 10

# Pad the trimmed glyph to a square, centered, with ~8% margin on each side (so the mark doesn't
# touch the icon's own edge), THEN treat that square as the crop box for every output size —
# same two-step idea as before, just one framing now instead of "core vs full" (this source has
# no corner marks that need a bigger 256px-only framing).
$side = [Math]::Round(([Math]::Max($trimmed.W, $trimmed.H)) * 1.16)
$squareBox = @{
  X = $trimmed.X - [Math]::Round(($side - $trimmed.W) / 2.0)
  Y = $trimmed.Y - [Math]::Round(($side - $trimmed.H) / 2.0)
  W = $side
  H = $side
}

$sizes = @(256, 48, 32, 24, 16)
$pngs = @{}
foreach ($s in $sizes) {
  $bmp = New-CroppedResize $srcBmp $squareBox $s
  $path = Join-Path $outDir ("mantosextract-$s.png")
  $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
  $pngs[$s] = $path
  $bmp.Dispose()
}

# login-mark.png: mesmo enquadramento, 160px, usado como <img> inline (base64) na tela de login
# do addin (src/MantosExtract.AddIn/wwwroot/index.html, .login-mark .badge) — já vem transparente
# de fábrica (brand-mark.png é um PNG com alpha real), sem key-out por cor precisar rodar.
$markBmp = New-CroppedResize $srcBmp $squareBox 160
$markBmp.Save((Join-Path $outDir 'login-mark.png'), [System.Drawing.Imaging.ImageFormat]::Png)
$markBmp.Dispose()
$srcBmp.Dispose()

# Multi-resolution .ico (PNG frames; Vista+/Win10/11 — mesma técnica de ../../../optimus).
$ms = New-Object System.IO.MemoryStream
$bw = New-Object System.IO.BinaryWriter($ms)
$bw.Write([UInt16]0); $bw.Write([UInt16]1); $bw.Write([UInt16]$sizes.Count)
$imgData = @{}
foreach ($s in $sizes) { $imgData[$s] = [System.IO.File]::ReadAllBytes($pngs[$s]) }
$offset = 6 + 16*$sizes.Count
foreach ($s in $sizes) {
  $bytes = $imgData[$s]
  $wByte = if ($s -ge 256) {0} else {$s}
  $bw.Write([Byte]$wByte); $bw.Write([Byte]$wByte); $bw.Write([Byte]0); $bw.Write([Byte]0)
  $bw.Write([UInt16]1); $bw.Write([UInt16]32)
  $bw.Write([UInt32]$bytes.Length); $bw.Write([UInt32]$offset)
  $offset += $bytes.Length
}
foreach ($s in $sizes) { $bw.Write($imgData[$s]) }
$bw.Flush()
[System.IO.File]::WriteAllBytes((Join-Path $outDir 'mantosextract.ico'), $ms.ToArray())
$bw.Dispose(); $ms.Dispose()

Write-Output "Generated MantosExtract icons in $outDir"
