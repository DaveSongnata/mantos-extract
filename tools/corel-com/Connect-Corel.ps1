# Conecta ao CorelDRAW JÁ ABERTO nesta máquina por automação COM (a mesma interface que o
# add-in usa por dentro). Portado de siscut/tools/corel-com (que veio do ai-sten) em 2026-10-13
# e adaptado ao Mantos Extract. Uso — dentro de UM comando PowerShell (o estado não sobrevive
# entre comandos do agente, então conecte e trabalhe no mesmo comando):
#
#   . D:\sback\siscut\mantos-extract\tools\corel-com\Connect-Corel.ps1
#   $app = Connect-Corel                       # recusa se não houver Corel aberto (não abre outro)
#   $doc = New-CorelTestDocument $app 400 300
#   $bmp = Import-CorelBitmap $app $doc 'C:\foto.png'   # já fica selecionado
#
# Regras de uso: docs/COREL-AUTOMACAO.md.

$ErrorActionPreference = 'Stop'

# Valores OFICIAIS (espelho de src/MantosExtract.Interop/CorelConstants.cs — nunca chutar:
# cdrMillimeter já foi chutado como 4 nos irmãos, que é centímetro, e tudo saiu 10x — CLAUDE.md).
$CdrMillimeter = 3
$CdrGroupShape = 7
$CdrFilterPng = 802

# Pastas de trabalho dos testes automatizados (documentos aqui podem ser fechados sem pena).
$MantosTestRoots = @("$env:LOCALAPPDATA\Temp\MantosExtract.E2E", "$env:LOCALAPPDATA\Temp\MantosExtract.CorelTests")

function Connect-Corel {
    [CmdletBinding()]
    param([switch]$AllowStart)

    $running = @(Get-Process CorelDRW -ErrorAction SilentlyContinue)
    if ($running.Count -eq 0 -and -not $AllowStart) {
        throw 'Nenhum CorelDRAW aberto. Rode Deploy-MantosExtract.ps1 (reabre o Corel) ou use -AllowStart.'
    }
    # O Corel NÃO se registra na Running Object Table: GetActiveObject falha com MK_E_UNAVAILABLE.
    # CoCreateInstance (New-Object -ComObject) devolve a instância que já está rodando.
    $app = New-Object -ComObject 'CorelDRAW.Application'
    $after = @(Get-Process CorelDRW -ErrorAction SilentlyContinue)
    if ($running.Count -gt 0 -and $after.Count -ne $running.Count) {
        throw "Abriu um SEGUNDO CorelDRAW (antes $($running.Count), agora $($after.Count)) — não era para acontecer."
    }
    if ($running.Count -eq 0) { $app.Visible = $true }
    Write-Verbose ("Conectado: CorelDRAW {0}.{1}, {2} documento(s) aberto(s)" -f $app.VersionMajor, $app.VersionMinor, $app.Documents.Count)
    return $app
}

# Documento NOVO para teste, em milímetros, já SALVO numa pasta de teste (tem caminho, então
# Close-CorelTestDocuments o reconhece e fecha sem pena).
function New-CorelTestDocument {
    param([Parameter(Mandatory)]$App, [double]$WidthMm = 210, [double]$HeightMm = 297, [string]$Name)
    $dir = $MantosTestRoots[1]
    New-Item -ItemType Directory -Force $dir | Out-Null
    if (-not $Name) { $Name = 'teste-' + [guid]::NewGuid().ToString('N').Substring(0, 6) }
    $doc = $App.CreateDocument()
    $doc.Unit = $CdrMillimeter
    $doc.ActivePage.SetSize($WidthMm, $HeightMm)
    # SaveAs(path) sozinho falha ("não é possível converter string em string"): Options explícito.
    $doc.SaveAs((Join-Path $dir "$Name.cdr"), $App.CreateStructSaveAsOptions())
    return $doc
}

# Importa um bitmap na camada ativa e o deixa como ÚNICA seleção (é o que o botão "Detectar" lê).
# Import com os três argumentos explícitos, como o CorelImporter do add-in: omitir os opcionais
# dá DISP_E_TYPEMISMATCH.
function Import-CorelBitmap {
    param([Parameter(Mandatory)]$App, [Parameter(Mandatory)]$Doc, [Parameter(Mandatory)][string]$Path)
    $layer = $Doc.ActiveLayer
    $before = @($layer.Shapes | ForEach-Object { $_.StaticID })
    $opts = $App.CreateStructImportOptions()
    [void]$layer.GetType().InvokeMember('Import', [Reflection.BindingFlags]::InvokeMethod, $null, $layer, @($Path, $CdrFilterPng, $opts))
    $new = @($layer.Shapes) | Where-Object { $before -notcontains $_.StaticID } | Select-Object -First 1
    if (-not $new) { throw "Import de '$Path' não criou forma nenhuma" }
    $Doc.ClearSelection()
    $new.CreateSelection()
    return $new
}

