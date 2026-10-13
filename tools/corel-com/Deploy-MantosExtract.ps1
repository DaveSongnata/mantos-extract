# Instala a build ATUAL do Mantos Extract no CorelDRAW DESTA máquina de desenvolvimento e confere
# que ela carregou. Portado de siscut/tools/corel-com/Deploy-SisCut.ps1 (2026-10-13).
#   1. (opcional) gera o instalador                      [-Build: scripts\build-all.ps1]
#   2. fecha o CorelDRAW — documento SEM NOME ou com alteração não salva ganha antes uma CÓPIA em
#      %LOCALAPPDATA%\Temp\MantosExtract.Deploy\salvos\<data> (o original não é tocado)
#   3. instala:
#        padrão      : roda o MantosExtract_Setup.exe da versão em modo SILENCIOSO (/silent) — o
#                      MESMO InstallerEngine do assistente, sem janela. Pede UMA elevação (UAC)
#                      por instalação, porque o instalador é requireAdministrator.
#        -CopyPayload: copia dist\payload direto para ...\Addons\MantosExtract (iteração rápida,
#                      sem UAC depois de rodar UMA vez com -GrantAccess). O instalador APAGA e
#                      recria a pasta, então depois de um -Installer a permissão some: rode
#                      -GrantAccess de novo antes do próximo -CopyPayload.
#   4. reabre o CorelDRAW, abre o docker e confere no docker.log a versão carregada.
#
# Uso:  powershell -NoProfile -ExecutionPolicy Bypass -File tools\corel-com\Deploy-MantosExtract.ps1 [-Build] [-DevTools] [-CopyPayload] [-GrantAccess] [-NoRestart]
#   -DevTools  cria %LOCALAPPDATA%\MantosExtract\dev-tools.on (porta 9479 para tools/corel-com/cdp.mjs)
param([switch]$Build, [switch]$DevTools, [switch]$CopyPayload, [switch]$GrantAccess, [switch]$NoRestart)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Connect-Corel.ps1')
$repo = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$corelDir = 'C:\Program Files\Corel\CorelDRAW Graphics Suite\25\Programs64'
$addonDir = Join-Path $corelDir 'Addons\MantosExtract'
$dockerGuid = '3bebb2f8-92d5-4d89-8dd3-dcc6a8d4bb13'   # src/MantosExtract.AddIn/addon/AppUI.xslt (dockerData)
$userTemp = Get-UserTemp
$log = Get-MantosDockerLogPath
$work = Join-Path $env:LOCALAPPDATA 'Temp\MantosExtract.Deploy'
New-Item -ItemType Directory -Force $work | Out-Null

$tag = ([regex]::Match((Get-Content "$repo\src\MantosExtract.AddIn\Build.cs" -Raw), 'Tag = "(.*?)"')).Groups[1].Value
Write-Host "==> Deploy Mantos Extract $tag"

if ($GrantAccess) {
    # PELO SID: nesta máquina o computador também se chama DAVE, e "Dave" no icacls resolve para
    # o computador, não para o usuário (lição do Deploy-SisCut).
    $sid = [Security.Principal.WindowsIdentity]::GetCurrent().User.Value
    $grantLog = Join-Path $work 'grant.log'
    $cmd = "New-Item -ItemType Directory -Force '$addonDir' | Out-Null; & icacls.exe '$addonDir' /grant '*${sid}:(OI)(CI)M' /T *> '$grantLog'; exit `$LASTEXITCODE"
    $enc = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($cmd))
    Write-Host "==> pedindo elevação (UAC) para liberar escrita em $addonDir"
    $p = Start-Process powershell -Verb RunAs -Wait -PassThru -ArgumentList '-NoProfile', '-EncodedCommand', $enc
    Write-Host "    elevação terminou (exit $($p.ExitCode)); icacls: $((Get-Content $grantLog -ErrorAction SilentlyContinue) -join ' | ')"
}

if ($Build) {
    Write-Host '==> [1] instalador (scripts\build-all.ps1)'
    & powershell -NoProfile -ExecutionPolicy Bypass -File "$repo\scripts\build-all.ps1" | Select-Object -Last 3
    if ($LASTEXITCODE -ne 0) { throw 'build-all falhou' }
}
$setup = "$repo\shared\bin\redistributables\Clientes\$tag\MantosExtract_Setup.exe"
$payload = "$repo\dist\payload"
if ($CopyPayload) {
    if (-not (Test-Path "$payload\MantosExtract.AddIn.dll")) { throw "payload ausente: $payload (rode com -Build)" }
    # Escrita liberada? Testa ANTES de fechar o Corel do Dave — falhar depois de fechar seria pior.
    $probe = Join-Path $addonDir ('.probe-' + [guid]::NewGuid().ToString('N'))
    try { Set-Content $probe 'x' -ErrorAction Stop; Remove-Item $probe -Force }
    catch { throw "Sem permissão de escrita em $addonDir. Rode UMA vez com -GrantAccess (pede uma elevação/UAC)." }
} elseif (-not (Test-Path $setup)) { throw "instalador ausente: $setup (rode com -Build)" }

if ($DevTools) {
    New-Item -ItemType Directory -Force "$env:LOCALAPPDATA\MantosExtract" | Out-Null
    Set-Content "$env:LOCALAPPDATA\MantosExtract\dev-tools.on" 'dev' -Encoding ascii
    Write-Host '    dev-tools.on criado (porta 9479)'
}

