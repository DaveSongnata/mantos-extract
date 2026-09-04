# Fase 5 — Estados de erro, créditos esgotados, retomada, i18n completo, instalador

- **STATUS:** [x] Maior parte implementada (2026-09-03). Instalador single-EXE **[ ]
  continua placeholder** (precisa de ícone aprovado pelo Davidson + wizard — ver "O QUE
  FALTA"). Resto testado via build/unit test; **nada validado numa VM real**.
- **OBJETIVO (1 frase):** Poli o produto pros edge cases reais do público (confecção,
  internet instável, PC sem GPU) e entrega um caminho de deploy real.
- **OBJETIVO-DE-NEGÓCIO SERVIDO:** É a diferença entre "funciona na demo" e "funciona no
  chão de fábrica" — o público não tem paciência nem vocabulário técnico pra contornar erro.

## REQUIREMENTS (EARS) — implementados

- **WHEN** qualquer chamada ao mantosfc falha por rede, **THEN** o sistema SHALL mostrar a
  tela 4.7 (erro de conexão) com "ver detalhes técnicos" colapsado por padrão. — feito
  (`<details class="tech">`).
- **WHEN** o crédito chega a zero, **THEN** o sistema SHALL mostrar a tela 4.8. — feito,
  **mas sem link real de WhatsApp** (ver "O QUE FALTA" — número/fluxo não fornecido, e o
  spec já avisava pra não inventar).
- **WHEN** um lote é interrompido (falha em elementos individuais), **THEN** o sistema SHALL
  permitir retomar só os elementos que falharam. — feito: tela de resultado guarda
  `lastFailedIds` e "Tentar de novo" reenvia só esses IDs pro Bridge, que resolve de novo
  contra `_lastDetectedElements` (cache do lado C#, nunca confia em bbox vindo do JS).
- **WHEN** a imagem selecionada é de baixa qualidade, **THEN** o sistema SHALL avisar antes
  de gastar crédito. — **NÃO implementado**, ver "O QUE FALTA".

## TARGET FILES (implementados)

- `mantos-extract/src/MantosExtract.AddIn/wwwroot/index.html` (telas 4.7/4.8 completas,
  "tentar de novo" seletivo)
- `mantos-extract/src/MantosExtract.Core/I18n/Strings.{Pt,Es,En}.cs` — cobertura 100%
  PT/ES/EN pras ~90 chaves de Fases 1-5 (`LocalizedStringsCoverageTests` garante: toda chave
  pt tem par explícito em es/en, nenhuma vazia)
- `mantos-extract/tests/.../I18n/IndexHtmlKeysTests.cs` — todo `data-i18n`/`-title`/
  `-placeholder` do HTML resolve contra o catálogo (pega chave digitada errado sem precisar
  do gate de JS)
- `mantos-extract/scripts/check-ui-js.js` — mirror do gate do Optimus (sintaxe + toda função
  chamada/handler resolve), **rodado de verdade** contra o `index.html` real, passou
- `mantos-extract/scripts/deploy-dev.ps1` — build completo + gate de JS + payload +
  cópia pra Addons de toda instalação de CorelDRAW encontrada, **rodado de verdade** neste
  ambiente (sem CorelDRAW aqui, então para com aviso claro em vez de fingir sucesso)

## O QUE FALTA (decisões/ativos que só o Davi ou o Davidson resolvem)

1. **Número/fluxo real de WhatsApp da tela 4.8** — o spec já pedia explicitamente pra não
   inventar. Hoje a tela mostra só texto ("Peça ao responsável... renovar no painel do
   mantosfc"), sem botão de deep-link. Trocar por um botão real quando o número existir.
2. **Ícone da marca Mantos Extract** — `MantosExtract.Resources` é um DLL vazio (sem
   `Win32Resource`), o botão do Corel renderiza sem ícone até existir uma arte aprovada.
3. **Instalador single-EXE polido** — hoje `installer/MantosExtract.Installer.csproj` é um
   stub (`Console.WriteLine`, sem WinForms/ILRepack). `scripts/deploy-dev.ps1` já cobre o
   deploy funcional pra VM/teste; o wizard bonito pro cliente final depende do ícone acima
   também, então foi adiado até ter os dois.
4. **Heurística de "imagem de baixa qualidade"** — precisa de critério objetivo (resolução
   mínima? blur detection?) e idealmente de testes com fotos reais de WhatsApp recomprimidas,
   que não tenho aqui. Ponto de partida sugerido: checar resolução mínima da foto exportada
   (`sharp().metadata()` já é usado no backend; dá pra expor `width`/`height` na resposta do
   `/detect` e o C# decide o aviso) — não implementado, é só a ideia mais barata.

## DO NOT WANT (respeitado)

- Nenhuma feature nova de produto além do polimento das Fases 1-4.

## VALIDATION

- **FEITO:** `dotnet build`/`dotnet test` (276/276) cobrindo todo o Core; `node
  scripts/check-ui-js.js` ok; `scripts/deploy-dev.ps1` roda de ponta a ponta (build + gate +
  payload de 22 arquivos), testado nesta sessão.
- **PENDENTE — VM real:** todo o roteiro de erro (derrubar a rede no meio de um lote,
  esgotar crédito de propósito, forçar timeout de upscale) só se prova com CorelDRAW e
  mantosfc de verdade rodando ao mesmo tempo — nenhum dos dois está disponível aqui.

## LESSONS

- **Arquivo `.ps1` com em-dash/acentos sem BOM quebra o parser do Windows PowerShell 5.1**
  com um erro de sintaxe que aponta pro lugar ERRADO (parece um parêntese não fechado numa
  linha de comentário, longe de onde o problema realmente está). Fix: salvar com
  `Set-Content -Encoding utf8` (que no PowerShell 5.1 adiciona BOM) em vez de deixar o
  encoding implícito. Vale pra qualquer `.ps1` novo deste repo com texto em português.
- **Diretiva de permissão (`icacls`) no ARQUIVO não basta se a PASTA que o contém continua em
  High Mandatory Level** — criar o `.tmp` do save atômico é uma escrita NA PASTA. Ver
  CHANGELOG.md pra o caso completo (mantosfc inteiro travado por TrustedInstaller).

## COVERAGE

REQUIREMENTS → HTML (4.7/4.8/retomada) + i18n completo + os dois scripts de build/deploy.
Pendências reais listadas em "O QUE FALTA" são de produto/ativo, não de código.
