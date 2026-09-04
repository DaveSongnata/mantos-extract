# Mantos Extract — Steering Document

Addin de CorelDRAW que extrai elementos visuais (logo/estampa/texto) de uma foto real de
roupa via IA, faz upscale local, e insere o resultado no canvas — pronto pro operador montar
em cima do molde no SISBOLT (fora de escopo). Cliente: Davidson Santos (Aisten Lab
Technology), revendido a confecções. Usuário real: operador de confecção, não Davidson.
Sibling project de SisCut/Optimus/AiSten (`../siscut/`, `../optimus/`, `../ai-sten/`);
reusa a arquitetura provada deles (shim C# net48 + WebView2), mas o backend consumido é o
**mantosfc já existente** (`../mantosfc/`), nunca um serviço novo hospedado à parte.

Spec de produto completa: `docs/mantos-extract-spec.md` (paste original do Dave, íntegro).
Plano: `plans/index.md` + `plans/Phase_1..5.md`.

---

## Arquitetura Golden Rule

- **Pure logic (parsing, cálculo de crédito/upscale, sanitização) → `netstandard2.0`** —
  testável no .NET 8, sem COM.
- **I/O com CorelDRAW / Windows → `net48`** — único lugar onde `dynamic` COM, WPF/WebView2 e
  Win32 (DPAPI) vivem.
- **Tests → `net8.0` + xUnit.** Todo algoritmo nasce com teste ANTES do código (TDD).
- Os shells COM/WebView2 são deliberadamente burros: nenhuma decisão de produto, nenhum
  cálculo — só repassam.

## Language Convention

- Código, identificadores, comentários, docs internos: **inglês**.
- Mensagens ao usuário (toda a UI web do docker): **pt-BR** por padrão, com **PT/ES/EN**
  i18n (mesmo padrão do Optimus: `LocalizationService`/`LocalizedStrings`/`I18nScript`,
  injetado via `AddScriptToExecuteOnDocumentCreatedAsync`, HTML sem texto próprio,
  `data-i18n`). Conversa com o Dave: pt-BR.

## Duas superfícies de auth — nunca confundir

1. **Sessão do operador (mantosfc, Bearer):** `POST /api/v1/auth/login` (email/senha) →
   `sessionId` (UUID puro, TTL 24 h por padrão, sem refresh — `SessionService` do mantosfc,
   verificado no código real). Persistido localmente via DPAPI. Usado em TODA chamada ao
   mantosfc (`Authorization: Bearer <sessionId>`).
2. **Chave OpenAI do tenant (BYOK, decisão do Dave 2026-09-03):** cada confecção traz a
   própria chave OpenAI, igual o Gemini já funciona hoje no mantosfc (`X-Gemini-Api-Key`).
   Pro addin isso significa: o operador cola a chave uma vez na tela de Configurações, ela
   fica guardada localmente (DPAPI, mesmo cofre da sessão) e é enviada como
   `X-OpenAI-Api-Key` em toda chamada a `/mantos-extract/detect` e `/mantos-extract/extract`.
   **Nunca confundir com `LicenseClient.cs`/HWID+Ed25519 do SisCut** — aquilo é licença de
   desktop do addin SisCut, sistema propositalmente separado (fora de escopo aqui).

## Acesso ao produto — gate `accessMantosExtract` (mantosfc, decisão do Dave 2026-09-04)

O mantosfc (não este repo) decide QUEM pode usar o Mantos Extract, por plano (padrão) ou por
pessoa (override, admin em `/admin/mantos-extract` ou na ficha do usuário) —
`PermissionResolver.resolve(user).accessMantosExtract`, checado nos dois endpoints
(`requireMantosExtractAccess`) antes de qualquer outra coisa. Sem acesso = `403 E_PLAN`. **O
cliente C# não precisa de nenhum tratamento especial pra esse código** —
`DetectionClient`/`ExtractionClient.BuildError` já sobrescreve a mensagem genérica por status
com o `message` que o servidor manda no corpo, então a frase amigável do backend
("Seu plano não inclui acesso ao Mantos Extract...") já chega pronta pro operador, do mesmo
jeito que `E_NO_CREDITS`/`E_MISSING_OPENAI_KEY` já chegavam.

## Créditos (decisão do Dave 2026-09-03)

**Detecção TAMBÉM debita 1 crédito**, não só a extração confirmada — leitura literal do
endpoint de geração já existente no mantosfc (`credit_check_middleware.ts` debita 1 em
qualquer 2xx do grupo `/api/v1` com `[creditCheck, activeSubscription]`, e os dois endpoints
novos entram nesse MESMO grupo). Consequência de UI: o custo real de um lote com N elementos
confirmados é **1 (detectar) + N (extrair)**, não N. A tela de seleção (4.4 do spec) precisa
mostrar isso explicitamente ("1 crédito já usado na detecção + N para extrair"), não só
"N selecionados, N créditos" como o mockup original sugeria — ver `CHANGELOG.md`.

## IA — dois provedores, nunca confundir

- **mantosfc hoje (MantosCreator):** Gemini (`gemini-2.5-flash-image` etc.), BYOK por
  header, endpoints `/generation/*` e `/extraction/inpaint`. **Não é usado por este addin.**
- **Endpoints NOVOS deste projeto** (`/api/v1/mantos-extract/detect`,
  `/api/v1/mantos-extract/extract`, dentro do mantosfc): **OpenAI**, BYOK por header
  `X-OpenAI-Api-Key`, sem abstração de troca de provider (decisão do Dave — complexidade sem
  necessidade real hoje). Padrão de credenciais espelha `gemini_credentials.ts` →
  `openai_credentials.ts` (header primário, `OPENAI_API_KEY` só dev, 422 `E_MISSING_OPENAI_KEY`
  se ausente — nunca 401, pra não confundir com sessão expirada no front).

## Upscale — processo externo, não biblioteca embutida

Real-ESRGAN NCNN-Vulkan (binário standalone, sem Python), invocado como **processo externo**
pelo shim, no MESMO padrão do `SisCut.Engine/EngineRunner.cs` (grava arquivo de entrada →
`Process.Start` com timeout duro → mapeia exit code → lê arquivo de saída). Cai pra CPU
sozinho se não houver GPU dedicada (mais lento, nunca trava). Fator fixo 2×.

**Nota de arquitetura (decisão técnica, ver CHANGELOG):** o spec original citava a
`PONTE-DE-ACAO.md` do SisCut como precedente — mas aquele protocolo é para um app PAR externo
disparar o SisCut (request.json/response.json com claim-por-delete), direção oposta ao que
este projeto precisa (o próprio shim possui e invoca seu processo filho). O precedente real é
o `EngineRunner.cs`, não a Ponte de Ação.

## UI Rules

- WebView2 + HTML, 100% offline. Nunca CDN, nunca webfont externa.
- **JS→C#: `e.WebMessageAsJson`** (nunca `TryGetWebMessageAsString` — versão corrigida do
  Optimus, não a antiga do SisCut).
