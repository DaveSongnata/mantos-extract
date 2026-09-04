# Mantos Extract — spec de bootstrap (paste original do Dave, 2026-09-03)

> Documento congelado como recebido. Decisões de produto marcadas "A CONFIRMAR" no corpo do
> texto original foram resolvidas via `AskUserQuestion` na mesma sessão que criou este
> repositório — ver `../CHANGELOG.md` para o registro completo (o que foi perguntado, o que
> o Dave respondeu, e o que isso mudou no plano). Onde o código real dos repositórios
> irmãos contradisse uma afirmação do spec, o código venceu — também registrado no
> CHANGELOG.

Addin de CorelDRAW que extrai elementos visuais de uma imagem de referência (foto real de roupa ou arte) via IA, faz upscale local, e insere o resultado direto no canvas. Cliente final: Davidson Santos (Aisten Lab Technology), que revende pra confecções. Usuário do addin: funcionário da confecção, não o Davidson.

## 0. Instruções pra você, Claude Code

Antes de escrever qualquer linha:

1. Leia o repositório **Optimus** inteiro (estrutura de pastas, .csproj/.sln, camada de bridge, build). Tecnicamente este projeto novo é o mesmo tipo de artefato que o Optimus: um shim C# net48 com WebView2 rodando dentro do docker do CorelDRAW. Não trate o Optimus como "referência de inspiração", trate como o scaffold literal de onde este projeto parte. Copie a estrutura de solução dele como ponto de partida.
2. Leia também o **Siscut** (principalmente `CorelExporter.cs` e o padrão de polling de seleção) e o **mantosfc** (principalmente `gemini_vision_service.ts`, `credit_check_middleware.ts` e as rotas de auth), já que o backend consumido é o mantosfc existente, não um novo.
3. Depois de ler os três, escreva seu próprio plano de implementação em fatias verticais (cada fase entrega algo rodando ponta a ponta dentro do Corel de verdade, nunca "fase 1 = todo o backend, fase 2 = toda a UI"). A seção 8 já sugere o fatiamento, mas questione se fizer sentido diferente depois de ler o código real.
4. Se algo aqui contradizer o que você encontrar no código real dos três repositórios, o código real vence. Me avise a diferença antes de prosseguir.
5. Pergunte antes de decidir qualquer coisa marcada como "A CONFIRMAR" abaixo. Não pergunte sobre nada marcado "DECIDIDO".
6. **Correção de arquitetura (segunda rodada)**: nem `/generation/elements` do mantosfc, nem um serviço novo hospedado fora do mantosfc. Decisão final do Dave: endpoints NOVOS dentro do próprio mantosfc, dedicados a este projeto (detecção e extração via OpenAI, chave só no servidor). O mantosfc já é infraestrutura hospedada de verdade, não é homelab pessoal, e é onde login, crédito, detecção e extração ficam, todos server-side. O upscale continua local na máquina do cliente, mas como uma mini aplicação companheira do shim (mesmo padrão do Siscut: shim invoca um executável compilado separado via IPC por arquivo, ver `siscut/docs/PONTE-DE-ACAO.md`), não embutida direto na lógica do shim. Qualquer menção anterior a "Mantos Extract API separada, hospedada na Ouroboros" está descartada, não existe mais.
7. Daqui pra frente: decisão técnica (biblioteca, estrutura interna, formato de dado) você resolve sozinho e registra num CHANGELOG.md com o porquê, sem parar o trabalho pra confirmar. Só pare de verdade pra perguntar quando for decisão de produto que só o Dave ou o Davidson conseguem responder. Se acumular mais de uma pergunta desse tipo, guarde todas e mande batched no fim de cada fase da seção 8, não uma por vez no meio da fase.

## 1. Visão geral do produto

O cliente da confecção tem uma foto (de celular, de internet, de referência de um cliente dele) de uma peça de roupa com estampa. Ele precisa dos elementos gráficos daquela estampa (logo, grafismo, texto, ícone) isolados, em alta resolução, com fundo transparente, dentro do CorelDRAW, pra depois montar em cima do molde da peça no SISBOLT (fora do escopo deste projeto).

