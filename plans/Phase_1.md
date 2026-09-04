# Fase 1 — Docker no Corel + login real + sessão persistida + crédito real

- **STATUS:** [x] Implementada nesta sessão (2026-09-03) — `dotnet build MantosExtract.sln -c
  Release` (7 projetos: Core, Interop, Windows, AddIn, Resources, Installer, Core.Tests) 0
  erros/0 avisos; `dotnet test` **144/144 verdes** (Auth: parsing de login/me, expiração de
  sessão, cliente HTTP contra handler fake, orquestrador de tela; I18n: cobertura PT/ES/EN,
  escaping do script injetado, chaves do HTML batendo com o catálogo). Runtime COM/WebView2
  real dentro do CorelDRAW é validação MANUAL pendente na VM (ver seção VALIDATION).
- **OBJETIVO (1 frase):** Um addin C# que carrega no CorelDRAW, mostra a tela de login,
  autentica de verdade contra `POST /api/v1/auth/login` do mantosfc, guarda a sessão
  (DPAPI) e mostra o saldo de crédito real — sem pedir login de novo dentro da validade do
  token.
- **OBJETIVO-DE-NEGÓCIO SERVIDO:** Fundação de tudo (habilita Fases 2-5); prova que o shim
  carrega no Corel (mesmo requisito que RF16 do SisCut) e que a integração com o mantosfc
  existente funciona sem precisar de nenhum endpoint novo.

## REQUIREMENTS (EARS)

- **WHEN** o operador abre o CorelDRAW 2024/2025/2026, **THEN** o sistema SHALL registrar e
  exibir o docker do Mantos Extract sem erro de carga.
- **WHEN** não há sessão local válida, **THEN** o sistema SHALL mostrar a tela de login
  (email/senha) e SHALL NOT mostrar nenhuma outra tela.
- **WHEN** o operador envia email/senha, **THEN** o sistema SHALL chamar
  `POST /api/v1/auth/login` do mantosfc com TLS 1.2 forçado e SHALL tratar `role` fora de
  `tenant`/`sub_tenant` (ex.: `admin`, `designer`) como login inválido pra este produto.
- **WHEN** o login retorna 2xx com `role` válida, **THEN** o sistema SHALL persistir
  `sessionId`, `expiresAt`, `role` e o e-mail via DPAPI (`ProtectedData`,
  `CurrentUser` scope) e SHALL navegar pra a tela principal mostrando `creditsRemaining`.
- **WHEN** o docker carrega e existe uma sessão persistida com `expiresAt` no futuro,
  **THEN** o sistema SHALL pular a tela de login e SHALL chamar `GET /api/v1/creator/me`
  pra atualizar o saldo de crédito exibido.
- **WHEN** `GET /api/v1/creator/me` responde 401 `E_UNAUTHORIZED` (sessão expirada/revogada
  no servidor), **THEN** o sistema SHALL limpar a sessão local e SHALL voltar pra tela de
  login.
- **WHEN** o login falha por rede indisponível, **THEN** o sistema SHALL mostrar mensagem
  pt-BR genérica de conectividade (não vazar exceção técnica na tela).
- **WHEN** o login falha por IP/horário não permitido (`E_IP_DENIED`/`E_TIME_DENIED`),
  **THEN** o sistema SHALL mostrar mensagem específica distinta da de rede — achado real no
  código (`TenantAuthMiddleware`), não estava no spec original.
- **WHEN** o operador clica "sair" na tela de Configurações, **THEN** o sistema SHALL
  chamar `POST /api/v1/auth/logout`, apagar a sessão local, e voltar pro login.

## DESIGN (curto)

Solução com 6 projetos desde o dia 1 (copiando a forma do `Optimus.sln`, instrução 0.1 do
spec): `MantosExtract.Core` (netstandard2.0, pura), `MantosExtract.Interop` (net48, COM —
esqueleto mínimo nesta fase, conteúdo real na Fase 2/3), `MantosExtract.Windows` (net48,
DPAPI + prefs de idioma), `MantosExtract.AddIn` (net48, docker/bridge/UI),
`MantosExtract.Resources` (ícone — placeholder nesta fase, ver DO NOT WANT),
`installer/MantosExtract.Installer` (esqueleto vazio, conteúdo real na Fase 5),
`tests/MantosExtract.Core.Tests` (net8.0, xUnit).

