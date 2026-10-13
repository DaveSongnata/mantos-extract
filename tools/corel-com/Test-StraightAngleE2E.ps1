# E2E da opção "Ângulo reto" na EXTRAÇÃO (especificação GORE de 2026-10-13, reunião com o Davidson)
# DENTRO do add-in instalado no CorelDRAW desta máquina, contra o mantosfc de PRODUÇÃO. Dirige a tela
# REAL pelo DevTools do docker (porta 9479, dev-tools.on) e confere, ponta a ponta:
#
#   CA-01  a caixinha aparece MARCADA na tela de seleção, e volta marcada numa detecção nova
#   CA-07  marcada e desmarcada mandam instruções diferentes (docker.log do add-in e, com
#          -ServerLogs, o log do mantosfc: straightAngle=true / false)
#   CA-02  marcada: a peça sai de pé  (fixture sintética: PreencheCaixa ~1 e eixo ~0°)
#   CA-03  desmarcada: a peça mantém a inclinação da foto (medida, e também na prancha)
#   CA-04  as duas saem com fundo transparente (cobertura < 1)
#   CA-05  um elemento pedido = uma peça no lote
#
# A IA é estocástica: os CA-02/CA-03 medem a forma do resultado e geram uma prancha lado a lado
# (comparacao.png) pra conferir no olho. Cada execução gasta 1 detecção + 2 extrações na chave
# OpenAI salva no add-in.
#
# Pré-requisitos: Deploy-MantosExtract.ps1 -DevTools (Corel aberto com a build nova), operador logado
# no docker, chave OpenAI salva nas Configurações (ou -OpenAiKeyFile), node no PATH.
#   powershell -NoProfile -ExecutionPolicy Bypass -File tools\corel-com\Test-StraightAngleE2E.ps1
#     [-Photo <png>] [-ElementMatch <regex do rótulo>] [-ServerLogs] [-OpenAiKeyFile <arquivo>] [-KeepOpen]
param([string]$Photo, [string]$ElementMatch, [switch]$ServerLogs, [string]$OpenAiKeyFile, [switch]$KeepOpen)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Connect-Corel.ps1')
. (Join-Path $PSScriptRoot 'StraightAngleMetrics.ps1')
$cdp = Join-Path $PSScriptRoot 'cdp.mjs'
$evidence = Join-Path $MantosTestRoots[0] ('run-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
New-Item -ItemType Directory -Force $evidence | Out-Null
$synthetic = -not $Photo
if ($synthetic) { $Photo = New-SkewedBadgeFixture (Join-Path $evidence 'foto-badge-inclinado.png') }
elseif ([IO.Path]::GetExtension($Photo) -ne '.png') {
    # Import-CorelBitmap usa o filtro PNG (802, CorelConstants): converte uma cópia, o original fica intacto.
    $img = [System.Drawing.Image]::FromFile((Resolve-Path $Photo).Path)
    $Photo = Join-Path $evidence ([IO.Path]::GetFileNameWithoutExtension($Photo) + '.png')
    $img.Save($Photo, [System.Drawing.Imaging.ImageFormat]::Png); $img.Dispose()
}

# ---- DevTools helpers (JS só com aspas SIMPLES: o PowerShell 5.1 estraga aspas duplas em args) ----
function Js([string]$expr) { $out = & node $cdp eval $expr; if ($LASTEXITCODE -ne 0) { throw "cdp eval falhou: $out" }; return ($out -join "`n") }
function JsObj([string]$expr) { $j = Js $expr; if ($j -eq 'null' -or -not $j) { return $null }; return ($j | ConvertFrom-Json) }
function JsWait([string]$cond, [int]$sec = 60) { $null = & node $cdp wait $cond $sec; if ($LASTEXITCODE -ne 0) { throw "tempo esgotado ($sec s) esperando: $cond" } }
function ResetMsgs { Js '(()=>{ window.__e2e.length=0; return true })()' | Out-Null }
function LastMsg([string]$type) { JsObj "window.__e2e.filter(m=>m.type==='$type').pop()||null" }
function Shot([string]$name) { & node $cdp shot (Join-Path $evidence $name) | Out-Null }
function Visible([string]$id) { [bool](JsObj "!document.getElementById('$id').hidden") }

$results = New-Object System.Collections.ArrayList
function Check([string]$caso, [bool]$ok, [string]$detalhe) {
    [void]$results.Add([pscustomobject]@{ Caso = $caso; Ok = $ok; Detalhe = $detalhe })
    Write-Host ("  [{0}] {1} — {2}" -f ($(if ($ok) { 'OK ' } else { 'FALHA' })), $caso, $detalhe)
}

# Uma extração pela TELA (clique no botão "Extrair"), devolvendo o PNG do lote novo.
function RunExtraction([string]$name, [bool]$straight) {
    Js "(()=>{ const c=document.getElementById('selectStraightAngle'); if(c.checked!==$($straight.ToString().ToLower())) c.click(); return c.checked })()" | Out-Null
    $before = @(Get-ChildItem (Get-MantosExtractionsRoot) -Directory -ErrorAction SilentlyContinue | ForEach-Object FullName)
    ResetMsgs
    Js "(()=>{ document.getElementById('extractBtn').click(); return true })()" | Out-Null
    JsWait "window.__e2e.some(m=>m.type==='extract'&&m.done)" 900
    $done = LastMsg 'extract'
    $rows = @(JsObj "window.__e2e.filter(m=>m.type==='extractProgress'&&m.stage==='done')")
    $batch = Get-ChildItem (Get-MantosExtractionsRoot) -Directory | Where-Object { $before -notcontains $_.FullName } | Sort-Object CreationTime | Select-Object -Last 1
    $png = if ($batch) { Get-ChildItem $batch.FullName -Filter *.png | Select-Object -First 1 } else { $null }
    $copy = Join-Path $evidence "$name.png"
    if ($png) { Copy-Item $png.FullName $copy }
    Shot "$name-tela.png"
    $err = if ($done.error) { " erro=$($done.error)" } elseif ($rows | Where-Object { -not $_.ok }) { ' erro=' + (($rows | Where-Object { -not $_.ok } | ForEach-Object { "$($_.code) $($_.error)" }) -join '; ') } else { '' }
    Check "$name — extração concluída" ($done.succeeded -eq 1 -and $png) ("ok=$($done.succeeded) falhas=$($done.failed) lote=$(if ($batch) { $batch.Name } else { '(nenhum)' })$err")
    Check "CA-05 $name — 1 elemento pedido = 1 peça" (@(if ($batch) { Get-ChildItem $batch.FullName -Filter *.png }).Count -eq 1) "$(@(if ($batch) { Get-ChildItem $batch.FullName -Filter *.png }).Count) PNG no lote"
    if ($png) { return $copy } else { return $null }
}

# ======================================================================================
Write-Host "==> E2E ângulo reto — evidências em $evidence"
Write-Host "    foto: $Photo $(if ($synthetic) { '(sintética, inclinação conhecida)' })"
$app = Connect-Corel
$log = Get-MantosDockerLogPath
$targets = & node $cdp targets
if ($LASTEXITCODE -ne 0 -or -not ($targets -match 'mantosextract.app')) { throw "DevTools do Mantos Extract indisponível (porta 9479). Rode Deploy-MantosExtract.ps1 -DevTools e abra o docker. ($targets)" }
Js "(()=>{ if(!window.__e2eHooked){ window.__e2e=[]; const o=window.mantosExtractReceive; window.mantosExtractReceive=function(m){ try{ window.__e2e.push(typeof m==='string'?JSON.parse(m):m) }catch(e){}; return o.apply(this,arguments) }; window.__e2eHooked=true } window.__e2e.length=0; return true })()" | Out-Null

if (-not (Visible 'screen-home')) {
    if (Visible 'screen-login') { throw 'O docker está na tela de LOGIN: entre com o usuário do operador e rode de novo.' }
    Js "(()=>{ window.chrome.webview.postMessage({cmd:'status'}); return true })()" | Out-Null
    Start-Sleep 2
    if (-not (Visible 'screen-home')) { throw 'O docker não está na tela inicial (outra tela aberta?). Volte ao início e rode de novo.' }
}
if ($OpenAiKeyFile) {
    # Base64 porque a chave passa por argumento de processo nativo (node) e não pode ser logada.
    $b64 = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes((Get-Content $OpenAiKeyFile -Raw).Trim()))
    Js "(()=>{ window.chrome.webview.postMessage({cmd:'saveOpenAiKey', key: atob('$b64')}); return true })()" | Out-Null
    Start-Sleep 2
    Write-Host '    chave OpenAI salva no add-in (DPAPI) a partir do arquivo'
}

