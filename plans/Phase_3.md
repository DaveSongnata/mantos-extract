# Fase 3 — Extração real + import no canvas

- **STATUS:** [x] Implementada (2026-09-03). Build+testes verdes. **NÃO validada** contra
  CorelDRAW real nem OpenAI real (ver CHANGELOG.md e VALIDATION).
  **Amendment 2026-09-07:** M2 (débito de crédito na extração) foi revogada — ver
  `CHANGELOG.md` (entrada 2026-09-07) e `CLAUDE.md`. Os requisitos abaixo que mencionam débito
  de crédito descrevem o comportamento ORIGINAL desta fase, já superado.
- **OBJETIVO (1 frase):** Operador confirma elementos na tela de seleção, o addin chama
  `POST /api/v1/mantos-extract/extract` (novo) por elemento confirmado, e cada resultado
  (PNG transparente) entra no documento do Corel via `Layer.Import`.
- **OBJETIVO-DE-NEGÓCIO SERVIDO:** Fecha o ciclo "foto → elemento isolado dentro do Corel" —
  o núcleo do produto vendido, mesmo que ainda em resolução original (sem 2×, isso é Fase 4).

## REQUIREMENTS (EARS)

- **WHEN** o operador confirma N elementos e clica "Extrair", **THEN** o sistema SHALL
  chamar `/mantos-extract/extract` **sequencialmente**, um por vez (decisão: paralelo demais
  arrisca rate limit da OpenAI, e o operador já vê progresso incremental por elemento).
- **WHEN** um elemento extrai com sucesso, **THEN** o sistema SHALL salvar o PNG num temp
  file e SHALL chamar `Layer.Import` via `Type.InvokeMember` — nunca `dynamic` solto.
- **WHEN** o import termina, **THEN** o sistema SHALL medir o tamanho real da shape
  (só conhecido DEPOIS do import) e SHALL posicioná-la via `ElementLayout.NextSlot` (Core,
  pura/testável) — layout em grade simples, esquerda→direita com quebra de linha.
- **WHEN** um elemento falha (erro da OpenAI, timeout, import), **THEN** o sistema SHALL
  seguir extraindo os demais e marcar só aquele como falho — nunca aborta o lote inteiro.
- ~~**WHEN** o servidor responde `E_NO_CREDITS` no meio do lote, **THEN** o sistema SHALL parar
  o lote (não tenta os restantes) e SHALL contar quantos ficaram de fora, sem debitar por
  eles (o middleware já garante isso — só 2xx debita).~~ Requisito morto desde 2026-09-07: o
  endpoint não faz mais parte do grupo credit-checked, então nunca mais responde 402
  `E_NO_CREDITS`. O código JS/C# que trata esse código (`stopBatch`/`skippedNoCredits`, tela
  4.8) permanece no repo, inofensivo, só nunca mais dispara — ver CHANGELOG.
- **WHEN** a internet cai/sessão expira no meio do lote, **THEN** o sistema SHALL preservar
  o progresso já feito; se a sessão expirou (`E_UNAUTHORIZED`), SHALL voltar pro login.

## Backend novo (mantosfc) — implementado

- `mantos_extract_extract_controller.ts` — `POST /api/v1/mantos-extract/extract`. Até
  2026-09-07 vivia no mesmo grupo credit-checked (**revogado**, ver `CHANGELOG.md` — não
  debita mais crédito, log de uso em `generations` continua). Recebe a foto ORIGINAL + bbox
  0-1000 confirmado + label.
- `OpenAiVisionService.extractElement()` — **recorta no servidor via `sharp` ANTES** de
  chamar a OpenAI (decisão tomada aqui, não assumida do spec original — ver CHANGELOG),
  padding de 3% porque o box do operador é sugestão. Chama `POST /v1/images/edits`
  (`gpt-image-1`, `background:'transparent'`, `size:'auto'`), confirmado atual via busca na
  doc oficial (existe uma preview `gpt-image-2` que RECUSA `background` — `gpt-image-1`
  continua certo pra transparência).
- Upload no R2 (`r2_storage_service.ts`, já existente) + log em `generations`
  (`endpoint: 'mantos_extract_extract'`, `prompt: <label>`).

## TARGET FILES (implementados)

