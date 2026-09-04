# Fase 2 — Detecção real (OpenAI, server-side) + tela de seleção

- **STATUS:** [x] Implementada (2026-09-03). Build+testes verdes nos dois repos (mantosfc:
  `tsc`/`eslint` limpos; mantos-extract: 276/276 testes). **NÃO validada** contra CorelDRAW
  real nem contra a OpenAI real — nenhum dos dois disponível neste ambiente (ver CHANGELOG.md
  e VALIDATION abaixo).
- **OBJETIVO (1 frase):** Operador seleciona um bitmap no Corel, clica "detectar", o addin
  chama `POST /api/v1/mantos-extract/detect` (novo, no mantosfc) que roda OpenAI
  server-side, e a tela de seleção (mockup 4.4) mostra as caixas + checklist reais.
- **OBJETIVO-DE-NEGÓCIO SERVIDO:** Prova a ponta mais arriscada tecnicamente (backend novo +
  OpenAI structured output) antes de gastar esforço em extração/upscale/import.

## REQUIREMENTS (EARS)

- **WHEN** o operador seleciona um bitmap no Corel, **THEN** o sistema SHALL habilitar o
  botão "Detectar" — `ICorelHost.SelectionIsSingleBitmap`, reportado no heartbeat `status`.
- **WHEN** o operador clica "Detectar", **THEN** o sistema SHALL exportar a imagem
  selecionada (`CorelExporter.ExportSelectionToPng`, `Type.InvokeMember`+`Finish()`) e enviar
  pro endpoint novo com `Authorization: Bearer <sessão>` + `X-OpenAI-Api-Key` do tenant.
- **WHEN** o endpoint responde 2xx, **THEN** o sistema SHALL debitar 1 crédito (automático via
  `credit_check_middleware.ts` reusado, mesmo grupo de rota) e SHALL desenhar as caixas
  retornadas sobre a imagem + checklist.
- **WHEN** o endpoint responde 402 `E_NO_CREDITS`, **THEN** o sistema SHALL mostrar a tela
  de créditos esgotados (mockup 4.8) — `handleActionError` no JS.
- **WHEN** falha de rede, **THEN** o sistema SHALL mostrar a tela de erro (4.7) com detalhe
  técnico colapsado.

Não implementado (fora do escopo desta fase, ver LESSONS): heurística de "imagem de baixa
qualidade" antes de gastar crédito — adiado pra Fase 5, ver nota lá.

## Backend novo (mantosfc) — implementado

- `openai_credentials.ts` — espelha `gemini_credentials.ts`. Diferença real: recebe
  `{defaultModel, allowedModels}` por chamada (não uma allowlist fixa única), porque detecção
  e extração usam famílias de modelo DIFERENTES.
- `openai_vision_service.ts` — `detectElements()` via `POST /v1/chat/completions` com
  `response_format:{type:'json_schema',...,strict:true}` — confirmado GA/atual via busca na
  doc oficial da OpenAI antes de codar (não assumido). Schema fixa
  `{elements:[{id,label,bbox:{x_min,y_min,x_max,y_max}}]}`, coordenadas 0-1000.
- `mantos_extract_detect_controller.ts` — `POST /api/v1/mantos-extract/detect`, no MESMO
  grupo `routes.ts` `[creditCheck, activeSubscription]` — decisão do Dave (CHANGELOG),
  detecção debita crédito igual qualquer outra geração.
- Log na tabela `generations` existente (`endpoint: 'mantos_extract_detect'`), sem tabela
  nova — `GenerationEndpoint` (generation.ts) estendido com os dois valores novos.
- **Deliberadamente sem SDK `openai` no npm** — `fetch`/`FormData` nativos do Node 24 (ver
  CHANGELOG "OpenAI via fetch nativo").

## TARGET FILES (implementados)

- `mantosfc/backend/app/services/openai_credentials.ts`
- `mantosfc/backend/app/services/openai_vision_service.ts`
- `mantosfc/backend/app/controllers/v1/mantos_extract_detect_controller.ts`
- `mantosfc/backend/app/validators/mantos_extract.ts`
- `mantosfc/backend/app/models/generation.ts` (`GenerationEndpoint` estendido)
- `mantosfc/backend/start/env.ts` (`OPENAI_API_KEY`/`OPENAI_DETECTION_MODEL`/
  `OPENAI_EXTRACTION_MODEL` no schema)
