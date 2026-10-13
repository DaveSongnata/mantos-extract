# Roda o pipeline REAL do Mantos Extract numa foto, dentro do Corel desta máquina e contra o mantosfc
# de produção: importa a foto, clica "Detectar", marca os elementos (todos, ou os que casam com
# -ElementMatch) e, com -Background, o "Fundo", clica "Extrair" e junta as peças numa prancha
# (prancha.png, com o tamanho e a orientação de cada peça). É a ferramenta para reproduzir uma
# reclamação de cliente com a foto dele. Gasta 1 detecção + 1 edição de imagem por peça na chave
# OpenAI salva no add-in.
#   powershell -NoProfile -ExecutionPolicy Bypass -File tools\corel-com\Invoke-PhotoExtraction.ps1 -Photo <foto> [-ElementMatch <regex>] [-Background] [-NoStraightAngle] [-KeepOpen]
param([Parameter(Mandatory)][string]$Photo, [string]$ElementMatch, [switch]$Background, [switch]$NoStraightAngle, [switch]$KeepOpen)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Connect-Corel.ps1')
. (Join-Path $PSScriptRoot 'StraightAngleMetrics.ps1')
. (Join-Path $PSScriptRoot 'MantosPage.ps1')
$evidence = Join-Path $MantosTestRoots[0] ('foto-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
New-Item -ItemType Directory -Force $evidence | Out-Null
if ([IO.Path]::GetExtension($Photo) -ne '.png') {
    # Import-CorelBitmap usa o filtro PNG (802): converte uma cópia, o original fica intacto.
    $img = [System.Drawing.Image]::FromFile((Resolve-Path $Photo).Path)
    $png = Join-Path $evidence ([IO.Path]::GetFileNameWithoutExtension($Photo) + '.png')
    $img.Save($png, [System.Drawing.Imaging.ImageFormat]::Png); $img.Dispose(); $Photo = $png
}

$app = Connect-Corel
Initialize-MantosPage
$doc = New-CorelTestDocument $app 400 300 ('foto-' + [IO.Path]::GetFileNameWithoutExtension($Photo))
$null = Import-CorelBitmap $app $doc $Photo
Write-Host "==> detectando em $Photo"
$det = Invoke-MantosDetect
$det | ConvertTo-Json -Depth 8 | Set-Content (Join-Path $evidence 'deteccao.json') -Encoding UTF8
$els = @($det.elements)
$els | ForEach-Object { Write-Host ("    {0}  {1,-45} x {2}-{3}  y {4}-{5}" -f $_.id, $_.label, $_.xMin, $_.xMax, $_.yMin, $_.yMax) }
$pick = if ($ElementMatch) { @($els | Where-Object { $_.label -match $ElementMatch }) } else { $els }
$ids = @($pick | ForEach-Object id)
if ($Background) { $ids += '__background__' }
if ($ids.Count -eq 0) { throw 'nada para extrair' }
Select-MantosElements $ids
PageShot (Join-Path $evidence 'selecao.png')

Write-Host "==> extraindo $($ids.Count) peça(s), ângulo reto $(if ($NoStraightAngle) { 'NÃO' } else { 'sim' })"
$t0 = Get-Date
$r = Invoke-MantosExtract (-not $NoStraightAngle)
Write-Host ("    {0} ok, {1} falha(s) em {2:N0} s; lote {3}" -f $r.Done.succeeded, $r.Done.failed, ((Get-Date) - $t0).TotalSeconds, $r.Batch.Name)
$r.Rows | Where-Object { -not $_.ok } | ForEach-Object { Write-Host "    FALHOU $($_.id): $($_.code) $($_.error)" }
$pngs = @(Get-ChildItem $r.Batch.FullName -Filter *.png | Sort-Object Name)
$pngs | Copy-Item -Destination $evidence
$sheet = New-ContactSheet @($pngs | ForEach-Object { Join-Path $evidence $_.Name }) (Join-Path $evidence 'prancha.png')
Write-Host "==> prancha: $sheet"
if (-not $KeepOpen) { Close-CorelTestDocuments $app | Out-Null }
