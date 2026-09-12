# Upscale — binário de terceiro (não versionado)

`assets/upscale/` fica vazio no git (`.gitignore`) — um humano precisa colocar os arquivos
abaixo aqui antes de rodar `scripts/build-all.ps1`, senão o instalador sai sem upscale e
`UpscaleRunner` degrada pra `UpscaleStatus.BinaryMissing` em runtime (não quebra nada, só não
upscala).

## Onde conseguir (confirmado 2026-09-11 via GitHub REST API)

**Não use os releases de `xinntao/Real-ESRGAN-ncnn-vulkan`** — os zips Windows desse repo
(ex.: v0.2.0, v0.1.3.2) só têm o `.exe` + DLLs, **sem nenhum modelo** dentro.

Use o release do repo principal:

- Repo: `xinntao/Real-ESRGAN`
- Release: `v0.2.5.0`
- Asset: `realesrgan-ncnn-vulkan-20220424-windows.zip` (~43MB)

Dentro do zip, copiar pra cá:

```
assets/upscale/realesrgan-ncnn-vulkan.exe
assets/upscale/vcomp140.dll
assets/upscale/vcomp140d.dll
assets/upscale/models/realesrgan-x4plus-anime.bin      <- modo GPU
assets/upscale/models/realesrgan-x4plus-anime.param
assets/upscale/models/realesr-animevideov3-x2.bin      <- modo CPU
assets/upscale/models/realesr-animevideov3-x2.param
```

**Não copiar os outros modelos do zip** (`realesrgan-x4plus`, `realesrnet-x4plus`,
`realesr-animevideov3-x3/x4`) — não são usados e só engordam o instalador à toa (o x4plus
sozinho tem 33MB contra os 8,9MB do que usamos).

## Modo CPU (máquina sem placa de vídeo) — `cpu-vulkan/`

O binário exige um device Vulkan, e numa VM sem aceleração 3D não existe nenhum (morre em
`vkCreateInstance failed -9`). Pra esses casos empacotamos o **lavapipe**, o Vulkan por
SOFTWARE da Mesa: apontado por `VK_DRIVER_FILES` só no processo filho, o mesmo binário roda
100% em CPU.

- Repo: `pal1000/mesa-dist-win`
- Release usado: **26.2.0**, asset `mesa3d-26.2.0-release-msvc.7z`
- Copiar de `x64/` pra cá (os dois juntos — o ICD referencia a DLL por caminho relativo):

```
assets/upscale/cpu-vulkan/lvp_icd.x86_64.json
assets/upscale/cpu-vulkan/vulkan_lvp.dll     (~54MB)
```

**Não copiar `vulkan_dzn.dll`** (Dozen, Vulkan sobre D3D12): testado, devolve
`invalid gpu device` — não serve.

Tempos medidos (1254×1254, máquina de dev): GPU 9s · CPU com o modelo compacto **89s** · CPU
com o modelo de GPU 640s (por isso o modo CPU usa outro modelo, ver `UpscalePaths.CpuModelName`).

## Por que `realesrgan-x4plus-anime` (o nome engana)

"anime" aqui quer dizer **treinado em arte ilustrada: borda dura, cor chapada** — que é
exatamente o que estampa, logo, escudo e número de camisa são. Comparado 1:1 com o
`realesrgan-x4plus` (treinado em foto real) na mesma imagem, devolveu bordas mais limpas,
sendo **2,7× mais rápido** (7,8s vs 20,9s numa RTX 3050) e com modelo **3,7× menor**.

**Nunca usar `realesr-animevideov3`** (o default do binário quando não se passa `-n`): é o mais
rápido de todos (2,6s), mas **zera o canal alpha** — testado, devolve a imagem inteira
transparente. Todo elemento extraído é PNG com transparência (M6), então sairia invisível no
Corel. `UpscalePaths.ModelName` fixa o modelo e `UpscaleRunner.BuildArguments` sempre passa
`-m`/`-n` explicitamente; nunca depender do default do binário.

## Por que `-s 4` e não `-s 2`, se o produto entrega 2×

Porque `-s` precisa ser a escala NATIVA da rede. Pedir `-s 2` de um modelo nativo 4× não produz
um 2×: o binário posiciona os tiles como se a saída fosse 2× enquanto a rede devolve tiles 4×, e
a imagem sai num mosaico de blocos desencontrados (bug real, visível a olho nu). O pipeline roda
`-s 4` e reduz pela metade em `MantosExtract.Windows.ImageDownscaler` — que além de correto é
melhor que um 2× direto, porque reduzir de 4× faz supersampling e suaviza os artefatos da rede.

## Layout esperado em runtime

```
<install-dir>\upscale\realesrgan-ncnn-vulkan.exe
<install-dir>\upscale\vcomp140.dll
<install-dir>\upscale\vcomp140d.dll
<install-dir>\upscale\models\realesrgan-x4plus-anime.bin
<install-dir>\upscale\models\realesrgan-x4plus-anime.param
<install-dir>\upscale\models\realesr-animevideov3-x2.bin
<install-dir>\upscale\models\realesr-animevideov3-x2.param
<install-dir>\upscale\cpu-vulkan\lvp_icd.x86_64.json
<install-dir>\upscale\cpu-vulkan\vulkan_lvp.dll
```

`build-all.ps1` copia `assets/upscale/*` (recursivo) pra `payload/upscale/` se
`realesrgan-ncnn-vulkan.exe` existir aqui — mesma estrutura relativa, preservada. Fator final
sempre 2× (M8), agora **opcional**: o operador aciona peça por peça na tela de resultado.
Sem estes arquivos o addin instala e funciona igual, só sem oferecer o botão de upscale.