- **C#→JS: `ExecuteScriptAsync("window.mantosExtractReceive(...)")`**, fire-and-forget — o
  canal `message` do WebView2 não é confiável dentro do docker WPF do Corel.
- Backstop `AppDomain.CurrentDomain.ProcessExit` → `Dispose()` do WebView2 (lição O17 do
  Optimus — sem isso o Corel inteiro pode cair pelo finalizador do GC).
- `Dispatcher.Invoke` real no marshalling pro UI thread (nunca `a => a()`).
- Painel dockable estreito (~320-360px), rolagem vertical, ícone antes de texto.
- **Design system: Swiss Style (Estilo Tipográfico Internacional) aplicado a
  futebol/confecção** (Dave, 2026-09-03 — substituiu a direção "painel industrial escuro"
  da seção 5 do spec original, que ficou obsoleta). Referência: biblioteca de componentes
  "Swiss Style | Futebol e Confecção" que o Dave forneceu. Tokens em
  `wwwroot/index.html:root` — pensados pra serem a base do design system do PACOTE inteiro,
  não só deste addin:
  - **Grid rígido, raio de borda ZERO em qualquer elemento.** Bordas sempre pretas e duras
    (`--lw: 2px`), nunca cinza-suave/hairline.
  - **Paleta restrita:** `--paper` (papel off-white), `--ink` (quase-preto), `--red`
    (vermelho-bandeira, única cor de ação/acento), `--gray`/`--gray-light`. Nada de teal,
    âmbar ou verde — diferenciação semântica (erro/aviso/sucesso) é por FORMA/ícone, não por
    matiz nova (o sistema já é vermelho+preto+branco+cinza, ponto).
  - **Tipografia:** `Arial Black`/`Arial` — grotesca neutra, a fonte DO Estilo Suíço
    (Helvetica-lineage), não um atalho genérico. Caixa-alta em headers/labels/botões.
  - **Faixa preta = cabeçalho de seção** (`.bar`), com uma "aba" vermelha de 8px antes do
    texto — mesmo padrão do masthead das referências.
  - **Números como peça gráfica**, não só dado — badge de crédito é um chip vermelho tipo
    placar com número grande tabular, não um texto discreto.
  - **Elementos detectados (tela de seleção) são marcados por NÚMERO + padrão de traço**
    (sólido/tracejado, vermelho/preto), nunca por uma paleta arco-íris de 6 matizes —
    acessível a daltonismo e no espírito "número de camisa" do tema.
  - Listras diagonais (`.stripes`) e marcas de registro tipográfico (`.reg`, cantos "+") são
    os únicos elementos decorativos permitidos, usados com moderação (um por tela no
    máximo) — nunca gradiente, nunca sombra, nunca glassmorphism, nunca sparkle/estrela
    (clichê visual de "isso usa IA").