Write-Host '==> [2] fechando o CorelDRAW'
if (@(Get-Process CorelDRW -ErrorAction SilentlyContinue).Count -gt 0) {
    try {
        $app = New-Object -ComObject 'CorelDRAW.Application'
        $salvos = Join-Path $work ('salvos\' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
        for ($i = $app.Documents.Count; $i -ge 1; $i--) {
            try {
                $d = $app.Documents.Item($i)
                $semNome = [string]::IsNullOrEmpty([string]$d.FullFileName)
                $deTeste = [bool]($MantosTestRoots | Where-Object { ([string]$d.FullFileName) -like "$_\*" })
                if (($semNome -or $d.Dirty) -and -not $deTeste) {
                    New-Item -ItemType Directory -Force $salvos | Out-Null
                    $nome = ([IO.Path]::GetFileNameWithoutExtension([string]$d.Title) -replace '[\\/:*?"<>|]', '_') + '.cdr'
                    # SaveAs grava a CÓPIA e passa a apontar o documento para ela — o original no
                    # disco fica intocado (nada do Dave é sobrescrito).
                    try { $d.SaveAs((Join-Path $salvos $nome), $app.CreateStructSaveAsOptions()); Write-Host "    cópia de segurança: $(Join-Path $salvos $nome)" }
                    catch { Write-Host "    cópia de '$($d.Title)' falhou: $($_.Exception.Message)" }
                }
                try { $d.Dirty = $false } catch { }
                $d.Close()
            } catch { Write-Host "    fechar documento ${i}: $($_.Exception.Message)" }
        }
        $app.Quit()
    } catch { Write-Host "    Quit por COM falhou: $($_.Exception.Message)" }
    $deadline = (Get-Date).AddSeconds(60)
    while ((Get-Process CorelDRW -ErrorAction SilentlyContinue) -and (Get-Date) -lt $deadline) { Start-Sleep -Milliseconds 500 }
    if (Get-Process CorelDRW -ErrorAction SilentlyContinue) {
        Write-Host '    Corel não fechou em 60 s: encerrando o processo'
        Get-Process CorelDRW | Stop-Process -Force
        Start-Sleep 2
    }
}

if ($CopyPayload) {
    Write-Host "==> [3] copiando $payload para $addonDir"
    Copy-Item "$payload\*" $addonDir -Recurse -Force
} else {
    Write-Host "==> [3] instalação silenciosa: $setup /silent (pede UAC)"
    $p = Start-Process $setup -ArgumentList '/silent' -Verb RunAs -Wait -PassThru
    # O instalador roda elevado, mas com o MESMO usuário: o log fica no TEMP dele.
    $silentLog = Join-Path $userTemp 'MantosExtract_Installer\install-silent.log'
    $tail = if (Test-Path $silentLog) { Get-Content $silentLog -Tail 1 } else { '(sem log)' }
    Write-Host "    instalador terminou: exit $($p.ExitCode) — $tail"
    if ($p.ExitCode -ne 0) { throw "instalação silenciosa falhou (exit $($p.ExitCode)): $tail — log completo em $silentLog" }
}
Write-Host "    ok: MantosExtract.AddIn.dll $((Get-Item "$addonDir\MantosExtract.AddIn.dll").LastWriteTime)"

if ($NoRestart) { Write-Host '==> pronto (Corel não reaberto: -NoRestart)'; return }

Write-Host '==> [4] reabrindo o CorelDRAW'
$started = Get-Date
$env:TEMP = $userTemp; $env:TMP = $userTemp
Start-Process -FilePath (Join-Path $corelDir 'CorelDRW.exe')
$app = $null
$deadline = (Get-Date).AddSeconds(180)
while (-not $app -and (Get-Date) -lt $deadline) {
    Start-Sleep 3
    $p = Get-Process CorelDRW -ErrorAction SilentlyContinue | Where-Object { $_.MainWindowHandle -ne 0 }
    if ($p) { try { $app = New-Object -ComObject 'CorelDRAW.Application'; $null = $app.VersionMajor } catch { $app = $null } }
}
if (-not $app) { throw 'CorelDRAW não respondeu ao COM em 180 s' }
Start-Sleep 5
try { $app.FrameWork.ShowDocker($dockerGuid) } catch { Write-Host "    ShowDocker: $($_.Exception.Message)" }

$deadline = (Get-Date).AddSeconds(90)
$loaded = $false
while (-not $loaded -and (Get-Date) -lt $deadline) {
    Start-Sleep 2
    if (Test-Path $log) {
        $loaded = [bool](Get-Content $log -Tail 400 | Where-Object { $_ -match "=== MantosExtract docker v$([regex]::Escape($tag)) ===" } |
            Where-Object { try { [datetime]::ParseExact($_.Substring(0, 12), 'HH:mm:ss.fff', $null).TimeOfDay -ge $started.TimeOfDay.Add([TimeSpan]::FromSeconds(-2)) } catch { $true } })
    }
}
if ($loaded) { Write-Host "==> PRONTO: CorelDRAW carregou o Mantos Extract $tag (log: $log)" }
else { Write-Host "==> ATENÇÃO: não vi '=== MantosExtract docker v$tag ===' no log em 90 s — confira $log"; exit 3 }
