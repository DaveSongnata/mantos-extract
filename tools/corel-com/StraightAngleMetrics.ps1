# Medidas e fixture do E2E do "ângulo reto" (Test-StraightAngleE2E.ps1). Separado para poder ser
# usado sozinho sobre PNGs já extraídos:
#   . tools\corel-com\StraightAngleMetrics.ps1
#   Measure-ExtractedPiece 'C:\...\GIRASSOL.png'
#   New-SkewedBadgeFixture 'C:\tmp\fixture.png'

Add-Type -AssemblyName System.Drawing
if (-not ('MantosE2E.Shape' -as [type])) {
    Add-Type -ReferencedAssemblies System.Drawing @'
using System; using System.Drawing; using System.Drawing.Imaging; using System.Runtime.InteropServices;
namespace MantosE2E {
  public static class Shape {
    // Máscara = alpha > 128. Devolve: cobertura (máscara/canvas), preenchimento da caixa
    // envolvente (máscara/bbox — um retângulo de pé dá ~1, um inclinado bem menos) e a
    // orientação do eixo principal em graus (momentos de 2ª ordem; 0 = alinhado aos eixos).
    public static double[] Measure(string path) {
      using (var src = new Bitmap(path))
      using (var bmp = src.Clone(new Rectangle(0, 0, src.Width, src.Height), PixelFormat.Format32bppArgb)) {
        var data = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        var px = new byte[data.Stride * bmp.Height];
        Marshal.Copy(data.Scan0, px, 0, px.Length);
        bmp.UnlockBits(data);
        long n = 0; double sx = 0, sy = 0, sxx = 0, syy = 0, sxy = 0;
        int minX = int.MaxValue, minY = int.MaxValue, maxX = -1, maxY = -1;
        for (int y = 0; y < bmp.Height; y++)
          for (int x = 0; x < bmp.Width; x++) {
            if (px[y * data.Stride + x * 4 + 3] <= 128) continue;
            n++; sx += x; sy += y; sxx += (double)x * x; syy += (double)y * y; sxy += (double)x * y;
            if (x < minX) minX = x; if (x > maxX) maxX = x; if (y < minY) minY = y; if (y > maxY) maxY = y;
          }
        if (n == 0) return new double[] { 0, 0, 0, bmp.Width, bmp.Height };
        double cx = sx / n, cy = sy / n;
        double mu20 = sxx / n - cx * cx, mu02 = syy / n - cy * cy, mu11 = sxy / n - cx * cy;
        double theta = 0.5 * Math.Atan2(2 * mu11, mu20 - mu02) * 180 / Math.PI;
        double bbox = (double)(maxX - minX + 1) * (maxY - minY + 1);
        return new double[] { (double)n / ((double)bmp.Width * bmp.Height), n / bbox, theta, bmp.Width, bmp.Height };
      }
    }
  }
}
'@
}

function Measure-ExtractedPiece {
    param([Parameter(Mandatory)][string]$Path)
    $m = [MantosE2E.Shape]::Measure($Path)
    [pscustomobject]@{
        Arquivo = Split-Path $Path -Leaf
        Pixels = '{0}x{1}' -f $m[3], $m[4]
        Cobertura = [Math]::Round($m[0], 3)        # 1 = sem transparência nenhuma (medida inconclusiva)
        PreencheCaixa = [Math]::Round($m[1], 3)    # ~1 = forma de pé; inclinada/cisalhada = menos
        AnguloEixo = [Math]::Round($m[2], 1)       # graus do eixo principal; 0 = alinhado
    }
}

