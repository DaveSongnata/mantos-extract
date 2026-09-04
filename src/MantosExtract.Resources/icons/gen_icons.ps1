# gen_icons.ps1 — derives the MantosExtract app icon (16/24/32/48/256 PNGs + mantosextract.ico)
# from the approved brand mark (source-logo.jpg, Swiss Style: camisa + corte diagonal, gerada
# no ChatGPT/Gemini, aprovada pelo Dave em 2026-09-04).
#
# Two crops of the SAME source art, not two different designs:
#   - "core"  (16/24/32/48): só o glifo central (camisa + faixa de corte) — os cantos de
#     registro e as cruzes "+" viram ruído ilegível em 16px, então ficam de fora dos tamanhos
#     pequenos.
#   - "full"  (256): a arte completa, com as marcas de canto — nesse tamanho elas ainda se
#     leem e reforçam a identidade Swiss Style.
# As coordenadas de corte vêm de segmentação por componentes conectados (achar o maior blob
# central e separá-lo das marcas isoladas de canto), não de um chute visual — script único,
# não repetido aqui para manter o arquivo enxuto; os números abaixo são o resultado medido.
#
#   powershell -ExecutionPolicy Bypass -File gen_icons.ps1 [outDir]
Add-Type -AssemblyName System.Drawing
$ErrorActionPreference = 'Stop'

$outDir = $args[0]
if (-not $outDir) { $outDir = $PSScriptRoot }
if (-not (Test-Path $outDir)) { New-Item -ItemType Directory -Force -Path $outDir | Out-Null }

$srcPath = Join-Path $PSScriptRoot 'source-logo.jpg'
if (-not (Test-Path $srcPath)) { throw "source-logo.jpg nao encontrado em $PSScriptRoot" }

# Bounding boxes medidos por segmentação de componentes conectados sobre o source 2048x2048
# (fundo #f1eee7, threshold de distância de cor > 28, blobs 4-conectados). O glifo central
# (camisa + corte + os dois acentos internos) é o maior componente + os dois pequenos que
# ficam DENTRO da área dele; as quatro marcas de canto e as cruzes "+" são componentes
# menores e isolados perto da borda, medidamente fora dessa caixa.
$coreBox = @{ X = 399; Y = 433; W = 1166; H = 1166 }   # quadrado centrado no glifo, +8% de folga
$fullBox = @{ X = 0;   Y = 0;   W = 2048; H = 2048 }   # arte completa (com molduras de canto)

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

$src = [System.Drawing.Image]::FromFile($srcPath)

$sizes = @(256, 48, 32, 24, 16)
$pngs = @{}
foreach ($s in $sizes) {
  $box = if ($s -ge 256) { $fullBox } else { $coreBox }
  $bmp = New-CroppedResize $src $box $s
  $path = Join-Path $outDir ("mantosextract-$s.png")
  $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
  $pngs[$s] = $path
  $bmp.Dispose()
}
$src.Dispose()

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

# login-mark.png: core crop, fundo transparente (key-out com feather de anti-aliasing), usado
# como <img> inline (base64) na tela de login do addin (src/MantosExtract.AddIn/wwwroot/index.html,
# .login-mark img) — o texto "login-mark" NÃO é a badge do Windows, é o mark web-facing.
$src2 = [System.Drawing.Image]::FromFile($srcPath)
$markBmp = New-CroppedResize $src2 $coreBox 160
$src2.Dispose()
$bg = $markBmp.GetPixel(2, 2)
$t1 = 18.0; $t2 = 42.0
for ($y = 0; $y -lt $markBmp.Height; $y++) {
  for ($x = 0; $x -lt $markBmp.Width; $x++) {
    $p = $markBmp.GetPixel($x, $y)
    $dr = $p.R - $bg.R; $dg = $p.G - $bg.G; $db = $p.B - $bg.B
    $dist = [Math]::Sqrt($dr*$dr + $dg*$dg + $db*$db)
    if ($dist -le $t1) {
      $markBmp.SetPixel($x, $y, [System.Drawing.Color]::FromArgb(0, $p.R, $p.G, $p.B))
    } elseif ($dist -lt $t2) {
      $a = [int](255.0 * ($dist - $t1) / ($t2 - $t1))
      $markBmp.SetPixel($x, $y, [System.Drawing.Color]::FromArgb($a, $p.R, $p.G, $p.B))
    }
  }
}
$markBmp.Save((Join-Path $outDir 'login-mark.png'), [System.Drawing.Imaging.ImageFormat]::Png)
$markBmp.Dispose()

Write-Output "Generated MantosExtract icons in $outDir"