- `mantosfc/backend/start/routes.ts` (rotas no grupo de geração existente)
- `mantosfc/.env.example`, `mantosfc/docker-compose.yml` (vars documentadas/passadas)
- `mantosfc/backend/tests/functional/mantos_extract_routes.spec.ts`
- `mantos-extract/src/MantosExtract.Interop/{CorelConstants.cs, CorelExporter.cs,
  CorelDocumentState.cs, ICorelHost.cs, CorelHost.cs}` (seleção real + export)
- `mantos-extract/src/MantosExtract.Core/Detect/*` (BoundingBox, DetectedElement,
  DetectionResult, IDetectionClient, DetectionClient)
- `mantos-extract/src/MantosExtract.Core/Api/MantosExtractApiException.cs`
- `mantos-extract/src/MantosExtract.AddIn/Ui/MantosExtractBridge.cs` (comando `detect`,
  status estendido com `selIsBitmap`, mapeamento de assets via segundo virtual host)
- `mantos-extract/src/MantosExtract.AddIn/wwwroot/index.html` (telas 4.2/4.3/4.4 reais)

## DO NOT WANT (respeitado)

- Nenhuma extração nesta fase (Fase 3, separada). A bbox é só visual/sugestão (M1).
- Nenhum provider alternativo — OpenAI direto, sem abstração (M7).

## VALIDATION

- **FEITO:** `tsc --noEmit` limpo no backend inteiro (incluindo os arquivos novos);
  `eslint` limpo nos arquivos novos/tocados (routes.ts tinha ~30 violações de prettier
  PRÉ-EXISTENTES no resto do arquivo, não tocadas — fora de escopo). Teste funcional
  `mantos_extract_routes.spec.ts` escrito espelhando `generation_middleware_chain.spec.ts`
  (401 sem sessão, 422 parado na validação com sessão válida, 402 tenant expirado, 422
  nunca-401 pra chave OpenAI ausente, 402 sem crédito) — **não executado**, sem Postgres
  acessível neste ambiente.
- `dotnet test` no mantos-extract cobre: parsing de `DetectionResult` (incluindo box
  degenerada sendo descartada sem quebrar o resto), `DetectionClient` contra handler HTTP
  fake (headers corretos, erro relaiado do servidor), `NameSanitizer`, `ElementLayout`.
- **PENDENTE — VM real:** rodar `scripts/deploy-dev.ps1` numa máquina com CorelDRAW,
  selecionar uma foto real de celular, clicar Detectar, confirmar que as caixas aparecem no
  lugar aproximado certo (erradas às vezes são esperadas — é sugestão, M1).
- **PENDENTE — OpenAI real:** nenhuma chamada real foi feita (sem chave de teste
  disponível aqui). O schema/prompt em `openai_vision_service.ts` é a melhor aposta fundamentada
  em busca na doc oficial, mas só uma chamada real confirma que o modelo respeita o schema
  0-1000 na prática.

## LESSONS

- **Crop determinístico no servidor, não no modelo** (aplicado já na Fase 2/3 juntas, ver
  Phase_3.md) — decisão tomada cedo por causa do padrão já visto no `matrix_rectifier.ts` do
  próprio mantosfc (precisou de heurística à parte porque modelos de IA não são espacialmente
  precisos).
- **Heurística de "imagem de baixa qualidade" não foi implementada** — adiada pra Fase 5 por
  falta de critério objetivo testável sem uma OpenAI real na mão; ver Phase_5.md.
- **`npm install` quebra silenciosamente em diretório errado** — rodar comandos de pacote
  sempre com `pwd` explícito antes; um `cd` anterior numa outra tarefa deixou o shell na raiz
  do mantosfc por engano e o primeiro `npm install` tentou rodar lá (sem `package.json`).

## COVERAGE

REQUIREMENTS → backend (detect controller/service/credentials) + addin (Interop export,
Core Detect, Bridge `detect`, HTML 4.2-4.4). Base para Fase 3 (mesma imagem exportada é
reusada na extração, cache em `_lastExportedImageBytes`).