- `mantosfc/backend/app/controllers/v1/mantos_extract_extract_controller.ts`
- `mantosfc/backend/app/validators/mantos_extract.ts` (`extractValidator`: bbox + label)
- `mantos-extract/src/MantosExtract.Interop/{CorelImporter.cs}` + `ICorelHost`/`CorelHost`
  estendidos (`ImportPng`, `MoveLastImportedShape`, `RenameLastImportedShape`,
  `ActivePageBoundsMm`)
- `mantos-extract/src/MantosExtract.Core/Extract/*` (ExtractedImage, IExtractionClient,
  ExtractionClient — POST multipart + download da URL pública resultante)
- `mantos-extract/src/MantosExtract.Core/Layout/ElementLayout.cs` (layout puro/testável)
- `mantos-extract/src/MantosExtract.Core/NameSanitizer.cs` (spec §6, "José & Cia")
- `mantos-extract/src/MantosExtract.AddIn/Ui/MantosExtractBridge.cs` (comando `extract`,
  `RunExtractAsync` — orquestra extract→upscale→import→layout→rename por elemento)
- `mantos-extract/src/MantosExtract.AddIn/wwwroot/index.html` (telas 4.5/4.6, "tentar de
  novo" só com os elementos que falharam)

## EXEMPLARS TO MIRROR (confirmados na prática)

- `IVGLayer.Import(String, cdrFilter?, StructImportOptions?)` **confirmado real** no dump da
  typelib (`optimus/docs/vgcore-tlb-dump.txt`, dentro de `IVGLayer`) — chamado com 1 arg só
  (filtro inferido pela extensão .png). Comportamento de seleção pós-import
  (`ActiveSelectionRange`) segue **não confirmado empiricamente** — `CorelImporter` tenta ler
  a seleção ativa primeiro, cai pro último shape da layer por ordem de criação se falhar.

## DO NOT WANT (respeitado)

- Upscale (Fase 4) — importa na resolução que a OpenAI devolveu.
- Fitting no molde/grid do SISBOLT — fora de escopo (M6).

## VALIDATION

- **FEITO:** `tsc`/`eslint` limpos no backend. `dotnet test`: `ExtractionClient` contra
  handler fake (POST então GET, nunca baixa se o POST falhar, campos de form corretos),
  `ElementLayout` (5 cenários: primeiro elemento, mesma linha, quebra de linha, altura da
  linha usa o mais alto, elemento mais largo que a página não trava em loop),
  `NameSanitizer` (acentos preservados, nomes reservados do Windows, colisão de nome
  disambiguada com sufixo).
- **PENDENTE — VM real:** confirmar visualmente que o elemento aparece como objeto novo,
  fundo transparente, sem distorcer proporção, no lugar calculado pelo `ElementLayout`.
  ~~Confirmar no painel do mantosfc que o crédito foi debitado 1× por elemento com sucesso.~~
  Obsoleto desde 2026-09-07 — extração não debita mais crédito (ver CHANGELOG.md). Confirmar
  em vez disso que a chamada aparece logada em `/admin/generations` (endpoint
  `mantos_extract_extract`, sem efeito em `creditsRemaining`).
- **PENDENTE — comportamento real do `Layer.Import`:** a suposição de que o import deixa o
  resultado como seleção ativa é do spec original, não verificada em nenhum código real dos
  irmãos. Se a VM mostrar outro comportamento, só `CorelImporter.ResolveImportedShape`
  precisa mudar (já isolado ali de propósito).

## LESSONS

- **Import/posicionamento são DOIS métodos separados na interface**, não um só — o tamanho
  real da shape só é conhecido DEPOIS do import (Corel decide), e o `ElementLayout` (pura)
  precisa desse tamanho pra calcular o slot. Colapsar os dois numa chamada só teria acoplado
  layout (testável sem Corel) a COM (só testável na VM).
- **CS8602 (nullable) em condições `dynamic == null || dynamic.Foo`**: o analisador do
  Roslyn não propaga a narrowing de null através de `dynamic` dentro do mesmo `||`/`&&`.
  Split em dois `if` separados resolve — mesmo padrão que o `SelectionShapeCount` original já
  usava sem eu ter notado o porquê até bater no aviso.

## COVERAGE

REQUIREMENTS → backend (extract controller/service) + addin (Interop import, Core
Extract/Layout/NameSanitizer, Bridge `extract`, HTML 4.5/4.6).
