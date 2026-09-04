# build-all.ps1 — builds Mantos Extract end to end and publishes a VERSIONED, single-EXE
# delivery.
#
#   1. MantosExtract.Resources (icon DLL)   2. MantosExtract.AddIn (+ Core/Interop/Windows,
#      pulled in via ProjectReference)
#   3. dist/payload/  (everything a CorelDRAW install needs under
#      Programs64\Addons\MantosExtract\ — embedded INTO the installer next, not shipped loose)
#   4. MantosExtract.Installer (embeds the payload, ILRepack merges it into a single EXE)
#   5. shared/bin/redistributables/Clientes/<versao>/  (publicado, NUNCA sobrescreve versões
#      antigas — mesmo padrão de ../optimus/scripts/build-all.ps1 e do SisCut)
#
# Sem ícone de marca ainda (Davidson não aprovou uma arte — plans/Phase_5.md item 2): o
# instalador e o botão do addon rodam com o ícone padrão até lá. Isso é cosmético, não afeta
# a instalação real.
#
# Run from anywhere:  powershell -ExecutionPolicy Bypass -File scripts\build-all.ps1
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$dotnet = 'dotnet'
$cfg = 'Release'; $tfm = 'net48'

function Step($m){ Write-Host "==> $m" -ForegroundColor Cyan }

# 0. Static-check both pages' JavaScript BEFORE anything is compiled or shipped (lição O18 do
#    Optimus, ../optimus/CLAUDE.md — um helper faltando derrubou o docker em silêncio por
#    semanas apesar de centenas de testes C# verdes, porque nenhum deles enxerga JavaScript).
Step 'Verificando o JavaScript das telas'
& node "$root\scripts\check-ui-js.js"
if ($LASTEXITCODE -ne 0) { throw "JavaScript de alguma tela com erro - build abortado." }

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

# 3. Assemble the payload (everything the installer embeds and deploys under
#    <Corel>\Programs64\Addons\MantosExtract\).
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
#     Se ausente, o instalador sai sem ele e o UpscaleRunner degrada pra BinaryMissing em
#     runtime — não impede a instalação nem o uso do resto do addin.
$upscaleSrc = "$root\assets\upscale"
if (Test-Path "$upscaleSrc\realesrgan-ncnn-vulkan.exe") {
    Step 'Empacotando realesrgan-ncnn-vulkan.exe'
    $upscaleOut = "$payload\upscale"
    New-Item -ItemType Directory -Force -Path $upscaleOut | Out-Null
    Copy-Item "$upscaleSrc\*" $upscaleOut -Recurse -Force
} else {
    Write-Warning "assets\upscale\realesrgan-ncnn-vulkan.exe ausente - upscale vai degradar pra BinaryMissing neste build (plans/Phase_4.md)."
}

Write-Host ("    {0} arquivo(s) no payload" -f (Get-ChildItem $payload -File -Recurse).Count)

# 4. Build the installer (embeds dist/payload/* + THIRD-PARTY-NOTICES.txt, ILRepack merges
#    everything managed into ONE exe on Release). The version is stamped into the assembly so
#    the "Apps & features" entry shows the build the customer actually received —
#    InstallerEngine.Version reads it back at runtime.
$versionTag = (Select-String -Path "$root\src\MantosExtract.AddIn\Build.cs" -Pattern 'Tag\s*=\s*"([^"]+)"').Matches[0].Groups[1].Value
if (-not $versionTag) { throw "Nao foi possivel ler a versao de Build.cs" }
Step "Compilando MantosExtract.Installer (v$versionTag)"
& $dotnet build "$root\installer\MantosExtract.Installer.csproj" -c $cfg --no-incremental -v quiet `
    -p:Version=$versionTag -p:AssemblyVersion="$versionTag.0" -p:FileVersion="$versionTag.0"
if ($LASTEXITCODE -ne 0) { throw "Build de MantosExtract.Installer falhou - build abortado." }

# 5. Grab the single merged EXE (ILRepack output). Plug and play — no loose DLLs.
$setup = "$root\dist\MantosExtract_Setup"
Step "Montando distribuição (EXE único) em $setup"
if (Test-Path $setup) { Remove-Item $setup -Recurse -Force }
New-Item -ItemType Directory -Force -Path $setup | Out-Null
$packed = "$root\installer\bin\$cfg\$tfm\packed\MantosExtract_Setup.exe"
if (-not (Test-Path $packed)) { throw "ILRepack nao gerou $packed" }
Copy-Item $packed $setup -Force
Copy-Item "$root\THIRD-PARTY-NOTICES.txt" $setup -Force

# 6. Publica numa pasta de cliente VERSIONADA. Versões anteriores NUNCA são apagadas — cada
#    release guarda sua própria pasta, então dá pra sempre voltar o cliente pra um build
#    anterior (mesmo padrão de ../optimus/scripts/build-all.ps1 e do
#    shared/bin/redistributables/Clientes/ do SisCut).
$clientes = "$root\shared\bin\redistributables\Clientes\$versionTag"
Step "Publicando versao $versionTag em $clientes"
New-Item -ItemType Directory -Force -Path $clientes | Out-Null   # nunca Remove-Item o pai
Copy-Item "$setup\MantosExtract_Setup.exe" $clientes -Force
Copy-Item "$root\THIRD-PARTY-NOTICES.txt" $clientes -Force
Compress-Archive -Path "$setup\MantosExtract_Setup.exe", "$setup\THIRD-PARTY-NOTICES.txt" `
    -DestinationPath "$clientes\MantosExtract_Setup.zip" -Force

$exeMb = [math]::Round((Get-Item $packed).Length/1MB, 1)
Write-Host ""
Write-Host "PRONTO." -ForegroundColor Green
Write-Host "Versao $versionTag ($exeMb MB): $clientes\MantosExtract_Setup.exe  (+ .zip)"
Write-Host "Versoes disponiveis:"
Get-ChildItem "$root\shared\bin\redistributables\Clientes" -Directory | ForEach-Object { Write-Host ("  - " + $_.Name) }
Write-Host "Rode MantosExtract_Setup.exe como Administrador, com o CorelDRAW fechado."
