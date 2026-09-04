# Fase 4 — Upscale local 2× (Real-ESRGAN) entra no pipeline

- **STATUS:** [x] Código implementado e testado (2026-09-03) — **[ ] binário real NÃO
  bundlado** (não pode ser baixado/verificado responsavelmente a partir deste ambiente). O
  produto funciona sem ele (degrada pra `UpscaleStatus.BinaryMissing`, importa sem 2×); ver
  "O QUE FALTA" abaixo antes de considerar esta fase 100% pronta pra cliente.
- **OBJETIVO (1 frase):** Entre extração e import, cada PNG passa por
  `realesrgan-ncnn-vulkan.exe` (fator fixo 2×, GPU com fallback pra CPU) via processo
  externo, no padrão do `EngineRunner.cs`.
- **OBJETIVO-DE-NEGÓCIO SERVIDO:** Resolve a dor real identificada na pesquisa do mantosfc
  (transcrição da reunião com o Davidson: gabarito 4096px, OpenAI/Gemini 4K ≈ 3840px, manga
  real medida em 995px) — sem isso, o resultado sai pixelado quando ampliado no molde.

## REQUIREMENTS (EARS)

- **WHEN** uma extração termina com sucesso, **THEN** o sistema SHALL invocar
  `realesrgan-ncnn-vulkan.exe` sobre o PNG resultante ANTES de chamar `Layer.Import`.
- **WHEN** o binário não está instalado, **THEN** o sistema SHALL importar o PNG original
  sem upscale, sem travar e sem mostrar erro pro operador (log interno só).
- **WHEN** o upscale falha ou estoura timeout (90 s), **THEN** o sistema SHALL importar o
  PNG ORIGINAL em vez de bloquear o elemento — crédito já foi debitado na extração.
- **WHEN** o processo termina com exit 0 mas SEM criar o arquivo de saída esperado, **THEN**
  o sistema SHALL tratar como falha (nunca confiar cegamente no exit code).

## TARGET FILES (implementados)

- `mantos-extract/src/MantosExtract.Core/Upscale/{UpscaleRunner.cs, UpscalePaths.cs}`
  (mirror de `EngineRunner.cs`/`EnginePaths.cs`: arquivo → `Process` com timeout → exit code
  → arquivo de saída; sem "dev build" fallback porque não compilamos o binário, ele é
  bundlado pronto)
- `mantos-extract/src/MantosExtract.AddIn/Ui/MantosExtractBridge.cs::ApplyUpscale` —
  degrada pro PNG original em QUALQUER status que não seja `Success`
- `mantos-extract/THIRD-PARTY-NOTICES.txt` — atribuição MIT + status "NÃO bundlado ainda"
- `mantos-extract/scripts/deploy-dev.ps1` — empacota `assets/upscale/*` se existir, avisa
  (não falha) se não existir

## EXEMPLARS TO MIRROR

- `siscut/plugin/src/SisCut.Engine/EngineRunner.cs` — a FORMA foi copiada (arg-building puro
  e testável separado do processo genérico `RunProcess`, testado contra `cmd.exe` real —
  ver LESSONS).

## O QUE FALTA (não é código — é um passo manual, humano)

1. Baixar `realesrgan-ncnn-vulkan` (release oficial, https://github.com/xinntao/
   Real-ESRGAN-ncnn-vulkan) — **decisão pendente de qual modelo/.param usar**: o conteúdo
   aqui é estampa/logo gráfico, não foto genérica; vale testar as variantes
   `realesrgan-x4plus` vs. as de anime/linha (o padrão da lib são fatores 4×, temos que
   confirmar que aceita `-s 2` ou se precisamos rodar 4× e reamostrar pra 2× depois — **não
   assumido, precisa de teste real**).
2. Verificar o checksum, colocar em `assets/upscale/` (nome exato:
   `realesrgan-ncnn-vulkan.exe` + os `.param`/`.bin` do modelo escolhido).
3. Rodar `scripts/deploy-dev.ps1` de novo — ele empacota automaticamente se o arquivo
   existir.
4. Medir tempo real em CPU (sem GPU) numa máquina de confecção típica — decide se precisa de
   estimativa de tempo na UI ou só "processando, aguarde" (mockup 4.5 já cobre ambos).

## VALIDATION

- **FEITO:** `UpscaleRunner.BuildArguments` testado puro (inclusive quoting de path com
  espaço). `UpscaleRunner.RunProcess` testado contra `cmd.exe` REAL (não um fake) nos 4
  cenários que importam: processo cria o arquivo esperado → `Success`; exit≠0 → `Failed`;
  exit 0 SEM criar o arquivo → `Failed` (nunca confia cegamente no exit code); processo que
  não termina a tempo → `Timeout` com kill efetivo. Mesma filosofia de
  `SisCut.Engine.EngineRunnerTests` (testar o processo de verdade), só que aqui o "processo
  de verdade" disponível neste ambiente é `cmd.exe`, não o binário real do Real-ESRGAN — ver
  nota de honestidade abaixo.
- **NÃO FEITO, e não dá pra fingir que foi:** nenhuma chamada real ao
  `realesrgan-ncnn-vulkan.exe` aconteceu. Os testes provam que o MOTOR de invocação de
  processo (timeout, exit code, checagem de arquivo) está correto — não provam que os flags
  `-i/-o/-s 2` são exatamente o que o binário real espera até alguém rodar com o binário de
  verdade.

## LESSONS

- **"Testar contra o processo real" nem sempre significa o processo FINAL real.** O
  `EngineRunnerTests` do SisCut roda o `nest-engine` de verdade porque ele já existe no
  próprio repo. Aqui o "processo real" não existe ainda (é um binário de terceiros que ainda
  precisa ser baixado por um humano) — a saída honesta foi separar o motor de invocação
  (testável com QUALQUER processo, usei `cmd.exe`) do arg-building específico do
  Real-ESRGAN (testado puro, sem processo nenhum). Fingir que testei contra o binário real
  teria sido pior que documentar a lacuna.

## COVERAGE

REQUIREMENTS → `UpscaleRunner`/`UpscalePaths` + `ApplyUpscale` no Bridge. Falta só o binário
em si — ver "O QUE FALTA".