$doc = $null
try {
    $doc = New-CorelTestDocument $app 400 300 'e2e-angulo-reto'
    $null = Import-CorelBitmap $app $doc $Photo
    $logMark = if (Test-Path $log) { (Get-Content $log).Count } else { 0 }
    $serverSince = (Get-Date).ToUniversalTime().AddSeconds(-5).ToString('yyyy-MM-ddTHH:mm:ss')

    # ---------- detecção pela tela ----------
    ResetMsgs
    Js "(()=>{ window.chrome.webview.postMessage({cmd:'status'}); return true })()" | Out-Null
    JsWait "!document.getElementById('detectBtn').disabled" 20
    Js "(()=>{ document.getElementById('detectBtn').click(); return true })()" | Out-Null
    JsWait "window.__e2e.some(m=>m.type==='detect')" 300
    $det = LastMsg 'detect'
    if (-not $det.ok) { throw "detecção falhou: $($det.code) $($det.error)" }
    $det | ConvertTo-Json -Depth 8 | Set-Content (Join-Path $evidence 'deteccao.json') -Encoding UTF8
    $els = @($det.elements)
    Write-Host ("    detectados: " + (($els | ForEach-Object { $_.label }) -join ' | '))
    if ($els.Count -eq 0) { throw 'nenhum elemento detectado' }
    $pick = if ($ElementMatch) { $els | Where-Object { $_.label -match $ElementMatch } | Select-Object -First 1 }
            else { $els | Sort-Object { ($_.xMax - $_.xMin) * ($_.yMax - $_.yMin) } -Descending | Select-Object -First 1 }
    if (-not $pick) { throw "nenhum elemento com rótulo casando '$ElementMatch'" }
    Write-Host "    elemento escolhido: $($pick.label) ($($pick.id))"

    # ---------- CA-01: marcada por padrão ----------
    JsWait "!document.getElementById('screen-select').hidden" 10
    $st = JsObj "(()=>{ const c=document.getElementById('selectStraightAngle'); const l=c.closest('label'); return {checked:c.checked, visible: l.offsetParent!==null, text: l.innerText.trim()} })()"
    Check 'CA-01 "Ângulo reto" marcada ao abrir a seleção' ($st.checked -and $st.visible) "marcada=$($st.checked) visível=$($st.visible) rótulo='$($st.text)'"
    $picked = JsObj "(()=>{ const c=Array.from(document.querySelectorAll('#selectChecklist input')).find(x=>x.dataset.id==='$($pick.id)'); if(!c) return false; if(!c.checked) c.click(); return c.checked })()"
    if (-not $picked) { throw "checkbox do elemento $($pick.id) não encontrado na tela" }
    Shot 'selecao.png'

    # ---------- as duas extrações ----------
    $straightPng = RunExtraction 'com-angulo-reto' $true
    $keepPng = RunExtraction 'sem-angulo-reto' $false

    # ---------- CA-01 (2): volta marcada numa detecção nova ----------
    # Reaplica a MESMA resposta de detecção pela função real da página (sem gastar outra chamada):
    # é exatamente o caminho de uma foto nova.
    $b64 = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes(($det | ConvertTo-Json -Depth 8 -Compress)))
    Js "(()=>{ window.mantosExtractReceive(JSON.parse(decodeURIComponent(escape(atob('$b64'))))); return true })()" | Out-Null
    $again = [bool](JsObj "document.getElementById('selectStraightAngle').checked")
    Check 'CA-01 volta marcada numa detecção nova (depois de desmarcada)' $again "marcada=$again"
    Js "(()=>{ document.getElementById('selectBackBtn').click(); return true })()" | Out-Null

    # ---------- CA-07: instruções diferentes ----------
    # O docker.log é UTF-8 sem BOM: sem -Encoding o PowerShell 5.1 lê como ANSI e "ângulo" não casa.
    $newLog = if (Test-Path $log) { Get-Content $log -Encoding UTF8 | Select-Object -Skip $logMark } else { @() }
    $sim = [bool]($newLog | Select-String 'Extração: 1 elemento\(s\), fundo não, ângulo reto sim')
    $nao = [bool]($newLog | Select-String 'Extração: 1 elemento\(s\), fundo não, ângulo reto não')
    Check 'CA-07 add-in mandou marcada e desmarcada (docker.log)' ($sim -and $nao) "sim=$sim não=$nao"
    if ($ServerLogs) {
        $srv = & ssh mfc "docker logs mantoscontrol-api-1 --since $serverSince 2>&1 | grep -o 'straightAngle=[a-z]*' | sort | uniq -c"
        $srvText = ($srv -join ' ').Trim()
        Check 'CA-07 mantosfc recebeu true e false (log da api)' (($srvText -match 'straightAngle=true') -and ($srvText -match 'straightAngle=false')) "log da api: $srvText"
    }

    # ---------- CA-02/03/04: forma do resultado ----------
    $mS = if ($straightPng) { Measure-ExtractedPiece $straightPng } else { $null }
    $mK = if ($keepPng) { Measure-ExtractedPiece $keepPng } else { $null }
    @($mS, $mK) | Where-Object { $_ } | Format-Table -AutoSize | Out-String | Write-Host
    if ($mS -and $mK) {
        Check 'CA-04 fundo transparente nas duas' ($mS.Cobertura -lt 0.98 -and $mK.Cobertura -lt 0.98) "cobertura reto=$($mS.Cobertura) sem=$($mK.Cobertura)"
        if ($synthetic) {
            # O escudo da foto está girado ~15° e cisalhado: de pé, ele preenche a própria caixa.
            Check 'CA-02 marcada: peça de pé' ($mS.PreencheCaixa -ge 0.9 -and [Math]::Abs($mS.AnguloEixo) -le 4) "preenche=$($mS.PreencheCaixa) eixo=$($mS.AnguloEixo)°"
            Check 'CA-03 desmarcada: mantém a inclinação' ($mK.PreencheCaixa -lt 0.85 -or [Math]::Abs($mK.AnguloEixo) -ge 6) "preenche=$($mK.PreencheCaixa) eixo=$($mK.AnguloEixo)°"
        } else {
            Write-Host '    foto real: CA-02/03 sem gabarito numérico — conferir a prancha comparacao.png'
        }
    }
    $sheet = New-ComparisonSheet @($Photo, $straightPng, $keepPng) @('FOTO', 'COM ANGULO RETO', 'SEM ANGULO RETO') (Join-Path $evidence 'comparacao.png')
    Write-Host "    prancha: $sheet"
    $app.ActiveWindow.ActiveView.ToFitAllObjects()
    Save-CorelScreenshot (Join-Path $evidence 'corel.png') | Out-Null
}
finally {
    if (-not $KeepOpen) { try { Close-CorelTestDocuments $app | Out-Null } catch { } }
}

$results | Format-Table -AutoSize -Wrap | Out-String -Width 220 | Write-Host
$results | ConvertTo-Json -Depth 4 | Set-Content (Join-Path $evidence 'resultado.json') -Encoding UTF8
$fail = @($results | Where-Object { -not $_.Ok }).Count
Write-Host ("==> {0} verificação(ões), {1} falha(s). Evidências: {2}" -f $results.Count, $fail, $evidence)
if ($fail -gt 0) { exit 1 }