Esse é o produto final vendido dentro do pacote de R$24.000 já fechado com o Davidson, não um MVP de vitrine. O público não é designer treinado necessariamente, é operador de confecção usando o CorelDRAW no trabalho, em PC comum, sem GPU dedicada na maioria dos casos, com internet de pequena empresa (pode cair).

## 2. Escopo

Dentro:
- Login por email/senha contra o mantosfc, sessão persistente por período configurável (não pedir login toda vez que o Corel abre).
- Addin lê a seleção ativa do CorelDRAW (`Application.ActiveSelectionRange`, `ActiveDocument.Name`), o usuário precisa ter importado a imagem no documento pelo fluxo normal do Corel antes de rodar o addin.
- Detecção automática de elementos visuais via endpoint novo do mantosfc (ex: `POST /mantos-extract/detect`, nome final a critério de quem escrever o backend), que chama OpenAI internamente. A chave da OpenAI só existe no servidor, nunca no addin. Isso é só sugestão pro usuário escolher, nunca é corte final sem confirmação visual dele. Ver seção 7.4.
- Tela de seleção: imagem original com as caixas detectadas desenhadas por cima, checklist com miniatura, usuário marca o que quer.
- Extração de cada elemento marcado via outro endpoint novo do mantosfc (ex: `POST /mantos-extract/extract`), mesma lógica: OpenAI chamado só pelo servidor, chave nunca sai de lá. Ver seção 7.5.
- Upscale local 2x fixo sobre o resultado de cada extração, via uma mini aplicação companheira do shim que embrulha o Real-ESRGAN (NCNN-Vulkan, sem dependência de Python), no mesmo padrão de IPC por arquivo que o Siscut já usa. Ver seção 7.6.
- Import do resultado final (PNG com fundo transparente) direto no canvas ativo do Corel, um objeto por elemento extraído, via `ActiveLayer.Import` (ver seção 7.3).
- Exibição de saldo de crédito (vem do mantosfc), aviso quando baixo ou zerado.
- i18n PT/ES/EN por chave, seguindo o padrão que Optimus e Siscut já usam. Nenhum texto de UI hardcoded.
- Log local de erro em `%TEMP%\MantosExtract\docker.log`, nunca deixar exceção derrubar o host do Corel.

Fora do escopo, explícito:
- SVG, DXF. Só bitmap/PNG com transparência.
- Fitting em molde, grid do SISBOLT. Isso é responsabilidade do SISBOLT.
- Correção de perspectiva ou dobra de tecido dedicada. Confia que a extração generativa já resolve isso na prática.
- Provider alternativo de IA (Gemini ou outro) nos endpoints novos. É OpenAI direto, sem abstração de troca de provider por enquanto, isso seria complexidade sem necessidade real hoje.
- Qualquer tabela de mm/px por tipo de peça ou molde. O alvo é sempre "resultado da extração × 2", fixo.
- Reaproveitar o fluxo `LicenseClient.cs` (HWID + Ed25519) pra login de usuário. Aquilo é licença de desktop do addin, é outra coisa, propositalmente separada.

## 3. Arquitetura

**Toda a IA (detecção e extração via OpenAI) roda server-side, em endpoints novos dentro do mantosfc.** Não existe serviço novo pra hospedar, o mantosfc já é a infraestrutura de produção. A chave da OpenAI mora só lá. O shim nunca tem acesso a ela, nunca monta o prompt, só manda a imagem e recebe o resultado. Login, sessão e crédito também ficam no mantosfc (já existiam, continuam lá). Isso é o que garante a comunicação com a IA ficar de fato protegida: descompilar o shim (trivial em .NET) só revela chamadas HTTPS pra um servidor seu com um Bearer token, nada sobre OpenAI.