Fluxo runtime: `MantosExtractDocker` (mirror de `OptimusDocker.cs`) inicia o WebView2,
injeta o catálogo i18n, navega pra `wwwroot/index.html`. `MantosExtractBridge` (mirror de
`OptimusBridge.cs`, `WebMessageAsJson`) recebe comandos `status`/`login`/`logout`/
`saveOpenAiKey`/`idioma` da página e responde via `ExecuteScriptAsync`. A UI não sabe nada
de DPAPI nem de HTTP — só manda comando e recebe evento, igual os irmãos.

`MantosfcAuthClient` (Core, pura, `HttpClient` injetado) fala com o mantosfc real. Vive em
`Auth/`. `SecureCredentialStore` (Windows, DPAPI real) implementa `ICredentialStore`
(interface em Core) — testável com um fake em memória nos testes, real só no addin.

## TARGET FILES (weight > 0.7)

- `MantosExtract.sln`
- `src/MantosExtract.Core/Auth/{LoginResult.cs, MeResult.cs, SessionState.cs,
  IMantosfcAuthClient.cs, MantosfcAuthClient.cs, ICredentialStore.cs, AuthOrchestrator.cs}`
- `src/MantosExtract.Core/I18n/{LocalizationService.cs, I18nScript.cs, LocalizedStrings.cs,
  Strings.Pt.cs, Strings.Es.cs, Strings.En.cs}`
- `src/MantosExtract.Windows/{SecureCredentialStore.cs, LanguageStore.cs}`
- `src/MantosExtract.AddIn/{Build.cs, MantosExtract.AddIn.csproj}`
- `src/MantosExtract.AddIn/Ui/{MantosExtractDocker.cs, MantosExtractBridge.cs,
  MantosExtractLog.cs}`
- `src/MantosExtract.AddIn/wwwroot/index.html`
- `src/MantosExtract.AddIn/addon/{AppUI.xslt, UserUI.xslt, config.xml, Coreldrw.addon}`
- `src/MantosExtract.Interop/{ICorelHost.cs, CorelHost.cs}` (esqueleto)
- `tests/MantosExtract.Core.Tests/Auth/*`, `tests/MantosExtract.Core.Tests/I18n/*`

## EXEMPLARS TO MIRROR

- `optimus/src/Optimus.AddIn/Ui/OptimusDocker.cs` — bootstrap do docker inteiro (AssemblyResolve
  estático, backstop `ProcessExit`, `Dispatcher.Invoke` real, `ResolveApp` com fallback ROT).
- `optimus/src/Optimus.AddIn/Ui/OptimusBridge.cs` — `WebMessageAsJson`, `switch(cmd)`,
  `ExecuteScriptAsync` fire-and-forget.
- `optimus/src/Optimus.Core/I18n/{LocalizationService.cs, I18nScript.cs}` — i18n injetado.
- `optimus/src/Optimus.Windows/LanguageStore.cs` — prefs simples em `%LOCALAPPDATA%`.
- `optimus/src/Optimus.AddIn/addon/{AppUI.xslt, UserUI.xslt, config.xml}` — payload do addon,
  GUIDs trocados pelos 3 novos deste projeto.
- `siscut/plugin/src/SisCut.Security/LicenseClient.cs` — molde de robustez HTTP (TLS 1.2
  forçado, timeout, exceção tipada, mensagem pt-BR de RB2/sem-internet).
- `mantosfc/backend/app/controllers/v1/creator_auth_controller.ts` — payload real do login
  (`sessionId, expiresAt, role, user{creditsRemaining,...}`).
- `mantosfc/backend/app/services/session_service.ts` — TTL 24h, sem refresh (confirma 7.7).

## READY-MADE SOLUTIONS TO USE