- Config e user-data do WebView2 sempre em `%LOCALAPPDATA%`, nunca em Program Files (init do
  WebView2 falha silencioso lá — lição paga no SisCut).
- Log: `%TEMP%\MantosExtract\docker.log`. Nada pode derrubar o host do Corel — todo entry
  point em try/catch, `AssemblyResolve` da pasta do addon (o host não adiciona ao probe path).

## CorelDRAW COM Rules

- **Nunca chutar constante do Corel.** Fonte da verdade:
  `../optimus/docs/vgcore-tlb-dump.txt` (typelib VGCore 25.2). `cdrMillimeter = 3` (o bug
  10× já aconteceu duas vezes nos irmãos — nunca uma terceira aqui).
- Toda chamada COM sensível (export/import com structs/opcionais) via `Type.InvokeMember`,
  nunca o binder `dynamic` solto — já quebrou com "Could not convert argument 0" no SisCut e
  no Optimus.
- `Layer.Import(FileName, Filter?, Options?)` confirmado na typelib (`IVGLayer`, linha ~4999
  do dump) — existe, mas o comportamento de seleção pós-import (`ActiveSelectionRange`) e o
  `StructImportOptions` exato ficam para validação na VM (Fase 3).
- COM late-bound (`dynamic`) atrás de uma interface (`ICorelHost`) → 1 binário serve Corel
  2024/25/26.
- Export de referência (padrão a espelhar, não a reescrever): `CorelExporter.cs` do SisCut.

## Build

- `dotnet build MantosExtract.sln -c Release` + `dotnet test MantosExtract.sln`.
- **`scripts/build-all.ps1`** → `dist/payload` → `installer/MantosExtract.Installer.csproj`
  (WinForms + WebView2 wizard, ILRepack funde tudo num EXE único) →
  `shared/bin/redistributables/Clientes/<versão>/MantosExtract_Setup.exe` (+ `.zip`) — mesmo
  padrão de entrega versionada do Optimus/SisCut (`../optimus/scripts/build-all.ps1`), versões
  antigas nunca são apagadas. `shared/bin/` é gitignored (arquivo binário, não é fonte — fica
  em armazenamento normal). Versão lida de `src/MantosExtract.AddIn/Build.cs` (`Build.Tag`) e
  stampada no assembly do instalador (aparece em "Aplicativos e Recursos" do Windows).
  Instalador registra desinstalação de verdade (`installer/Core/Uninstaller.cs`). Ícone de
  marca real e aprovado (`src/MantosExtract.Resources/icons/`, `gen_icons.ps1` +
  `build_res.ps1`), usado no botão do Corel, no EXE do instalador e na janela do wizard.
  `scripts/deploy-dev.ps1` continua existindo à parte, pra iteração rápida numa VM sem gerar
  instalador (copia o payload solto direto pro Addons).
- Layout da solução (ver `plans/index.md` §Reúso vs. construção): `src/MantosExtract.Core`
  (netstandard2.0), `src/MantosExtract.Interop` (net48, COM), `src/MantosExtract.Windows`
  (net48, DPAPI/prefs), `src/MantosExtract.AddIn` (net48, WebView2 + bridge + docker +
  payload do addon), `src/MantosExtract.Resources` (ícone), `installer/` (Fase 5),
  `tests/MantosExtract.Core.Tests` (net8.0, xUnit).
- Arquivos < 500 linhas.

## Locked Decisions

| ID | Regra |
|----|-------|
| M1 | Detecção NÃO é corte final — é sugestão visual; usuário confirma cada elemento antes de extrair (spec §2) |
| M2 | Detecção debita 1 crédito (Dave, 2026-09-03) — mesma leitura literal do grupo de middleware existente |
| M3 | Chave OpenAI é BYOK por tenant, igual Gemini hoje (Dave, 2026-09-03) — nunca chave única da plataforma |
| M4 | Sessão de operador (Bearer/mantosfc) e chave OpenAI (BYOK) são independentes de `LicenseClient.cs`/HWID+Ed25519 do SisCut — nunca reusar aquele fluxo pra login |
| M5 | Upscale é processo externo (Real-ESRGAN NCNN-Vulkan via IPC por arquivo, padrão `EngineRunner.cs`), nunca lib embutida no shim nem dependência de Python |
| M6 | Só bitmap/PNG com transparência — sem SVG, sem DXF, sem fitting de molde (isso é SISBOLT) |
| M7 | Sem abstração de troca de provider de IA — é OpenAI direto nos endpoints novos |
| M8 | Fator de upscale é sempre 2× fixo sobre o resultado da extração — nenhuma tabela de mm/px por tipo de peça |
