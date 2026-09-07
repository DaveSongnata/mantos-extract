# Mantos Extract — Plano de Implementação (índice)

> **Fonte autoritativa do QUÊ/PORQUÊ:** `../docs/mantos-extract-spec.md` (paste original do
> Dave). Em qualquer dúvida, a seção 2 (escopo) e a seção 5 (direção de design) são o critério
> de desempate de produto; `../CLAUDE.md` é o critério de desempate de arquitetura.
> Decisões de produto que exigiram confirmação do Dave, e decisões técnicas resolvidas sem
> parar o trabalho, estão em `../CHANGELOG.md` — leia antes de reabrir qualquer debate já
> fechado lá.

---

## Objetivo global

Addin de CorelDRAW que extrai elementos visuais (logo/estampa/texto) de uma foto real de
roupa via IA (OpenAI, server-side no mantosfc), com upscale local 2× via Real-ESRGAN, e
insere o resultado (PNG com fundo transparente) no canvas — pronto pro operador montar em
cima do molde no SISBOLT. Vendido dentro de um pacote de R$24.000 já fechado com o Davidson;
público final é o operador de confecção, não um designer treinado.

## CONVENTIONS MAP (herdado dos irmãos + descoberto nesta sessão)

1. **Duas camadas, como SisCut/Optimus/AiSten:** lógica pura em `netstandard2.0`
   (`MantosExtract.Core`), COM/Win32 em `net48` (`MantosExtract.Interop`,
   `MantosExtract.Windows`, `MantosExtract.AddIn`). Testes xUnit em `net8.0`. TDD.
2. **Ponte WebView2, versão Optimus (não a versão antiga do SisCut):** JS→C# via
   `e.WebMessageAsJson`; C#→JS via `ExecuteScriptAsync("window.mantosExtractReceive(...)")`,
   fire-and-forget, nunca `PostWebMessageAsJson`/canal `message`.
3. **Duas superfícies de auth, nunca confundidas:** sessão Bearer do mantosfc (login
   email/senha, `SessionService`, TTL 24h sem refresh) vs. chave OpenAI BYOK por tenant
   (header `X-OpenAI-Api-Key`, guardada localmente). Nenhuma das duas é o
   HWID+Ed25519 do `LicenseClient.cs` do SisCut (aquilo é licença de desktop do OUTRO addin).
4. **Créditos:** debitados pelo `credit_check_middleware.ts` já existente no mantosfc, em
   QUALQUER 2xx do grupo `/api/v1` `[creditCheck, activeSubscription]` — decisão do Dave
   (2026-09-03): detecção TAMBÉM debita, não só extração confirmada. Ver M2 no CLAUDE.md.
5. **Import ainda não existe em nenhum addin irmão** — `IVGLayer.Import` confirmado na
   typelib, mas o comportamento real (seleção pós-import, `StructImportOptions`) é
   território novo, validado só na VM (Fase 3).
6. **Upscale é processo externo** (`realesrgan-ncnn-vulkan.exe`), invocado no padrão do
   `EngineRunner.cs` do SisCut (arquivo → `Process` com timeout → exit code → arquivo de
   saída) — não a Ponte de Ação (que é o protocolo inverso, peer externo → SisCut). Ver
   CHANGELOG pra o porquê da correção.
7. **Nunca chutar constante do Corel** — fonte: `../optimus/docs/vgcore-tlb-dump.txt`.
   `cdrMillimeter = 3`.
8. **Mensagens ao usuário pt-BR** (spec, Language Convention); i18n PT/ES/EN por chave,
   HTML sem texto próprio (`data-i18n`), catálogo injetado via
   `AddScriptToExecuteOnDocumentCreatedAsync` — padrão `LocalizationService`/`I18nScript`
   do Optimus, reimplementado aqui (cada repo é dono da própria cópia, sem pacote
   compartilhado entre os irmãos).
9. **Arquivos < 500 linhas.** Nada de SVG/DXF/fitting de molde (fora de escopo, seção 2).
10. **Log:** `%TEMP%\MantosExtract\docker.log`, nunca deixar exceção derrubar o Corel —
    `AssemblyResolve` da pasta do addon, `UnhandledException`/`UnobservedTaskException`
    tratados, todo entry point em try/catch.

---

## Reúso vs. construção

| Componente | Decisão |
|---|---|
| Estrutura da solução (`.sln`, TFMs, addon payload/XSLT/GUIDs) | **COPIA a forma** do `Optimus.sln` (instrução 0.1 do spec) — conteúdo próprio, GUIDs novos |
| Ponte WebView2↔C# (`WebMessageAsJson`, `ExecuteScriptAsync`, backstop `ProcessExit`→Dispose, `Dispatcher.Invoke` real) | **COPIA o padrão** do `OptimusDocker.cs`/`OptimusBridge.cs` |
| i18n (`LocalizationService`, `I18nScript`, `LocalizedStrings`) | **COPIA o padrão** do Optimus, reimplementado com strings próprias |
| Persistência de prefs simples (idioma) | **COPIA o padrão** do `LanguageStore.cs` (texto plano em `%LOCALAPPDATA%`) |
| Persistência de sessão + chave OpenAI (segredo) | **CONSTRÓI NOVO** — DPAPI (`ProtectedData`), nenhum irmão tem esse padrão ainda |
| Export de bitmap (referência de como fazer COM de export) | **ESPELHA** `CorelExporter.cs` do SisCut — não precisamos exportar nesta fase, mas o import (Fase 3) usa a mesma disciplina (`Type.InvokeMember`, `Finish()`) |
| Import de bitmap pro canvas | **CONSTRÓI NOVO** — nenhum addin irmão fez isso ainda |
| Chamada HTTP ao mantosfc (login, me, detect, extract) | **CONSTRÓI NOVO**, no padrão de robustez do `LicenseClient.cs` (TLS 1.2 forçado, timeout, exceção tipada, mensagem pt-BR) mas contra endpoints/payloads diferentes |
| Invocação de processo externo (upscale) | **ESPELHA** `EngineRunner.cs` do SisCut (arquivo→Process→timeout→exit code→arquivo) |
| Backend novo (`/mantos-extract/detect`, `/extract`, `openai_credentials.ts`, `openai_vision_service.ts`) | **CONSTRÓI NOVO** dentro do mantosfc, espelhando a FORMA de `gemini_credentials.ts`/`gemini_vision_service.ts` (conteúdo é OpenAI, não Gemini) — Fase 2/3, fora do escopo desta sessão |
| Sistema de créditos, sessão, Asaas | **REUSA 100% inalterado** — mantosfc já tem tudo, só entra no mesmo grupo de rota |

