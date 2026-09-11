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
assets/upscale/models/realesrgan-x4plus.bin
assets/upscale/models/realesrgan-x4plus.param
```

**Não copiar os outros modelos do zip** (`realesrgan-x4plus-anime`, `realesrnet-x4plus`,
`realesr-animevideov3*`) — não são usados e só engordam o instalador à toa.

## Por que `realesrgan-x4plus` e não o default do binário

Sem `-n` explícito, o binário cai no seu próprio default (`realesr-animevideov3`), otimizado
pra vídeo de anime — errado pra estampa/logo de roupa. `UpscalePaths.ModelName` fixa
`realesrgan-x4plus` (modelo geral) e `UpscaleRunner.BuildArguments` sempre passa `-m`/`-n`
explicitamente; nunca depender do default do binário.

## Layout esperado em runtime

```
<install-dir>\upscale\realesrgan-ncnn-vulkan.exe
<install-dir>\upscale\vcomp140.dll
<install-dir>\upscale\vcomp140d.dll
<install-dir>\upscale\models\realesrgan-x4plus.bin
<install-dir>\upscale\models\realesrgan-x4plus.param
```

`build-all.ps1` copia `assets/upscale/*` (recursivo) pra `payload/upscale/` se
`realesrgan-ncnn-vulkan.exe` existir aqui — mesma estrutura relativa, preservada. Fator fixo
2x (`-s 2`, M8) — nenhuma opção pro operador.