# Foto SINTÉTICA de "estampa inclinada numa camisa": um escudo retangular 2:1 (texto "MANTOS FC 10",
# borda, faixa) girado ~15° e cisalhado, sobre um tecido laranja com dobras. A inclinação é
# CONHECIDA, então dá pra medir se a extração devolveu de pé (PreencheCaixa ~1, eixo ~0°) ou
# manteve o ângulo — sem depender de olho.
function New-SkewedBadgeFixture {
    param([Parameter(Mandatory)][string]$Path)
    $W = 1600; $H = 1200
    $canvas = New-Object System.Drawing.Bitmap($W, $H)
    $g = [System.Drawing.Graphics]::FromImage($canvas)
    $g.SmoothingMode = 'AntiAlias'; $g.TextRenderingHint = 'AntiAliasGridFit'; $g.InterpolationMode = 'HighQualityBicubic'
    $g.Clear([System.Drawing.Color]::FromArgb(232, 116, 12))
    # Dobras do tecido: faixas largas e suaves mais escuras/claras (não fazem parte da estampa).
    $rnd = New-Object System.Random(7)
    for ($i = 0; $i -lt 14; $i++) {
        $a = if ($i % 2) { 40 } else { 28 }
        $c = if ($i % 2) { [System.Drawing.Color]::FromArgb($a, 120, 40, 0) } else { [System.Drawing.Color]::FromArgb($a, 255, 210, 150) }
        $pen = New-Object System.Drawing.Pen($c, (30 + $rnd.Next(50)))
        $y0 = $rnd.Next($H)
        $g.DrawBezier($pen, -50, $y0, 500, $y0 + $rnd.Next(-200, 200), 1100, $y0 + $rnd.Next(-200, 200), $W + 50, $y0 + $rnd.Next(-150, 150))
        $pen.Dispose()
    }
    # Escudo plano 900x450.
    $bw = 900; $bh = 450
    $badge = New-Object System.Drawing.Bitmap($bw, $bh)
    $bg = [System.Drawing.Graphics]::FromImage($badge)
    $bg.SmoothingMode = 'AntiAlias'; $bg.TextRenderingHint = 'AntiAliasGridFit'
    $bg.Clear([System.Drawing.Color]::FromArgb(18, 32, 92))
    $yellow = [System.Drawing.Color]::FromArgb(252, 180, 0)
    $bg.FillRectangle((New-Object System.Drawing.SolidBrush($yellow)), 0, 300, $bw, 40)
    $bg.DrawRectangle((New-Object System.Drawing.Pen($yellow, 36)), 18, 18, $bw - 36, $bh - 36)
    $font = New-Object System.Drawing.Font('Arial Black', 96, [System.Drawing.FontStyle]::Regular, [System.Drawing.GraphicsUnit]::Pixel)
    $fmt = New-Object System.Drawing.StringFormat; $fmt.Alignment = 'Center'; $fmt.LineAlignment = 'Center'
    $bg.DrawString('MANTOS', $font, [System.Drawing.Brushes]::White, (New-Object System.Drawing.RectangleF(0, 60, $bw, 200)), $fmt)
    $small = New-Object System.Drawing.Font('Arial Black', 44, [System.Drawing.FontStyle]::Regular, [System.Drawing.GraphicsUnit]::Pixel)
    $bg.DrawString('FC  10', $small, [System.Drawing.Brushes]::White, (New-Object System.Drawing.RectangleF(0, 340, $bw, 80)), $fmt)
    $bg.Dispose()
    # Paralelogramo: largura girada 15°, altura girada e cisalhada (perspectiva "de lado").
    $pts = [System.Drawing.Point[]]@(
        (New-Object System.Drawing.Point(400, 280)),
        (New-Object System.Drawing.Point(1269, 513)),
        (New-Object System.Drawing.Point(200, 700)))
    $g.DrawImage($badge, $pts)
    $g.Dispose(); $badge.Dispose()
    New-Item -ItemType Directory -Force (Split-Path $Path) | Out-Null
    $canvas.Save($Path, [System.Drawing.Imaging.ImageFormat]::Png); $canvas.Dispose()
    return $Path
}