---

## Verdicts de arquitetura

**V1 — Onde entram os endpoints novos no mantosfc?**
Grupo existente `routes.ts:67-78` (`.prefix('/api/v1').use([creditCheck, activeSubscription])`)
já é o único lugar credit-aware pronto. **Verdict:** os dois endpoints novos entram NESSE
grupo — decisão confirmada pelo Dave como consequência da resposta a P1 (detecção também
cobra crédito). Não criar um grupo paralelo sem crédito pra detecção.

**V2 — Onde mora a chave OpenAI do tenant no addin?**
BYOK confirmado (P2). **Verdict:** guardada localmente via DPAPI (mesmo cofre da sessão,
`SecureCredentialStore`), inserida numa tela de Configurações nova (não existia nos mockups
originais — a entrada natural é o ícone "[.]" já presente em quase toda tela do spec).
Enviada como header `X-OpenAI-Api-Key`, nunca persistida no mantosfc.

**V3 — Upscale: wrapper próprio ou binário upstream?**
**Verdict:** invocar `realesrgan-ncnn-vulkan.exe` (upstream, MIT/BSD, pré-compilado) direto,
no padrão do `EngineRunner.cs`. Ver CHANGELOG. Reabrir só se Dave pedir controle mais fino
que a CLI upstream não dá.

**V4 — Companion do upscale: Ponte de Ação ou EngineRunner?**
**Verdict:** `EngineRunner.cs` (Process síncrono com timeout). A Ponte de Ação é
peer-to-peer na direção contrária (app externo → SisCut); aqui o shim possui o processo
filho que ele mesmo lança. Ver CHANGELOG.

**V5 — Fatiamento em fases: manter a sugestão do spec (seção 8) ou reordenar?**
Lido o código real dos três repositórios, o fatiamento do spec já é são: cada fase entrega
algo rodando dentro do Corel de verdade, na ordem de dependência técnica real (docker
carrega → detecta → extrai → upscala → poli mento de erro). **Verdict:** mantido como está,
sem reordenar. Único ajuste: a Fase 1, mesmo sendo "só login", já precisa do payload do
addon completo (GUIDs, XSLT, docker bootstrap) porque é isso que faz o docker aparecer
dentro do Corel — não é trabalho adicional de uma fase futura, é a definição de "rodar de
ponta a ponta dentro do Corel de verdade" já na Fase 1.

---

## Fases

| Fase | Entrega ponta a ponta | Status |
|---|---|---|
| [Fase 1](Phase_1.md) | Docker carrega no Corel, login real contra o mantosfc, sessão persiste (DPAPI), saldo de crédito real | **Implementada** — 0 erros/0 avisos; pendente validação na VM |
| [Fase 2](Phase_2.md) | Seleção de bitmap no Corel → `/mantos-extract/detect` real (OpenAI) → overlay de caixas + checklist | **Implementada** — backend+addin, `tsc`/`eslint` limpos; pendente validação na VM + chamada OpenAI real |
| [Fase 3](Phase_3.md) | Confirmação → `/mantos-extract/extract` real → `Layer.Import` no canvas, crédito debitado | **Implementada** — pendente validação na VM + chamada OpenAI real |
| [Fase 4](Phase_4.md) | Upscale 2× via `realesrgan-ncnn-vulkan.exe` entra no pipeline extração→import | **Código implementado e testado** — binário real NÃO bundlado (passo manual, ver Phase_4.md "O QUE FALTA") |
| [Fase 5](Phase_5.md) | Estados de erro, créditos esgotados, retomada de lote, i18n completo, instalador | **Implementada por completo** (2026-09-04) — instalador single-EXE real + ícone de marca aprovado e conectado; WhatsApp da tela 4.8 sem número real (não inventado, ver Phase_5.md); pendente validação na VM |

**Resumo do que "produto completo" significa aqui:** todo o código de produto (backend nos
dois endpoints novos, addin com as 4 fases de fluxo, i18n completo, scripts de build/deploy)
está escrito, compila sem erro/aviso, e tem 276 testes unitários verdes cobrindo toda a
lógica que dá pra testar sem CorelDRAW/OpenAI reais. **O que genuinamente falta não é código
— são 4 ativos/decisões que só existem fora deste ambiente:** validação numa VM com
CorelDRAW real, uma chamada real à OpenAI, o binário do Real-ESRGAN, e o ícone da marca. Cada
um está documentado no lugar certo (CHANGELOG.md + a seção VALIDATION/"O QUE FALTA" de cada
Phase_N.md), não escondido.