- `System.Net.Http.HttpClient` + `System.Text.Json` (cliente do mantosfc).
- `System.Security.Cryptography.ProtectedData` (DPAPI, `net48`) — `CurrentUser` scope, sem
  `optionalEntropy` extra (o cofre já é por-usuário-por-máquina, que é a garantia que
  precisamos: a sessão não deve migrar sozinha pra outra máquina).
- `Microsoft.Web.WebView2` (mesma versão pinada do Optimus: `1.0.2792.45`).
- `Microsoft.NETFramework.ReferenceAssemblies` (compila net48 sem precisar do .NET Framework
  instalado, mesmo truque do Optimus/SisCut).

## TASKS

1. [x] **Test-first:** `LoginResultParsingTests` — parse do JSON real de
   `POST /api/v1/auth/login` (`sessionId`, `expiresAt` ISO, `role`, `user.creditsRemaining`),
   incluindo os 4 valores possíveis de `role` (`admin`/`designer`/`tenant`/`sub_tenant`) e a
   regra "só tenant/sub_tenant é válido pra este produto".
2. [x] `LoginResult`/`MeResult`/`SessionState` (DTOs `System.Text.Json`, sem lógica).
3. [x] **Test-first:** `SessionStateExpiryTests` — `IsExpired` por `expiresAt` vs. um relógio
   injetado (nunca `DateTime.Now` direto, testável).
4. [x] **Test-first:** `MantosfcAuthClientTests` — via `HttpMessageHandler` fake: sucesso,
   401 sessão inválida, 403 `E_IP_DENIED`, 403 `E_TIME_DENIED`, timeout/sem-rede,
   `role` fora do permitido.
5. [x] `MantosfcAuthClient` (Core): `LoginAsync`, `MeAsync`, `LogoutAsync`. TLS 1.2 forçado
   (mesma nota do `LicenseClient.cs` — AppDomain do Corel fica em TLS 1.0 por padrão).
   Mensagens de erro em pt-BR já na camada Core (a UI só exibe).
6. [x] `ICredentialStore` (Core) + `SecureCredentialStore` (Windows, DPAPI real) — guarda
   `{sessionId, expiresAt, role, email}` como um blob JSON criptografado em
   `%LOCALAPPDATA%\MantosExtract\session.bin`. Nunca guarda senha.
7. [x] `AuthOrchestrator` (Core) — decide, dado o estado local + resposta do servidor, qual
   tela mostrar (`login` | `home`) e qual mensagem de erro — pura, testável, é o que a
   bridge chama.
8. [x] Reimplementar `LocalizationService`/`I18nScript`/`LocalizedStrings` (cópia adaptada
   do Optimus, `window.mantosExtractReceive`/`MANTOSEXTRACT_*` em vez de `OPTIMUS_*`) com as
   chaves da tela de login + Configurações + saldo de crédito, PT/ES/EN completas desde já.
9. [x] `LanguageStore` (Windows) — mirror do Optimus, pasta `%LOCALAPPDATA%\MantosExtract`.
10. [x] `MantosExtractLog`/`Build.cs`/`MantosExtractDocker`/`MantosExtractBridge` — mirror
    fiel do Optimus (AssemblyResolve estático, backstop ProcessExit, Dispatcher.Invoke real,
    i18n injetado antes do script da página).
11. [x] `wwwroot/index.html` — tela de login (mockup 4.1), tela principal mínima só com
    saldo de crédito + botão Configurações (Fase 2 adiciona o resto), tela de
    Configurações (idioma + chave OpenAI BYOK + sair) — nova, não estava nos mockups do
    spec, ver CHANGELOG V2.
12. [x] Payload do addon: `Coreldrw.addon` (vazio), `config.xml`, `AppUI.xslt`/`UserUI.xslt`
    com os 3 GUIDs novos (`ce01d45e-…`/`30dbb917-…`/`3bebb2f8-…`), paleta própria (grafite +
    acento âmbar/industrial, seção 5 do spec — nunca indigo/verde/laranja dos irmãos).
13. [x] `ICorelHost`/`CorelHost` (Interop) — esqueleto mínimo (`DocumentName`,
    `SelectionShapeCount`), suficiente pra existir a costura; conteúdo real de seleção só na
    Fase 2.

## DO NOT WANT

