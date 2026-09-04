# deploy-dev.ps1 — builds Mantos Extract and copies the addon payload straight into every
# installed CorelDRAW's Addons folder. This is the Fase 1-5 "manual copy" from
# plans/Phase_1.md (VALIDATION) turned into one command — NOT the polished single-EXE
# installer (that one needs real icon art from Davidson + an ILRepack wizard UI, neither of
# which exist yet; installer/MantosExtract.Installer.csproj is still a placeholder, see its
# own header comment and plans/Phase_5.md).
#
# Run from anywhere, on the VM (or any machine with CorelDRAW installed):
#   powershell -ExecutionPolicy Bypass -File scripts\deploy-dev.ps1
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$cfg = 'Release'; $tfm = 'net48'

function Step($m){ Write-Host "==> $m" -ForegroundColor Cyan }

# 0. Static-check the page's JavaScript BEFORE anything is compiled or shipped — a missing
#    helper broke Optimus's docker silently for weeks despite 414 green C# tests (O18,
#    ../CLAUDE.md). This is cheap insurance every deploy pays.
Step 'Verificando o JavaScript da tela'
& node "$root\scripts\check-ui-js.js"
if ($LASTEXITCODE -ne 0) { throw "JavaScript da tela com erro - deploy abortado." }

# 1. Build the whole solution.
Step 'Compilando MantosExtract.sln'
& dotnet build "$root\MantosExtract.sln" -c $cfg --no-incremental -v quiet
if ($LASTEXITCODE -ne 0) { throw "Build falhou - deploy abortado." }

$addinOut = "$root\src\MantosExtract.AddIn\bin\$cfg\$tfm"
$resOut = "$root\src\MantosExtract.Resources\bin\$cfg\$tfm"
if (-not (Test-Path "$addinOut\MantosExtract.AddIn.dll")) {
    throw "MantosExtract.AddIn.dll nao encontrado em $addinOut apos o build."
}

# 2. Assemble the payload (everything that lands in <Corel>\Programs64\Addons\MantosExtract\).
$payload = "$root\dist\payload"
Step "Montando payload em $payload"
if (Test-Path $payload) { Remove-Item $payload -Recurse -Force }
New-Item -ItemType Directory -Force -Path $payload | Out-Null
Get-ChildItem "$addinOut\*.dll" | Copy-Item -Destination $payload -Force
if (Test-Path "$resOut\MantosExtract.Resources.dll") {
    Copy-Item "$resOut\MantosExtract.Resources.dll" $payload -Force
} else {
    Write-Warning "MantosExtract.Resources.dll nao encontrado em $resOut - o botao vai renderizar sem icone (placeholder, ver plans/Phase_1.md)."
}

$webView2Loader = "$addinOut\runtimes\win-x64\native\WebView2Loader.dll"
if (Test-Path $webView2Loader) {
    Copy-Item $webView2Loader $payload -Force
} else {
    Write-Warning "WebView2Loader.dll nao encontrado em $webView2Loader — o docker pode falhar ao iniciar."
}

Copy-Item "$root\src\MantosExtract.AddIn\addon\*" $payload -Force
Write-Host ("    {0} arquivo(s) no payload" -f (Get-ChildItem $payload -File).Count)

# 2b. Upscale binary (Fase 4) — NOT bundled by this repo (plans/Phase_4.md: it is a
#     third-party redistributable, downloaded/verified by a human, never fabricated here).
#     Copied into the payload ONLY if a human already placed it under assets/upscale/.
$upscaleSrc = "$root\assets\upscale"
$upscaleOut = "$payload\upscale"
if (Test-Path "$upscaleSrc\realesrgan-ncnn-vulkan.exe") {
    Step 'Empacotando realesrgan-ncnn-vulkan.exe'
    New-Item -ItemType Directory -Force -Path $upscaleOut | Out-Null
    Copy-Item "$upscaleSrc\*" $upscaleOut -Recurse -Force
} else {
    Write-Warning "assets\upscale\realesrgan-ncnn-vulkan.exe ausente — upscale vai degradar sempre pra UpscaleStatus.BinaryMissing (elemento importado sem 2x). Ver plans/Phase_4.md."
}

# 3. Find every installed CorelDRAW and copy the payload into its Addons folder.
$suiteRoot = Join-Path ([Environment]::GetFolderPath('ProgramFiles')) 'Corel\CorelDRAW Graphics Suite'
if (-not (Test-Path $suiteRoot)) {
    Write-Warning "Nenhum CorelDRAW encontrado em $suiteRoot — payload montado em $payload, mas nao copiado. Rode este script na VM."
    exit 0
}

$deployed = 0
Get-ChildItem $suiteRoot -Directory | ForEach-Object {
    $addons = Join-Path $_.FullName 'Programs64\Addons'
    if (-not (Test-Path $addons)) { return }

    $target = Join-Path $addons 'MantosExtract'
    Step "Copiando para $target"
    New-Item -ItemType Directory -Force -Path $target | Out-Null
    Copy-Item "$payload\*" $target -Recurse -Force
    $deployed++
}

if ($deployed -eq 0) {
    Write-Warning "CorelDRAW Graphics Suite encontrado em $suiteRoot, mas nenhuma pasta Programs64\Addons dentro dela."
} else {
    Write-Host ""
    Write-Host "PRONTO — $deployed instalacao(oes) do CorelDRAW atualizada(s)." -ForegroundColor Green
    Write-Host "Abra o CorelDRAW segurando F8 para forcar o rebuild do workspace (le o XSLT de novo)."
    Write-Host "Confira plans/Phase_1.md (VALIDATION) para o roteiro de teste manual."
}