# Todas as formas de uma página, descendo em grupos (lista plana).
function Get-CorelShapesDeep {
    param([Parameter(Mandatory)]$Page)
    $out = New-Object System.Collections.ArrayList
    $stack = New-Object System.Collections.Stack
    foreach ($ly in @($Page.Layers)) { foreach ($s in @($ly.Shapes)) { $stack.Push($s) } }
    while ($stack.Count) {
        $x = $stack.Pop(); [void]$out.Add($x)
        if ($x.Type -eq $CdrGroupShape) { foreach ($c in @($x.Shapes)) { $stack.Push($c) } }
    }
    return $out
}

# Fecha SEM salvar os documentos cujo caminho casa com -Like (padrão: as pastas de teste dos
# agentes). Nunca use '*' com o Dave testando junto (regra 3 do docs/COREL-AUTOMACAO.md).
function Close-CorelTestDocuments {
    param([Parameter(Mandatory)]$App, [string[]]$Like = @('*\MantosExtract.E2E\*', '*\MantosExtract.CorelTests\*'))
    for ($i = $App.Documents.Count; $i -ge 1; $i--) {
        $d = $App.Documents.Item($i)
        $path = [string]$d.FullFileName
        if ($Like | Where-Object { $path -like $_ }) { $d.Dirty = $false; $d.Close(); "fechado: $path" }
    }
}

# Print da janela do Corel (inteira, ou só a faixa de cima com -Height). PrintWindow desenha a
# janela do Corel mesmo com o terminal na frente. Leia o PNG com a ferramenta de imagem.
function Save-CorelScreenshot {
    param([Parameter(Mandatory)][string]$Path, [int]$Height = 0)
    Add-Type -AssemblyName System.Drawing
    if (-not ('CorelWin2' -as [type])) {
        Add-Type @'
using System; using System.Runtime.InteropServices;
public static class CorelWin2 {
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr hdc, uint flags);
  [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr h);
  [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int cmd);
  public struct RECT { public int L, T, R, B; }
}
'@
    }
    $p = Get-Process CorelDRW | Select-Object -First 1
    $hw = $p.MainWindowHandle
    if ([CorelWin2]::IsIconic($hw)) { [CorelWin2]::ShowWindow($hw, 9) | Out-Null; Start-Sleep -Milliseconds 800 }
    $r = New-Object CorelWin2+RECT
    [CorelWin2]::GetWindowRect($hw, [ref]$r) | Out-Null
    $w = $r.R - $r.L; $fullH = $r.B - $r.T
    $full = New-Object System.Drawing.Bitmap($w, $fullH)
    $g = [System.Drawing.Graphics]::FromImage($full)
    $hdc = $g.GetHdc()
    [CorelWin2]::PrintWindow($hw, $hdc, 2) | Out-Null   # 2 = PW_RENDERFULLCONTENT (WebView2/GPU)
    $g.ReleaseHdc($hdc); $g.Dispose()
    $h = if ($Height -gt 0) { [Math]::Min($Height, $fullH) } else { $fullH }
    $bmp = $full.Clone((New-Object System.Drawing.Rectangle(0, 0, $w, $h)), $full.PixelFormat)
    $bmp.Save($Path); $bmp.Dispose(); $full.Dispose()
    return $Path
}

# TEMP do USUÁRIO (é o que o Corel herda). O shell do agente às vezes tem outro TEMP.
function Get-UserTemp {
    $t = [Environment]::GetEnvironmentVariable('TEMP', 'User'); if (-not $t) { $t = "$env:LOCALAPPDATA\Temp" }
    return [Environment]::ExpandEnvironmentVariables($t)
}

# Log do docker do Mantos Extract (MantosExtractLog: %TEMP%\MantosExtract\docker.log).
function Get-MantosDockerLogPath { return Join-Path (Get-UserTemp) 'MantosExtract\docker.log' }

# Lotes de extração (MantosExtractBridge.ExtractionsRootDir): uma pasta por lote, com batch.json.
function Get-MantosExtractionsRoot { return Join-Path $env:LOCALAPPDATA 'MantosExtract\Extractions' }