- Nenhuma chamada a `/mantos-extract/detect` ou `/extract` (não existem ainda — Fase 2/3).
- Nenhuma leitura de seleção do Corel além do esqueleto (`ICorelHost` existe, mas a tela
  principal desta fase não usa seleção pra nada).
- Nenhum ícone/asset gráfico definitivo — `MantosExtract.Resources` fica com um placeholder
  claramente marcado (ver VALIDATION), não um ícone final não-aprovado pelo Davidson.
- Nenhum instalador de verdade (single-EXE) — Fase 5. Nesta fase, deploy pra VM é manual
  (copiar `bin/` pra `Addons\MantosExtract\`), documentado no fim deste arquivo.
- Nada de upscale, import, extração — todas as fases seguintes.

## VALIDATION

- **FEITO (local, Windows, dotnet 8.0.422):** `dotnet build MantosExtract.sln -c Release` →
  0 erros/0 avisos nos 7 projetos, incluindo os net48 (Interop/Windows/AddIn/Resources/
  Installer compilam sem o .NET Framework instalado, via
  `Microsoft.NETFramework.ReferenceAssemblies` — mesmo truque dos irmãos). `dotnet test
  MantosExtract.sln` → **144/144 verdes** em 35 ms (só `MantosExtract.Core.Tests`,
  netstandard2.0/net8.0, sem COM/WebView2 — essa camada nunca precisa da VM pra rodar; o
  runtime COM/WebView2 real só se prova lá).
- **PENDENTE — ícone/asset:** `MantosExtract.Resources` embute um placeholder (glifo
  geométrico simples, sem depender de arte do Davidson) só pra o botão não ficar em branco;
  trocar pelo mark oficial assim que existir.
- **MANUAL na VM (pendente, COM/WebView2 real):**
  1. Compilar a solução completa no Visual Studio da VM (com o CorelDRAW instalado).
  2. Copiar a saída de `MantosExtract.AddIn` (+ deps) pra
     `<Corel>\Programs64\Addons\MantosExtract\`, junto do payload de `addon/`.
  3. Abrir o CorelDRAW segurando F8 (força rebuild do workspace, lição documentada nos XSLT
     dos irmãos); confirmar que o botão aparece no menu Ferramentas + toolbar Standard.
  4. Abrir o docker; confirmar que a tela de login aparece (nenhuma sessão local ainda).
  5. Logar com uma conta de creator/tenant real do mantosfc; confirmar que o saldo de
     crédito mostrado bate com o painel web do mantosfc.
  6. Fechar e reabrir o CorelDRAW; confirmar que NÃO pede login de novo (sessão persistida).
  7. Esperar a sessão expirar (ou revogar manualmente no admin do mantosfc) e confirmar que
     a próxima ação volta pro login com mensagem clara, não uma tela travada/quebrada.
  8. Testar com uma conta `admin` ou `designer` (se existir uma de teste) — confirmar que o
     produto recusa com mensagem clara, não deixa entrar como se fosse tenant.

## VERIFICATION GATE

- [x] `dotnet test MantosExtract.sln` 100% verde na camada pura antes de qualquer commit —
  144/144, 0 falhas.
- [ ] Os 8 passos de VALIDATION → VM confirmados manualmente (pendente, precisa da VM com
  CorelDRAW + uma conta de teste no mantosfc).

## TECHNICAL CONSTRAINTS

- Sessão nunca em texto claro — só via `ProtectedData`/DPAPI, `CurrentUser` scope.
- Senha NUNCA persistida, nem em memória além do tempo da chamada de login.
- TLS 1.2 forçado (`ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12`)
  antes de qualquer `HttpClient` do addin — lição já paga no SisCut.
- Arquivos < 500 linhas.

## LESSONS

*(preenchido após a validação manual na VM — nesta sessão só a camada pura foi exercida de
verdade; runtime COM/WebView2 ainda não tem uma lição real pra registrar)*

## COVERAGE

REQUIREMENTS → tarefas 1-13. Fundação para Fases 2-5 (docker, bridge, i18n, DPAPI e cliente
HTTP do mantosfc são reusados sem mudança nas fases seguintes).
