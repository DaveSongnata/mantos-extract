# build-all.ps1 — builds Mantos Extract end to end and publishes a VERSIONED delivery kit.
#
#   1. MantosExtract.Resources (icon DLL)   2. MantosExtract.AddIn (+ Core/Interop/Windows,
#      pulled in via ProjectReference)
#   3. dist/payload/         (everything a CorelDRAW install needs under
#      Programs64\Addons\MantosExtract\)
#   4. dist/MantosExtract_Kit/  (payload + THIRD-PARTY-NOTICES.txt, zipped)
#   5. shared/bin/redistributables/Clientes/<versao>/  (publicado, NUNCA sobrescreve versões
#      antigas — mesmo padrão de ../optimus/scripts/build-all.ps1 e do SisCut)
#
# NAO gera um instalador single-EXE ainda — installer/MantosExtract.Installer.csproj continua
# placeholder (falta o ícone aprovado pelo Davidson + o wizard WinForms, ver
# plans/Phase_5.md item 3). Até lá o kit ZIP + cópia manual em Programs64\Addons\MantosExtract\
# (scripts/deploy-dev.ps1 automatiza isso numa única máquina) É o entregável.
#
# Run from anywhere:  powershell -ExecutionPolicy Bypass -File scripts\build-all.ps1
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$dotnet = 'dotnet'
$cfg = 'Release'; $tfm = 'net48'

function Step($m){ Write-Host "==> $m" -ForegroundColor Cyan }

# 0. Static-check the page's JavaScript BEFORE anything is compiled or shipped (lição O18 do
#    Optimus, ../optimus/CLAUDE.md — um helper faltando derrubou o docker em silêncio por
#    semanas apesar de centenas de testes C# verdes, porque nenhum deles enxerga JavaScript).
Step 'Verificando o JavaScript da tela'
& node "$root\scripts\check-ui-js.js"
if ($LASTEXITCODE -ne 0) { throw "JavaScript da tela com erro - build abortado." }

# 1. Build the icon DLL (placeholder até o Davidson aprovar uma arte — plans/Phase_5.md item 2).
Step 'Compilando MantosExtract.Resources'
& $dotnet build "$root\src\MantosExtract.Resources\MantosExtract.Resources.csproj" -c $cfg -v quiet
if ($LASTEXITCODE -ne 0) { throw "Build de MantosExtract.Resources falhou - build abortado." }

# 2. Build the add-in (traz Core/Interop/Windows junto via ProjectReference).
Step 'Compilando MantosExtract.AddIn'
& $dotnet build "$root\src\MantosExtract.AddIn\MantosExtract.AddIn.csproj" -c $cfg --no-incremental -v quiet
if ($LASTEXITCODE -ne 0) { throw "Build de MantosExtract.AddIn falhou - build abortado." }

$addinOut = "$root\src\MantosExtract.AddIn\bin\$cfg\$tfm"
$resOut   = "$root\src\MantosExtract.Resources\bin\$cfg\$tfm"

# 3. Assemble the payload (everything that lands in <Corel>\Programs64\Addons\MantosExtract\).
$payload = "$root\dist\payload"
Step "Montando payload em $payload"
if (Test-Path $payload) { Remove-Item $payload -Recurse -Force }
New-Item -ItemType Directory -Force -Path $payload | Out-Null
Get-ChildItem "$addinOut\*.dll" | Copy-Item -Destination $payload -Force
if (Test-Path "$resOut\MantosExtract.Resources.dll") {
    Copy-Item "$resOut\MantosExtract.Resources.dll" $payload -Force
} else {
    Write-Warning "MantosExtract.Resources.dll nao encontrado - o botao vai renderizar sem icone (plans/Phase_5.md item 2)."
}

$webView2Loader = "$addinOut\runtimes\win-x64\native\WebView2Loader.dll"
if (-not (Test-Path $webView2Loader)) { throw "WebView2Loader.dll nao encontrado em $webView2Loader - build abortado." }
Copy-Item $webView2Loader $payload -Force

Copy-Item "$root\src\MantosExtract.AddIn\addon\*" $payload -Force

# 3b. Upscale binary (Fase 4) — redistribuível de terceiros, só empacotado se um humano já
#     colocou o binário em assets/upscale/ (nunca baixado/fabricado aqui, plans/Phase_4.md).
$upscaleSrc = "$root\assets\upscale"
if (Test-Path "$upscaleSrc\realesrgan-ncnn-vulkan.exe") {
    Step 'Empacotando realesrgan-ncnn-vulkan.exe'
    $upscaleOut = "$payload\upscale"
    New-Item -ItemType Directory -Force -Path $upscaleOut | Out-Null
    Copy-Item "$upscaleSrc\*" $upscaleOut -Recurse -Force
} else {
    Write-Warning "assets\upscale\realesrgan-ncnn-vulkan.exe ausente - upscale vai degradar pra BinaryMissing neste kit (plans/Phase_4.md)."
}

Write-Host ("    {0} arquivo(s) no payload" -f (Get-ChildItem $payload -File -Recurse).Count)

# 4. Monta o kit de entrega (payload + avisos de licença de terceiros).
$kit = "$root\dist\MantosExtract_Kit"
Step "Montando kit de entrega em $kit"
if (Test-Path $kit) { Remove-Item $kit -Recurse -Force }
New-Item -ItemType Directory -Force -Path $kit | Out-Null
Copy-Item "$payload\*" $kit -Recurse -Force
Copy-Item "$root\THIRD-PARTY-NOTICES.txt" $kit -Force

# 5. Publica numa pasta de cliente VERSIONADA. Versões anteriores NUNCA são apagadas — cada
#    release guarda sua própria pasta, então dá pra sempre voltar o cliente pra um build
#    anterior (mesmo padrão de ../optimus/scripts/build-all.ps1 e do
#    shared/bin/redistributables/Clientes/ do SisCut).
$version = (Select-String -Path "$root\src\MantosExtract.AddIn\Build.cs" -Pattern 'Tag\s*=\s*"([^"]+)"').Matches[0].Groups[1].Value
if (-not $version) { throw "Nao foi possivel ler a versao de Build.cs" }
$clientes = "$root\shared\bin\redistributables\Clientes\$version"
Step "Publicando versao $version em $clientes"
New-Item -ItemType Directory -Force -Path $clientes | Out-Null   # nunca Remove-Item o pai
Copy-Item "$root\THIRD-PARTY-NOTICES.txt" $clientes -Force
Compress-Archive -Path "$kit\*" -DestinationPath "$clientes\MantosExtract_Kit.zip" -Force

$zipMb = [math]::Round((Get-Item "$clientes\MantosExtract_Kit.zip").Length/1MB, 1)
Write-Host ""
Write-Host "PRONTO." -ForegroundColor Green
Write-Host "Versao $version ($zipMb MB): $clientes\MantosExtract_Kit.zip"
Write-Host "Kit manual (sem instalador single-EXE ainda, ver plans/Phase_5.md item 3) - extrair"
Write-Host "em Programs64\Addons\MantosExtract\ de cada instalacao do CorelDRAW."
Write-Host "Versoes disponiveis:"
Get-ChildItem "$root\shared\bin\redistributables\Clientes" -Directory | ForEach-Object { Write-Host ("  - " + $_.Name) }
