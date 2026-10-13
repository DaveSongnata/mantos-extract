# Automação do CorelDRAW nesta máquina (agentes e testes E2E do Mantos Extract)

Portado de `siscut/docs/COREL-AUTOMACAO.md` + `siscut/tools/corel-com/` (que vieram do ai-sten) em
2026-10-13 e adaptado ao Mantos Extract. A máquina de desenvolvimento tem o **CorelDRAW 2024**
(numeração interna **25**: `C:\Program Files\Corel\CorelDRAW Graphics Suite\25\Programs64\CorelDRW.exe`),
com o Mantos Extract em `...\Programs64\Addons\MantosExtract\`.

Com isso um agente consegue, sozinho e localmente:
- **gerar o instalador** (`scripts\build-all.ps1`) e **instalá-lo em modo silencioso** no Corel daqui
  (`Deploy-MantosExtract.ps1`), conferindo que a versão nova carregou;
- conectar ao Corel por COM, criar documento, importar uma foto e deixá-la selecionada;
- **dirigir a tela do Mantos Extract dentro do Corel** (DevTools do docker, porta **9479**) — detectar,
  marcar elementos, extrair — contra o mantosfc de produção;
- medir o resultado e tirar print da janela e da página.

## Ferramentas (`tools/corel-com/`)
| Arquivo | Para quê |
|---|---|
| `Connect-Corel.ps1` | `Connect-Corel [-AllowStart]`, `New-CorelTestDocument` (já salvo em pasta de teste), `Import-CorelBitmap` (importa e deixa selecionado), `Get-CorelShapesDeep`, `Close-CorelTestDocuments`, `Save-CorelScreenshot`, `Get-MantosDockerLogPath`, `Get-MantosExtractionsRoot`, constantes (`$CdrMillimeter`=3, `$CdrFilterPng`=802) |
| `Deploy-MantosExtract.ps1` | `[-Build]` → fecha o Corel (cópia de segurança de documento sem nome/alterado em `%LOCALAPPDATA%\Temp\MantosExtract.Deploy\salvos`) → **instala o `MantosExtract_Setup.exe` da versão com `/silent`** (pede UAC) → reabre → abre o docker → confere `=== MantosExtract docker vX ===` no log. `-DevTools` liga a porta 9479. `-CopyPayload` copia `dist\payload` em vez de rodar o instalador (sem UAC depois de UM `-GrantAccess`) |
| `cdp.mjs` | DevTools do docker: `targets`, `eval "<js>"`, `wait "<js>" [s]`, `shot <png>` |
| `StraightAngleMetrics.ps1` | `New-SkewedBadgeFixture` (foto sintética com escudo inclinado ~15°), `Measure-ExtractedPiece` (cobertura, quanto a peça preenche a própria caixa, ângulo do eixo principal), `New-ComparisonSheet` |
| `Test-StraightAngleE2E.ps1` | E2E da caixinha "Ângulo reto" na extração (CA-01..CA-07 da especificação de 2026-10-13) |
| `MantosPage.ps1` | helpers comuns do DevTools: `Initialize-MantosPage` (engancha as mensagens e volta à tela inicial), `Invoke-MantosDetect`, `Select-MantosElements`, `Invoke-MantosExtract` |
| `Invoke-PhotoExtraction.ps1` | **reproduzir reclamação de cliente:** importa a foto dele, detecta, extrai todos (ou `-ElementMatch`) + `-Background`, e monta `prancha.png` com tamanho e orientação de cada peça; guarda `deteccao.json` |
| `Invoke-ServerExtraction.ps1` | **testar mudança de servidor sem reinstalar o addin:** usa o `ExtractionClient` de `dist\payload`, a sessão/chave do addin, a foto que o Corel exportou (`%TEMP%\MantosExtract\work\selecao_*.png`) e o `deteccao.json` |

## Instalador silencioso
`MantosExtract_Setup.exe /silent` roda o MESMO `InstallerEngine` do assistente, sem janela e sem
WebView2. Como o instalador é `requireAdministrator`, cada execução pede UMA elevação (UAC). Resultado:
exit code `0` instalado, `2` instalado com arquivos que falharam, `1` não instalado (Corel aberto, Corel
ausente, sem espaço…), e o motivo em `%TEMP%\MantosExtract_Installer\install-silent.log`.

## Como dirigir a tela do Mantos Extract
Só com `%LOCALAPPDATA%\MantosExtract\dev-tools.on` antes de abrir o Corel (`Deploy-MantosExtract.ps1
-DevTools`; código em `src/MantosExtract.AddIn/Ui/MantosExtractDevTools.cs`). PC de cliente nunca tem o
arquivo.
- O script da página fica numa IIFE: `send`/`$`/`state` **não** são globais. Mande comandos com
  `window.chrome.webview.postMessage({cmd:'...'})` e dirija a tela por DOM
  (`document.getElementById('detectBtn').click()`).
- Respostas do C# chegam em `window.mantosExtractReceive(msg)` (global): envolva-a e guarde as mensagens
  (`type:'detect'|'extractProgress'|'extract'|'auth'…`). Fim de extração = `{type:'extract', done:true}`.
- O botão "Detectar" lê o bitmap SELECIONADO no Corel: importe com `Import-CorelBitmap` e mande
  `{cmd:'status'}` para a tela habilitar o botão.
- **Aspas**: o PowerShell 5.1 estraga aspas duplas em argumentos de programas nativos (`node`) — use só
  aspas SIMPLES no JS; dados complexos em base64.
- **Encoding**: `.ps1` com acento tem que estar em UTF-8 **com BOM** — sem ele o PowerShell 5.1 lê como
  ANSI e um `Select-String 'ângulo'` deixa de casar.

## Pré-requisitos que não são do agente
- **Login do operador**: a sessão (`%LOCALAPPDATA%\MantosExtract\session.bin`, DPAPI, 30 dias) precisa
  existir; se o docker abrir na tela de login, alguém entra uma vez.
- **Chave OpenAI** (BYOK): salva nas Configurações do docker (`openai.bin`, DPAPI) ou passada ao E2E por
  `-OpenAiKeyFile <arquivo>` (o script a salva pelo próprio add-in, sem logar). O E2E gasta 1 detecção +
  2 extrações por execução nessa chave.
- **mantosfc**: o E2E bate na produção; o recurso testado precisa estar deployado (`/dmfc`).

## Regras
1. Constantes COM só de `src/MantosExtract.Interop/CorelConstants.cs` (e do dump da typelib) — nunca chutar.
2. **Sempre leia de volta** o que criou/alterou (o E2E mede o PNG do lote em
   `%LOCALAPPDATA%\MantosExtract\Extractions\<lote>\`).
3. **O Dave pode estar testando junto**: o deploy salva uma cópia de todo documento sem nome/alterado
   antes de fechar o Corel; testes só fecham documentos das pastas `%LOCALAPPDATA%\Temp\MantosExtract.E2E`
   / `MantosExtract.CorelTests`.
4. Log do add-in: `%TEMP%\MantosExtract\docker.log` (TEMP do USUÁRIO). Log do servidor:
   `ssh mfc "docker logs mantoscontrol-api-1 --since <hora>"` (`-ServerLogs` no E2E).