O shim (addin C# + WebView2 dentro do Corel) tem duas responsabilidades, e conversa com dois destinos diferentes:
- **Frontend/orquestrador**: interage com o Corel (seleção, import) e com os dois endpoints novos do mantosfc (detecção, extração), via HTTPS com o Bearer de sessão.
- **Upscale local**: o shim NÃO faz upscale ele mesmo. Ele invoca uma mini aplicação companheira, compilada à parte (mesmo modelo do executável Rust que o Siscut já shell-a), cujo único trabalho é: receber a imagem que veio da extração (já processada pela OpenAI), rodar o Real-ESRGAN nela, e devolver o resultado. Comunicação shim-para-companheiro via IPC por arquivo, seguindo o padrão já documentado em `siscut/docs/PONTE-DE-ACAO.md` (o Claude Code já sinalizou esse doc como reaproveitável direto).

Duas camadas, como o padrão já validado em Siscut/Optimus:
- **Lógica pura** em netstandard2.0, sem dependência de COM: parsing do JSON de detecção, cálculo de fator de upscale, sanitização de nome de arquivo/camada, cliente HTTP do mantosfc.
- **Camada net48 com COM**: tudo que toca `Application`/`ActiveDocument`/`ActiveLayer` do Corel.
- Testes xUnit net8.0 cobrindo só a camada pura (não depende do Corel estar aberto pra rodar).
- Bridge JS-para-C#: copiar o padrão do **Optimus** (`WebMessageAsJson`, não a versão antiga de string crua do Siscut). C#-para-JS sempre `ExecuteScriptAsync(...)` fire-and-forget, o canal `message` não é confiável dentro do docker WPF do Corel.
- Copiar do Optimus também: o backstop de `Dispose` do WebView2 (evita crash do Corel pelo finalizador, o Siscut não tem isso), o gate `check-ui-js.js` do build, e o padrão de i18n injetado.
- Config e user-data do WebView2 sempre em `%LOCALAPPDATA%`, nunca `Program Files` (init falha silencioso lá).
- TLS 1.2 forçado em toda chamada HTTP saindo do addin, o AppDomain do Corel fica em TLS 1.0 por padrão.
- Arquivos abaixo de 500 linhas, nomes específicos e grepáveis.

## 4. Fluxo de telas

[mockups ASCII completos — ver histórico da conversa / mensagem original do Dave, omitidos aqui só por brevidade de armazenamento; nenhum conteúdo normativo foi perdido, as seções 4.1–4.8 e os edge cases foram todos incorporados nos planos de fase]

## 5. Direção de design, pra não parecer "cara de IA"

Escolha UMA direção e execute com consistência, não misture:

- **Tom**: ferramenta industrial de precisão, não "app de IA genérico". Pense em painel de máquina de corte a laser ou mesa de trabalho de ateliê, não dashboard de SaaS.
- **Cor**: base grafite escuro (não preto puro, não cinza morto), um único acento saturado e confiante. Evite ativamente roxo/azul degradê, é o clichê visual de toda ferramenta de IA de 2025/2026 até agora. Considere âmbar/laranja de sinalização industrial ou um verde-oficina, cor que remete a equipamento de trabalho, não a "produto de tecnologia".
- **Tipografia**: evite Inter, Roboto, Arial, qualquer fonte de sistema genérica. Números e dimensões (créditos, pixels) merecem uma fonte com peso técnico tipo régua/mostrador. Labels curtos podem usar uma sans humanista mais simples. Duas fontes no máximo.
- **Ícone antes de texto**, sempre que der (o Dave já pediu isso desde o início). Texto só quando o ícone sozinho não é suficiente pra entender (erro, crédito esgotado).
- **Motion**: mínimo, só feedback funcional (progresso, transição de estado). Isso roda ao lado do Corel o dia inteiro, não é uma landing page, animação decorativa cansa e distrai.
- Evite: glassmorphism, blur genérico de fundo, gradiente decorativo sem função, ícone de estrela/sparkle (virou clichê visual de "isso usa IA").

## 6. Edge cases do público (confecção)

- Internet cai no meio da extração: ver seção 4, não perde progresso parcial.
- Foto de qualidade muito baixa (print de WhatsApp recomprimido): avisar antes de gastar crédito, algo como "essa imagem está com pouca qualidade, o resultado pode sair ruim, continuar mesmo assim?", não bloquear, só avisar.
- Usuário sem vocabulário de design: mensagem do estado vazio (4.2) tem que ser literal, sem jargão tipo "objeto vetorial" ou "camada ativa".
- PC sem GPU dedicada: Real-ESRGAN cai pra CPU automaticamente, avisar que vai demorar mais em vez de travar sem feedback nenhum.
- Créditos acabam no meio de um lote de 4: extrai o que der, avisa quantos ficaram de fora, não cobra os que não rodaram.
- Nome de elemento ou de cliente com caractere especial (comum, tipo "José & Cia"): sanitizar antes de virar nome de camada/arquivo no Corel.
- Tela pequena (1366x768 é comum nesse público): lista vertical rolável, nunca grid largo fixo.

## 7. Contratos técnicos conhecidos

### 7.1 Seleção e documento ativo no Corel

Copiar o padrão já validado em Siscut/Optimus: polling por heartbeat de 2,5s em `_app.ActiveSelection.Shapes` e `_app.ActiveDocument.Name`, com o helper `Safe()` que já existe pra evitar exceção quando não há Corel/documento aberto. O Corel não expõe evento confiável pra mudança de seleção.

### 7.2 Export de referência (já existe, é o espelho do que falta)

`CorelExporter.cs` do Siscut já faz `ClearSelection()` -> `AddToSelection()` -> `ExportEx`/`ExportBitmap` com `cdrSelection=2`, DPI configurável, PNG/TIFF/JPEG. Leia esse arquivo antes de escrever o import, é o mesmo tipo de chamada em sentido contrário.

### 7.3 Import do resultado pro canvas (não existe ainda, é novo)

A chamada correta pra importar um arquivo pro documento ativo é `ActiveLayer.Import(caminhoDoArquivo)`, que devolve a shape recém-importada como `ActiveSelectionRange` (confirmado em código real de addin CorelDRAW). Depois de importar, posicionar via `PositionX`/`PositionY` da shape, relativo ao `ActivePage.BoundingBox` ou à posição da imagem original selecionada, o que fizer mais sentido depois de ver o comportamento real. Sempre via `Type.InvokeMember`, nunca binder `dynamic` solto, isso já quebrou nesse cliente com "Could not convert argument 0". `ExportFilter.Finish()` é obrigatório depois de qualquer operação de export/import que use `ExportFilter`. Toda constante do Corel usada tem que ser conferida contra `optimus/docs/vgcore-tlb-dump.txt` antes de virar código, o bug de `cdrMillimeter=3` já aconteceu duas vezes nesse cliente por assumir valor óbvio errado.

### 7.4 Contrato de detecção (endpoint novo do mantosfc)

`POST /mantos-extract/detect` (ou nome equivalente) no mantosfc, não uma chamada direta do addin pra OpenAI. Recebe a imagem, chama OpenAI internamente com structured output via `json_schema`, devolve bounding box normalizado de 0 a 1000 (não pixel absoluto, mais estável entre imagens de tamanho diferente, é o formato que a documentação da OpenAI recomenda hoje) mais rótulo curto:

```json
{
  "elements": [
    {
      "id": "el_1",
      "label": "logo",
      "bbox": { "x_min": 120, "y_min": 300, "x_max": 400, "y_max": 550 }
    }
  ]
}
```

Isso é só pra desenhar a caixa e o rótulo na tela de seleção (4.4). O corte de verdade não usa essa bbox como coordenada final, quem extrai de fato é o endpoint de extração (7.5).

### 7.5 Contrato de extração (endpoint novo do mantosfc)

`POST /mantos-extract/extract` no mantosfc. Recebe a imagem original mais a região (bbox) que o usuário confirmou na tela 4.4, chama OpenAI internamente, devolve o elemento extraído com fundo transparente. Autenticado com o mesmo Bearer de sessão do login (não confundir com o HWID+Ed25519 do `LicenseClient.cs`, são sistemas separados). Débito de crédito acontece nesse endpoint, reaproveitando o `credit_check_middleware.ts`/ledger que já existe no mantosfc, um request 2xx bem sucedido debita um crédito, igual a spec já assumia na tela 4.4 ("3 selecionados, 3 créditos").

### 7.6 Upscale local, mini aplicação companheira do shim

Não é uma chamada HTTP, não é embutido na lógica do shim. É um executável separado, compilado à parte, instalado junto com o addin, cujo único trabalho é: ler a imagem extraída (já processada pela OpenAI), rodar Real-ESRGAN NCNN-Vulkan nela (sem dependência de Python, cai pra CPU se não tiver GPU dedicada, mais lento mas funcional), escrever o resultado. Comunicação shim-para-companheiro via IPC por arquivo, mesmo padrão documentado em `siscut/docs/PONTE-DE-ACAO.md`. Fator fixo 2x sobre o resultado que já vem em até ~3840px da OpenAI. A flag e o modelo exatos do binário (`-s 2`, qual `.param`/`.bin`) precisam ser validados empiricamente antes de fixar no código, não assuma sem testar.

### 7.7 Login e sessão

Email/senha contra o mantosfc. O Claude Code já confirmou que o `session_service.ts` usa UUID puro com TTL fixo por tipo de usuário (`SESSION_TTL_CREATOR_HOURS`, default 24h), sem endpoint de refresh, então não existe refresh token pra usar. Guardar o sessionId (UUID) via DPAPI e reusar até vencer, nunca guardar senha em texto puro. Login de novo só depois que a sessão realmente expira. Sessão persiste mesmo fechando e reabrindo o Corel, dentro da validade do TTL.

## 8. Fases de entrega, fatias verticais

Cada fase entrega algo rodando de ponta a ponta dentro do Corel de verdade, não "todo backend primeiro".

**Fase 1**: docker abre dentro do Corel, tela de login (4.1), autentica contra o mantosfc de verdade, mostra saldo de crédito real. Critério de aceite: fechar e abrir o Corel de novo dentro da validade do token não pede login de novo.

**Fase 2**: usuário seleciona um bitmap no Corel, clica detectar, recebe bounding box real da OpenAI, vê a tela 4.4 com overlay e checklist funcionando. Critério de aceite: rodar com uma foto real de celular e ver as caixas aparecerem no lugar certo, mesmo que erradas às vezes (é esperado, é só sugestão).

**Fase 3**: usuário confirma seleção, extração real via `POST /mantos-extract/extract` do mantosfc roda, resultado (ainda sem upscale) é importado no canvas via `ActiveLayer.Import`. Critério de aceite: o elemento aparece como objeto novo dentro do documento do Corel, fundo transparente, no lugar esperado, e crédito foi debitado no mantosfc.

**Fase 4**: mini aplicação companheira de upscale (7.6) entra no pipeline entre extração e import, via IPC por arquivo. Critério de aceite: comparar dimensão do arquivo antes e depois, tem que estar em 2x, e o shim não pode travar se o companheiro demorar ou falhar.

**Fase 5**: estados de erro (4.7), créditos esgotados (4.8), retomada de lote interrompido, i18n completo PT/ES/EN, log em `%TEMP%\MantosExtract\docker.log`.

## 9. Checklist pré-commit

- [ ] Build passa
- [ ] Lint limpo
- [ ] Tipos explícitos, sem `any`/`var` solto onde o tipo importa
- [ ] Testes xUnit pra lógica pura (parsing do JSON de detecção, cálculo de upscale, sanitização de nome)
- [ ] Nenhuma chave de API hardcoded em nenhum arquivo do addin ou do companheiro de upscale (o próprio Siscut tem esse problema hoje em `ApiCredentials.cs`, não repetir). Chave da OpenAI existe só no mantosfc.
- [ ] Nenhum texto de UI fora do sistema de i18n
- [ ] Arquivos abaixo de 500 linhas
- [ ] Todo entry point que toca o Corel com try/catch e log, nunca derruba o host
- [ ] Constantes do Corel conferidas contra `vgcore-tlb-dump.txt`
- [ ] Nenhuma bbox da OpenAI vira corte final sem confirmação visual do usuário