# Prancha em grade (várias peças, com a proporção de cada uma escrita no título) sobre xadrez.
function New-ContactSheet {
    param([Parameter(Mandatory)][string[]]$Images, [Parameter(Mandatory)][string]$Path, [int]$Columns = 4, [int]$Cell = 420)
    $pad = 16; $head = 44
    $rows = [Math]::Ceiling($Images.Count / $Columns)
    $sheet = New-Object System.Drawing.Bitmap(($Columns * ($Cell + $pad) + $pad), ($rows * ($Cell + $head + $pad) + $pad))
    $g = [System.Drawing.Graphics]::FromImage($sheet)
    $g.Clear([System.Drawing.Color]::White); $g.InterpolationMode = 'HighQualityBicubic'
    $font = New-Object System.Drawing.Font('Arial', 14, [System.Drawing.FontStyle]::Bold, [System.Drawing.GraphicsUnit]::Pixel)
    for ($i = 0; $i -lt $Images.Count; $i++) {
        $x0 = $pad + ($i % $Columns) * ($Cell + $pad); $y0 = $pad + [Math]::Floor($i / $Columns) * ($Cell + $head + $pad)
        $img = [System.Drawing.Image]::FromFile($Images[$i])
        $title = '{0}  {1}x{2} ({3})' -f [IO.Path]::GetFileNameWithoutExtension($Images[$i]), $img.Width, $img.Height, $(if ($img.Width -gt $img.Height) { 'deitada' } elseif ($img.Width -lt $img.Height) { 'em pé' } else { 'quadrada' })
        $g.DrawString($title, $font, [System.Drawing.Brushes]::Black, (New-Object System.Drawing.RectangleF($x0, $y0, $Cell, $head)))
        $yc = $y0 + $head
        for ($cy = 0; $cy -lt $Cell; $cy += 20) { for ($cx = 0; $cx -lt $Cell; $cx += 20) {
            $c = if ((($cx + $cy) / 20) % 2) { [System.Drawing.Brushes]::Gainsboro } else { [System.Drawing.Brushes]::White }
            $g.FillRectangle($c, $x0 + $cx, $yc + $cy, 20, 20) } }
        $s = [Math]::Min($Cell / $img.Width, $Cell / $img.Height)
        $w = [int]($img.Width * $s); $h = [int]($img.Height * $s)
        $g.DrawImage($img, $x0 + [int](($Cell - $w) / 2), $yc + [int](($Cell - $h) / 2), $w, $h)
        $img.Dispose()
    }
    $g.Dispose(); $sheet.Save($Path, [System.Drawing.Imaging.ImageFormat]::Png); $sheet.Dispose()
    return $Path
}

# Prancha lado a lado (fixture | ângulo reto | sem ângulo reto) sobre xadrez, pra olhar o resultado.
function New-ComparisonSheet {
    param([Parameter(Mandatory)][string[]]$Images, [Parameter(Mandatory)][string[]]$Titles, [Parameter(Mandatory)][string]$Path)
    $cell = 600; $pad = 20; $head = 50
    $sheet = New-Object System.Drawing.Bitmap(($Images.Count * ($cell + $pad) + $pad), ($cell + $head + 2 * $pad))
    $g = [System.Drawing.Graphics]::FromImage($sheet)
    $g.Clear([System.Drawing.Color]::White); $g.InterpolationMode = 'HighQualityBicubic'
    $font = New-Object System.Drawing.Font('Arial', 20, [System.Drawing.FontStyle]::Bold, [System.Drawing.GraphicsUnit]::Pixel)
    for ($i = 0; $i -lt $Images.Count; $i++) {
        $x0 = $pad + $i * ($cell + $pad); $y0 = $head + $pad
        for ($cy = 0; $cy -lt $cell; $cy += 20) { for ($cx = 0; $cx -lt $cell; $cx += 20) {
            $c = if ((($cx + $cy) / 20) % 2) { [System.Drawing.Brushes]::Gainsboro } else { [System.Drawing.Brushes]::White }
            $g.FillRectangle($c, $x0 + $cx, $y0 + $cy, 20, 20) } }
        $g.DrawString($Titles[$i], $font, [System.Drawing.Brushes]::Black, $x0, $pad)
        if (-not (Test-Path $Images[$i])) { continue }
        $img = [System.Drawing.Image]::FromFile($Images[$i])
        $s = [Math]::Min($cell / $img.Width, $cell / $img.Height)
        $w = [int]($img.Width * $s); $h = [int]($img.Height * $s)
        $g.DrawImage($img, $x0 + [int](($cell - $w) / 2), $y0 + [int](($cell - $h) / 2), $w, $h)
        $img.Dispose()
    }
    $g.Dispose(); $sheet.Save($Path, [System.Drawing.Imaging.ImageFormat]::Png); $sheet.Dispose()
    return $Path
}
