# Helpers para dirigir a página REAL do Mantos Extract dentro do Corel pelo DevTools (cdp.mjs,
# porta 9479). Usado por Test-StraightAngleE2E.ps1 e Invoke-PhotoExtraction.ps1.
#   . tools\corel-com\MantosPage.ps1
#   Initialize-MantosPage            # engancha window.mantosExtractReceive e confere a tela inicial
#   Invoke-MantosDetect              # clica "Detectar" sobre o bitmap selecionado no Corel
# JS só com aspas SIMPLES: o PowerShell 5.1 estraga aspas duplas em argumentos de programas nativos.

$cdp = Join-Path $PSScriptRoot 'cdp.mjs'

function Js([string]$expr) { $out = & node $cdp eval $expr; if ($LASTEXITCODE -ne 0) { throw "cdp eval falhou: $out" }; return ($out -join "`n") }
function JsObj([string]$expr) { $j = Js $expr; if ($j -eq 'null' -or -not $j) { return $null }; return ($j | ConvertFrom-Json) }
function JsWait([string]$cond, [int]$sec = 60) { $null = & node $cdp wait $cond $sec; if ($LASTEXITCODE -ne 0) { throw "tempo esgotado ($sec s) esperando: $cond" } }
function ResetMsgs { Js '(()=>{ window.__e2e.length=0; return true })()' | Out-Null }
function LastMsg([string]$type) { JsObj "window.__e2e.filter(m=>m.type==='$type').pop()||null" }
function Visible([string]$id) { [bool](JsObj "!document.getElementById('$id').hidden") }
function PageShot([string]$path) { & node $cdp shot $path | Out-Null }

# Base64 porque o texto passa por argumento de processo nativo (node).
function PostCmd([hashtable]$msg) {
    $b64 = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes(($msg | ConvertTo-Json -Compress -Depth 6)))
    Js "(()=>{ window.chrome.webview.postMessage(JSON.parse(decodeURIComponent(escape(atob('$b64'))))); return true })()" | Out-Null
}

function Initialize-MantosPage {
    $targets = & node $cdp targets
    if ($LASTEXITCODE -ne 0 -or -not ($targets -match 'mantosextract.app')) { throw "DevTools do Mantos Extract indisponível (porta 9479). Rode Deploy-MantosExtract.ps1 -DevTools e abra o docker. ($targets)" }
    Js "(()=>{ if(!window.__e2eHooked){ window.__e2e=[]; const o=window.mantosExtractReceive; window.mantosExtractReceive=function(m){ try{ window.__e2e.push(typeof m==='string'?JSON.parse(m):m) }catch(e){}; return o.apply(this,arguments) }; window.__e2eHooked=true } window.__e2e.length=0; return true })()" | Out-Null
    if (Visible 'screen-login') { throw 'O docker está na tela de LOGIN: entre com o usuário do operador e rode de novo.' }
    if (-not (Visible 'screen-home')) {
        # Outra tela aberta (seleção/resultado de um teste anterior): volta ao início pela própria tela.
        Js "(()=>{ for (const id of ['selectBackBtn','resultNewImageBtn']) { const b=document.getElementById(id); if (b && b.offsetParent!==null) { b.click(); return id } } return null })()" | Out-Null
        PostCmd @{ cmd = 'status' }
        Start-Sleep 2
        if (-not (Visible 'screen-home')) { throw 'O docker não está na tela inicial. Volte ao início e rode de novo.' }
    }
}

# Clica "Detectar" (o bitmap já tem que estar selecionado no Corel) e devolve a mensagem de detecção.
function Invoke-MantosDetect([int]$TimeoutSec = 300) {
    ResetMsgs
    PostCmd @{ cmd = 'status' }
    JsWait "!document.getElementById('detectBtn').disabled" 20
    Js "(()=>{ document.getElementById('detectBtn').click(); return true })()" | Out-Null
    JsWait "window.__e2e.some(m=>m.type==='detect')" $TimeoutSec
    $det = LastMsg 'detect'
    if (-not $det.ok) { throw "detecção falhou: $($det.code) $($det.error)" }
    JsWait "!document.getElementById('screen-select').hidden" 10
    return $det
}

# Marca na tela de seleção os ids pedidos (inclusive '__background__' para o Fundo).
function Select-MantosElements([string[]]$Ids) {
    $b64 = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes((ConvertTo-Json @($Ids) -Compress)))
    $n = JsObj "(()=>{ const want=JSON.parse(atob('$b64')); let n=0; document.querySelectorAll('#selectChecklist input').forEach(c=>{ const on=want.includes(c.dataset.id); if(c.checked!==on) c.click(); if(c.checked) n++ }); return n })()"
    if ($n -ne $Ids.Count) { throw "marquei $n de $($Ids.Count) elementos na tela" }
}

# Clica "Extrair" e espera o lote acabar; devolve @{ Done; Rows; Batch } com a pasta nova do lote.
function Invoke-MantosExtract([bool]$StraightAngle = $true, [int]$TimeoutSec = 1200) {
    Js "(()=>{ const c=document.getElementById('selectStraightAngle'); if(c.checked!==$($StraightAngle.ToString().ToLower())) c.click(); return c.checked })()" | Out-Null
    $before = @(Get-ChildItem (Get-MantosExtractionsRoot) -Directory -ErrorAction SilentlyContinue | ForEach-Object FullName)
    ResetMsgs
    Js "(()=>{ document.getElementById('extractBtn').click(); return true })()" | Out-Null
    JsWait "window.__e2e.some(m=>m.type==='extract'&&m.done)" $TimeoutSec
    $batch = Get-ChildItem (Get-MantosExtractionsRoot) -Directory | Where-Object { $before -notcontains $_.FullName } | Sort-Object CreationTime | Select-Object -Last 1
    return @{
        Done  = (LastMsg 'extract')
        Rows  = @(JsObj "window.__e2e.filter(m=>m.type==='extractProgress'&&m.stage==='done')")
        Batch = $batch
    }
}
