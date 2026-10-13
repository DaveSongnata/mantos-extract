# Repete extrações contra o mantosfc de produção SEM o Corel e sem instalar o add-in: usa a classe
# ExtractionClient do build atual (dist\payload), a sessão e a chave OpenAI que o add-in guardou
# (DPAPI), a foto EXATAMENTE como o Corel exportou (%TEMP%\MantosExtract\work\selecao_*.png) e as
# caixas de uma detecção salva (deteccao.json do Invoke-PhotoExtraction.ps1). Serve pra validar uma
# mudança de prompt do servidor antes de reinstalar o add-in. Gasta 1 edição de imagem por peça.
#   powershell -NoProfile -ExecutionPolicy Bypass -File tools\corel-com\Invoke-ServerExtraction.ps1 `
#     -Export <selecao_*.png> -Detection <deteccao.json> -Ids el_3,el_9 [-Background] [-NoStraightAngle]
param([Parameter(Mandatory)][string]$Export, [Parameter(Mandatory)][string]$Detection, [string]$Ids = '',
      [switch]$Background, [switch]$NoStraightAngle, [string]$OutDir)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'StraightAngleMetrics.ps1')
$repo = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$payload = Join-Path $repo 'dist\payload'
# AssemblyResolve em C#, não em scriptblock: o resolve também acontece na thread do async, onde um
# scriptblock morre com "Não há Runspace disponível". Ele faz o papel dos binding redirects que o
# Corel/add-in teriam (versões das DLLs de System.* do payload).
if (-not ('MantosE2E.PayloadResolver' -as [type])) {
    Add-Type @'
using System; using System.IO; using System.Reflection;
namespace MantosE2E {
  public static class PayloadResolver {
    static string _dir;
    public static void Install(string dir) {
      _dir = dir;
      AppDomain.CurrentDomain.AssemblyResolve += (s, e) => {
        string p = Path.Combine(_dir, new AssemblyName(e.Name).Name + ".dll");
        return File.Exists(p) ? Assembly.LoadFrom(p) : null;
      };
    }
  }
}
'@
}
[MantosE2E.PayloadResolver]::Install($payload)
foreach ($n in 'MantosExtract.Core', 'MantosExtract.Windows') { [void][Reflection.Assembly]::LoadFrom("$payload\$n.dll") }
if (-not $OutDir) { $OutDir = Join-Path "$env:LOCALAPPDATA\Temp\MantosExtract.E2E" ('servidor-' + (Get-Date -Format 'yyyyMMdd-HHmmss')) }
New-Item -ItemType Directory -Force $OutDir | Out-Null

$store = New-Object MantosExtract.Windows.SecureCredentialStore
$session = $store.LoadSession(); $key = $store.LoadOpenAiKey()
if (-not $session -or -not $key) { throw 'sem sessão ou sem chave OpenAI salvas pelo add-in' }
$client = New-Object MantosExtract.Core.Extract.ExtractionClient($null, $null)
$bytes = [IO.File]::ReadAllBytes($Export)
$det = Get-Content $Detection -Raw -Encoding UTF8 | ConvertFrom-Json
$all = New-Object 'System.Collections.Generic.List[MantosExtract.Core.Detect.DetectedElement]'
foreach ($e in $det.elements) { $all.Add((New-Object MantosExtract.Core.Detect.DetectedElement($e.id, $e.label, (New-Object MantosExtract.Core.Detect.BoundingBox($e.xMin, $e.yMin, $e.xMax, $e.yMax))))) }
$straight = [Nullable[bool]](-not $NoStraightAngle)
$none = [Threading.CancellationToken]::None
$out = @()
foreach ($id in ($Ids -split '[,; ]+' | Where-Object { $_ })) {
    $el = $all | Where-Object { $_.Id -eq $id } | Select-Object -First 1
    if (-not $el) { throw "elemento $id não está na detecção" }
    $others = [MantosExtract.Core.Extract.OtherElements]::For($all, $el)
    $t0 = Get-Date
    $r = $client.ExtractAsync($session.SessionId, $key, 'medium', $bytes, 'image/png', $el.Box, $el.Label, $none, $false, $straight, $others).GetAwaiter().GetResult()
    $file = Join-Path $OutDir (($el.Label -replace '[\\/:*?"<>|]', '_') + '.png')
    [IO.File]::WriteAllBytes($file, $r.Bytes); $out += $file
    Write-Host ("    {0} {1}: {2:N0}s, {3} outros elementos" -f $id, $el.Label, ((Get-Date) - $t0).TotalSeconds, $others.Count)
}
if ($Background) {
    $t0 = Get-Date
    $r = $client.ExtractBackgroundAsync($session.SessionId, $key, 'medium', $bytes, 'image/png', $none, $false).GetAwaiter().GetResult()
    $file = Join-Path $OutDir 'Fundo.png'; [IO.File]::WriteAllBytes($file, $r.Bytes); $out += $file
    Write-Host ("    Fundo: {0:N0}s" -f ((Get-Date) - $t0).TotalSeconds)
}
$sheet = New-ContactSheet $out (Join-Path $OutDir 'prancha.png')
Write-Host "==> prancha: $sheet"
