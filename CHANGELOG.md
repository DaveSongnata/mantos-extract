# CHANGELOG — decisões técnicas

Registro exigido pela seção 0.7 do spec (`docs/mantos-extract-spec.md`): decisão técnica eu
resolvo sozinho e registro aqui com o porquê; decisão de produto eu paro e pergunto. Ordem
cronológica, mais recente no topo.

---

## 2026-09-07 — Reversão de M2: Mantos Extract não debita mais crédito

Decisão de produto do Dave (não técnica minha — ele que pediu a mudança e definiu o motivo).
Argumento dele: Mantos Extract é BYOK (a confecção traz a própria chave OpenAI) e a confecção
já paga o plano do mantosfc — debitar "crédito" da plataforma em cima de uma chamada cujo custo
de IA já sai do bolso do próprio tenant é cobrar duas vezes por algo que não custa nada pra
plataforma. Não existe nenhum usuário Mantos Extract em produção ainda, então a mudança é segura
agora (nenhum cliente pagante afetado) — diferente de reabrir a mesma pergunta pro Gemini
(`/generation/*`, `/extraction/inpaint`), que já tem tenants reais sendo cobrados hoje; isso
ficou deliberadamente FORA do escopo desta mudança.

**O que muda de fato:** nenhuma chamada a `/api/v1/mantos-extract/detect` ou
`/api/v1/mantos-extract/extract` consome mais `creditsRemaining`. A tabela `generations` do
mantosfc continua logando cada chamada (endpoint, IP, timestamp, status) sem nenhuma mudança —
isso deixa a base de dados pronta pro Dave criar, se quiser no futuro, uma lógica de limite POR
PLANO (um teto mensal por plano, por exemplo) em vez do modelo atual de crédito
comprado/consumido. Essa lógica de limite futura NÃO foi implementada agora, só ficou possível.

A remoção do middleware de crédito nesses dois endpoints (rota `/api/v1/mantos-extract/*` saindo
do grupo `[creditCheck, activeSubscription]`) e o ajuste dos testes funcionais correspondentes
foi feita no repo `mantosfc`, em paralelo a esta entrada, por outro agente na mesma sessão — ver
o CHANGELOG/histórico de commits daquele repo pros detalhes de arquivo.

Neste repo (`mantos-extract`), a reversão tocou: `CLAUDE.md` (seção "Créditos" e linha M2 da
tabela de Locked Decisions), a UI de seleção do addin (removida a menção a custo de crédito na
tela de detecção/extração) e `plans/Phase_2.md`/`plans/Phase_3.md` (requisitos que citavam
débito de crédito).

## 2026-09-04 — Trocar senha em Configurações (voluntário, não forçado)

Dave pediu inicialmente um fluxo de troca OBRIGATÓRIA no primeiro login (conta criada pelo
admin com senha padrão → força trocar antes de usar) — cheguei a implementar um flag
`mustChangePassword` no mantosfc (migration + model + login/me response), mas ele voltou atrás
no meio da implementação: "vamo voltar essa parte ai, nao vamos colocar esse 'precisa trocar
a senha', so vamos disponibilizar". Revertido tudo do lado mantosfc antes de commitar
(`git restore` + apagar a migration nova — nada disso chegou a ir pro repo).

O que ficou: só disponibilizar a troca voluntária, a qualquer momento, em Configurações.
Achado importante: **o endpoint já existia pronto no mantosfc**
(`POST /api/v1/creator/me/change-password`, `me_controller.ts`) — zero mudança de backend
necessária. Todo o trabalho ficou neste repo: `IMantosfcAuthClient.ChangePasswordAsync` +
implementação real em `MantosfcAuthClient` (mesmo padrão de `SendAsync`/`BuildError` do
login), `AuthOrchestrator.ChangePasswordAsync` (carrega a sessão local, delega pro client —
não é uma decisão de tela, então não devolve `AuthOrchestratorResult`), novo comando
`changePassword` no `MantosExtractBridge` com seu PRÓPRIO tipo de resposta (nunca `"auth"` —
trocar senha não navega pra lugar nenhum, o operador continua em Configurações), e a seção
nova na tela de Configurações (`index.html`): senha atual + nova + confirmar, validação
client-side (tamanho mínimo 8, confirmação bate) antes de chamar o servidor.

`dotnet test` 317/317 verde (285 → 317: +2 `AuthOrchestrator`, +3 `MantosfcAuthClient`, resto
é cobertura de i18n automática pras ~11 chaves novas × 3 idiomas).

---

## 2026-09-04 — Vídeo de fundo + glassmorphism de bordas duras na tela de login

Dave mandou o clipe (`docs/loop.mp4`, 1,5 MB) e pediu explicitamente um efeito de vidro
fosco/desfoque gaussiano no fundo da tela de login — uma exceção DELIBERADA à regra "nunca
glassmorphism" do design system (documentada agora no `CLAUDE.md`, seção UI Rules, pra não
virar "correção" acidental de alguém lendo a regra geral sem ver a exceção).

Decisão técnica: não dava pra embutir o vídeo como `data:` URI igual o `login-mark.png` — 1,5
MB vira ~2 MB de base64 dentro do `index.html`, que já ficou pesado só com o PNG de 33 KB (o
próprio agente não consegue mais ler o arquivo inteiro de uma vez por causa disso). Em vez
disso, `loop.mp4` virou um SEGUNDO `EmbeddedResource` (`ui.loop.mp4`) no
`MantosExtract.AddIn.csproj`, extraído por `MantosExtractBridge.ExtractUi()` pra dentro da
MESMA pasta temp que já serve o `index.html` — um `<video src="loop.mp4">` relativo no HTML
resolve sozinho contra o virtual host `https://mantosextract.app/` que já existe, sem precisar
de nenhuma mudança na Docker/bridge além de extrair um arquivo a mais. Extração é tolerante:
resource ausente não derruba nada, a tela de login só perde o vídeo e sobra o fundo sólido.

Efeito: `<video autoplay muted loop playsinline>` com `filter: blur(20px)` posicionado atrás
de um cartão (`.login-glass`) com `backdrop-filter: blur(16px)` + fundo branco 10% opaco +
borda branca translúcida — mantendo raio ZERO e sem gradiente (só opacidade+blur), pra ficar
"vidro fosco Suíço" em vez do glassmorphism genérico arredondado que todo mundo usa. Balanço
de `<div>` da seção conferido programaticamente (8 aberturas / 8 fechamentos) antes de buildar,
já que o arquivo ficou grande demais pra revisão visual direta de um `Read` completo.

---

## 2026-09-04 — Logo na tela de login + remoção de "Esqueci minha senha"

Dave notou que a marca aparecia no instalador e no ícone do addin no Corel, mas não na tela de
login — o `.login-mark .badge` ainda era um placeholder de texto ("ME" num quadrado preto),
nunca trocado quando o ícone real foi conectado. Corrigido: `gen_icons.ps1` agora também gera
`login-mark.png` (mesmo recorte "core" do glifo central usado no `.ico`, 160px, fundo
recortado pra transparente com uma faixa de anti-aliasing suave em vez de um corte duro) e o
`index.html` embute esse PNG como `data:` URI direto no `<img>` — mantém a regra "HTML
autocontido, sem CDN, sem arquivo externo" (não virou um segundo `EmbeddedResource` no
`.csproj`, só inline no HTML que já é o único arquivo embutido).

Também removido o botão "Esqueci minha senha" da tela de login (e as 3 chaves de i18n
correspondentes em Pt/Es/En) — Dave apontou que não faz sentido aqui: o login é gerenciado
inteiramente pelo mantosfc.com, o addin só é a porta de entrada. Recuperação de senha é
problema do mantosfc, não deste addin.

`dotnet test` caiu de 285 para 282 (esperado, não regressão — 3 testes de cobertura de i18n
por locale desapareceram junto com a chave removida).

---

## 2026-09-04 — Ícone da marca conectado de verdade (não é mais placeholder)

O Dave gerou (via IA de imagem) e aprovou a arte da marca: camisa/gola em Swiss Style com uma
faixa diagonal branca "cortando" o desenho (metáfora de extração) + marcas de registro de
canto (`+` e cantos em L), paleta restrita a vermelho/preto/branco/cinza — bate 1:1 com o
design system já travado. Como é uma imagem raster gerada por IA (2048×2048, sem versão
vetorial), simplificar pra um ícone de 16px teria que ser fiel à arte aprovada, não uma
reinterpretação minha — resolvido por segmentação: um script mede os componentes conectados
do JPG fonte (fundo vs. blobs de cor) pra achar automaticamente a caixa do GLIFO CENTRAL
(camisa + corte) separada das quatro marcas de canto e das cruzes, que são blobs isolados e
pequenos. Dois enquadres da MESMA arte, não dois designs: 16/24/32/48px usam só o glifo
central (as marcas de canto viram ruído ilegível nesse tamanho); 256px usa a arte completa com
as molduras, que ainda se lê bem nesse tamanho e reforça a moldura Swiss Style. Validado antes
de commitar: gerei uma folha de pré-visualização com nearest-neighbor scale-up de cada
resolução pra conferir a legibilidade real, não só confiar no redimensionamento.

`src/MantosExtract.Resources/icons/gen_icons.ps1` (recorta+redimensiona, gera os PNGs +
`mantosextract.ico`) e `build_res.ps1` (empacota o `.res` Win32 com RT_GROUP_ICON 101 +
RT_STRING "Mantos Extract") — mesma receita byte-a-byte de
`../optimus/src/Optimus.Resources/{gen_icons,build_res}.ps1`, só trocando a fonte de "desenhar
programaticamente" pra "recortar de um arquivo aprovado". `MantosExtract.Resources.csproj`
ganhou `<Win32Resource>mantosextract.res</Win32Resource>` de verdade (antes: DLL vazio, sem
recurso nenhum). O mesmo `.ico` virou `ApplicationIcon` do instalador e ícone da janela do
wizard (`installer/MantosExtract.Installer.csproj`, `InstallerForm.LoadAppIcon`). Rebuild
completo + `build-all.ps1` + extração do ícone do EXE final confirmam visualmente que a marca
aparece de verdade no `MantosExtract_Setup.exe` publicado.

Fecha o último gap real do `plans/Phase_5.md` (item 2) — a Fase 5 e o pacote de build/
instalador estão completos agora, o único "não testado" que sobra é validação numa VM com
CorelDRAW de verdade (não é código faltando, é ambiente).

---

## 2026-09-04 — Instalador single-EXE real (não é mais placeholder)

Depois de publicar o kit ZIP versionado (entrada anterior deste changelog), o Dave apontou —
com razão — que "kit manual pra extrair na mão" não é um instalador completo, e que o gap do
ícone da marca não deveria ter travado o resto (instalar de verdade, com wizard e
desinstalação registrada no Windows, não depende de nenhuma arte aprovada). Construído
`installer/Core/InstallerEngine.cs` + `installer/Core/Uninstaller.cs` +
`installer/InstallerForm.cs` + `installer/Program.cs`, portados quase 1:1 de
`../optimus/installer/` (mesma receita: acha toda instalação do CorelDRAW em
`Program Files\Corel\...`, fecha o Corel antes de escrever, apaga-e-recopia a pasta do addon,
verifica espaço em disco antes de copiar um byte, limpa Mark-of-the-Web de cada DLL, registra
em "Aplicativos e Recursos" com uma cópia de si mesmo como desinstalador). Diferenças
deliberadas em relação ao Optimus: sem o módulo de app de manutenção (Mantos Extract não tem
equivalente), sem o payload de voz/Whisper, e a UI do wizard (`installer/wwwroot/index.html`)
usa os tokens Swiss Style do próprio addin em vez do visual laranja arredondado do Optimus —
o instalador é a primeira tela que o cliente vê, não podia destoar do resto do produto. 3
passos (Bem-vindo/Instalando/Concluído) em vez dos 4 do Optimus, porque não existe o passo de
opt-in do app de manutenção aqui.

`scripts/build-all.ps1` atualizado pra compilar o instalador (com `-p:Version=` estampando
`Build.Tag`) e publicar o EXE do ILRepack (`installer/bin/Release/net48/packed/
MantosExtract_Setup.exe`) na pasta versionada, no lugar do ZIP manual. Único gap real que
sobrou é cosmético: sem `.ico` de marca ainda (ver `plans/Phase_5.md` item 2) — o botão do
Corel e a janela do instalador rodam com ícone padrão até existir uma arte aprovada; isso não
impede a instalação nem o funcionamento do addin. Rodado de ponta a ponta nesta sessão: build
completo + `dotnet test` (285/285 verdes) + `build-all.ps1` gerando
`shared/bin/redistributables/Clientes/0.1.0/MantosExtract_Setup.exe` (3,8 MB, EXE único, 22
arquivos de payload embutidos).

---

## 2026-09-04 — Processo de build versionado (`scripts/build-all.ps1`), mesmo padrão do Optimus/SisCut

Dave pediu pra padronizar o processo de entrega igual já existe no Optimus e no SisCut: build
completo → payload → pasta de cliente VERSIONADA, gerada automaticamente, sem apagar versões
anteriores (`shared/bin/redistributables/Clientes/<versão>/`). Criado `scripts/build-all.ps1`
espelhando `../optimus/scripts/build-all.ps1` (mesma estrutura de steps, mesmo caminho de
publicação, mesma leitura de versão via `Build.Tag`). `shared/bin/` entrou no `.gitignore`
(binário grande, não é fonte — mesma regra do Optimus).

Primeira versão deste script não gerava instalador single-EXE (zipava o payload cru) — ver a
entrada acima, corrigida na mesma sessão depois do Dave apontar que isso não era aceitável.

---

## 2026-09-04 — Correção: fallback de chave OpenAI REMOVIDO (era o pior cenário, não o mais seguro)

Ao explicar o fallback `OPENAI_API_KEY` no `.env`, o Dave apontou o problema real: em
produção, se essa var ficasse setada (por engano, por um ops "só garantindo"), ela vira a
chave do PRÓPRIO Davidson cobrindo silenciosamente qualquer tenant que esqueça de configurar
a própria — custo sem teto, sem aviso. "Fallback de dev" e "vazamento de custo em produção"
são o MESMO código quando a única diferença entre os dois ambientes é uma variável de
ambiente que alguém pode setar sem entender a implicação.

**Fix:** `openai_credentials.ts` não lê `env.get('OPENAI_API_KEY')` mais — a chave só pode
vir do header `X-OpenAI-Api-Key`, sem exceção, nem em dev/teste. Sem header = sempre 422
`E_MISSING_OPENAI_KEY`. Removido também: a var `OPENAI_API_KEY` do schema (`env.ts`), do
`.env.example` e do `docker-compose.yml` — não só parou de ser lida, deixou de EXISTIR como
opção, pra ninguém recriar essa armadilha sem querer. Ficaram só `OPENAI_DETECTION_MODEL`/
`OPENAI_EXTRACTION_MODEL` (nome do modelo default, sem custo/segurança envolvido — a chamada
real sempre usa a chave do tenant).

Teste funcional atualizado (`mantos_extract_routes.spec.ts`): a asserção que antes aceitava
`E_MISSING_OPENAI_KEY` OU `E_INVALID_IMAGE` (contemplando um `.env` local com fallback)
virou estrita — só `E_MISSING_OPENAI_KEY` é aceitável agora, porque não existe mais cenário
onde a credencial passa sem o header.

**Tela confirmada (pedido do Dave):** o addin já checava isso ANTES de bater na rede
(`MantosExtractBridge.RunDetectAsync`/`RunExtractAsync`, linha com `LoadOpenAiKey()`) — sem
chave salva localmente, mostra banner inline na tela inicial: "Configure sua chave da OpenAI
em Configurações antes de detectar." (`me.detect.error.noKey`). Se por algum motivo esse
check local for contornado, a resposta 422 do servidor cai na MESMA rota de exibição (catch
de `MantosExtractApiException` → mesmo banner) — duas camadas, mesma tela visível, nunca
engolido em silêncio. Nada mudou nessa tela nesta correção — só confirmado que continua
certa depois do fallback sair do backend.

`tsc --noEmit` e `eslint` limpos após a correção (arquivos tocados:
`openai_credentials.ts`, `env.ts`, `.env.example`, `docker-compose.yml`,
`mantos_extract_routes.spec.ts`).

## 2026-09-03 (continuação 3) — Modo preview (dev-only) pra revisar UI fora do Corel

O Dave abriu o `index.html` puro num browser pra olhar o redesign e ficou preso na tela de
"Carregando..." — comportamento CORRETO do produto (a página espera resposta de um host que
não existe fora do WebView2 real), mas inútil pra revisão visual. Adicionado um harness de
preview que simula as respostas do C#, só ativo fora do host real.

**Como funciona, e por que não é risco de segurança/negócio** (pergunta legítima do Dave,
respondida e registrada aqui):
- `wwwroot/preview-mock.js` é um arquivo NOVO, deliberadamente **fora** da lista
  `<EmbeddedResource>` do `MantosExtract.AddIn.csproj` (só `index.html` está lá) — nunca é
  copiado pro payload/instalador, nunca existe na máquina de um cliente.
- Mesmo que existisse lá, a primeira linha é `if (window.chrome && window.chrome.webview)
  return;` — dentro do CorelDRAW real essa condição é sempre verdadeira (é a própria API do
  WebView2), então o mock se desliga sozinho.
- Os dados simulados são 100% fictícios (imagem placeholder SVG, bounding boxes fixas,
  contador de crédito numa variável JS que zera ao recarregar) — não processa foto real, não
  chama OpenAI, não toca o mantosfc. Não tem "uso de graça" possível por esse caminho porque
  não existe nenhum resultado de valor sendo produzido.
- Precedente já usado no SisCut: `SisCut.Tester`/`SisCut.VmSmoke` são ferramentas que também
  só vivem no repo, nunca no pacote entregue ao cliente.

**Mecanismo:** `index.html` ganhou uma tag `<script src="preview-mock.js">` (404 silencioso
em produção) e um `if (window.__mantosExtractPreview)` de uma linha dentro de `send()`. O
mock simula todo o ciclo bootstrap→login→home→detect→select→extract→result com timings
realistas, e injeta um painel flutuante (canto inferior direito, só existe em modo preview)
com um botão por tela pra pular direto sem precisar simular o fluxo inteiro toda vez.

**Uso:** abrir `src/MantosExtract.AddIn/wwwroot/index.html` direto num browser qualquer.

## 2026-09-03 (continuação 2) — Redesign completo: Swiss Style futebol/confecção

Depois do v1.0 funcional, o Dave pediu refazer TODA a parte visual "com intencionalidade" —
Swiss Style (Estilo Tipográfico Internacional) aplicado ao universo de futebol/confecção,
com duas imagens de referência (biblioteca de componentes + pôster). Isso substitui a
direção "painel industrial escuro" da seção 5 do spec original, que o próprio Dave já tinha
sinalizado como superada ao trazer as referências novas.

### O que mudou (design system, não reskin de cor)

- **Paleta reduzida a 4 cores** — papel off-white, preto quase puro, um único vermelho de
  ação, dois cinzas. Removidos: teal (accent antigo), âmbar (warn antigo), verde (ok antigo).
  Diferenciação de erro/aviso/sucesso agora é por FORMA/ícone (cartão vermelho de futebol
  pra erro, triângulo de aviso, check), não por matiz nova — o sistema já é
  vermelho+preto+branco+cinza, ponto, igual as referências.
- **Raio de borda ZERO em todo elemento**, bordas sempre pretas de 2px (nunca hairline
  cinza) — trocado em botões, inputs, cards, badges, tabela, barra de progresso.
- **Tipografia: Arial/Arial Black**, não Bahnschrift/Cascadia Mono de antes. Decisão
  consciente: o Estilo Suíço É a estética da grotesca neutra (Helvetica-lineage); Arial é o
  clone universal do Windows, escolha period-accurate, não um atalho — ao contrário da minha
  primeira escolha (Bahnschrift/DIN), que fazia sentido pro tema "industrial" antigo mas não
  pro Suíço.
- **Overlay de elementos detectados**: trocou paleta de 6 matizes arco-íris por
  número+padrão de traço (sólido/tracejado × vermelho/preto/cinza) — acessível a
  daltonismo, e mais "número de camisa" do que "sticker colorido".
- **Badge de crédito** virou chip tipo placar (número grande tabular + rótulo pequeno),
  não mais um texto discreto — números como peça gráfica, princípio central das referências.
- **Progresso/resultado de extração**: viraram tabela de dados de verdade (`<table>`, linha
  de cabeçalho preta), não mais "cards" soltos — mirando o componente "DATA TABLE" da
  referência.
- Elementos decorativos exclusivos ao sistema: listras diagonais (`.stripes`), xadrez
  (`.checker`), marcas de registro tipográfico "+" (`.reg`, usadas com moderação — no máximo
  uma por tela, nunca em todo card).

### Decisão técnica — tokens escritos pra virar design system do pacote

CSS custom properties nomeadas de forma genérica (`--paper`, `--ink`, `--red`, `--gray`,
`--lw`) e comentário explícito no topo do `<style>` dizendo que isso é pensado como ponto de
partida do design system do PACOTE inteiro (Dave: "é a criação do nosso design system"), não
só deste addin — mesmo que hoje só o Mantos Extract consuma esses tokens. Se/quando os
irmãos (SisCut/Optimus/AiSten) adotarem a mesma linha visual, é copiar o bloco `:root` e os
componentes-base (`.bar`, `.btn`, `.swiss-check`, `.dtable`) como ponto de partida.

### Bug real encontrado e corrigido durante a revisão (não era só estética)

O botão "Ver" (mostrar senha) usava `position:absolute` dentro de um `<div class="field
field-with-toggle">` que também continha o `<label>` — ou seja, o botão esticava por cima
do label inteiro, não só do campo de senha. **Esse bug já existia na Fase 1 original**
(mesma estrutura, só que centralizado por `top:50%` em vez de `top:0;bottom:0`, o que
mascarava o problema visualmente mas não o corrigia). Fix: mover o `<label>` pra fora do
`.field-with-toggle`, que agora envolve só `<input>`+`<button>`.

### Validação desta rodada

- `node scripts/check-ui-js.js`: ok.
- Balanceamento de tags HTML (script de verificação ad-hoc, não faz parte do gate
  permanente): todas as tags conferidas batem (div/section/button/span/table/tbody/th/
  label/form/svg).
- `dotnet build`/`dotnet test`: 0/0, **285/285 verdes** (9 a mais que antes — cobertura das
  3 chaves i18n novas: `me.home.credits.unit`, `me.table.element`, `me.table.status`).
- **NÃO renderizado visualmente.** Sem extensão do Chrome conectada nesta sessão e sem
  CorelDRAW aqui — não consegui tirar um screenshot real pra conferir o resultado com os
  próprios olhos. A revisão foi estrutural (build, testes, gate de JS, balanceamento de
  tags, leitura atenta linha a linha do CSS procurando contradição de layout — foi assim que
  o bug do toggle de senha apareceu). **Abrir o `index.html` num browser (ou rodar no
  CorelDRAW de verdade) antes de considerar o visual aprovado** — nenhuma checagem
  automática aqui substitui olhar pra tela.

## 2026-09-03 (continuação) — Fases 2-5 implementadas, produto completo

Depois do bootstrap (Fase 1), o Dave pediu explicitamente pra não parar em "Fase 2 só" —
"quero o produto completo e testável ao final, com todas as fases concluídas". Esta entrada
cobre a implementação de ponta a ponta: backend novo no mantosfc + addin completo.

### Decisão de produto (perguntada e respondida, via `AskUserQuestion`, nesta continuação)

Nenhuma nova — as duas únicas questões de produto genuínas (crédito na detecção, dono da
chave OpenAI) já tinham sido resolvidas no bootstrap. Tudo daqui pra frente foi decisão
técnica, resolvida e registrada abaixo.

### Decisão técnica — OpenAI via `fetch` nativo, não o SDK `openai` npm

`npm install openai` falhou repetidamente com `EPERM` no `backend/node_modules` (ver seção
"Gotcha de permissão" abaixo — mesma causa-raiz). Em vez de insistir, usei `fetch`/`FormData`/
`Blob` nativos do Node 24 (confirmado como runtime dos dois estágios do `Dockerfile` do
backend) — evita a dependência nova inteira, e o produto só faz 2 chamadas REST (chat
completions com `response_format: json_schema` pra detecção; `images/edits` com
`background:'transparent'` pra extração), o que não justifica uma SDK.

### Decisão técnica — modelos OpenAI

- Detecção: `gpt-4o` (default) + allowlist `gpt-4o-mini`/`gpt-4.1`/`gpt-4.1-mini`, via Chat
  Completions com **Structured Outputs** (`response_format:{type:'json_schema',...,
  strict:true}`) — confirmado atual/GA via busca na doc oficial antes de codar (não assumido
  de memória, o próprio `plans/Phase_2.md` já pedia isso).
- Extração: `gpt-image-1` via `POST /v1/images/edits`, `background:'transparent'`,
  `output_format:'png'`, `size:'auto'` — também confirmado na doc oficial (existe uma preview
  de `gpt-image-2` que **recusa** o parâmetro `background`, então `gpt-image-1` continua
  sendo o certo pra transparência).
- **Crop feito no servidor (`sharp`), não pelo modelo.** O bbox 0-1000 confirmado pelo
  operador vira um recorte determinístico ANTES de chamar `images/edits` — evita depender da
  precisão espacial do modelo (o próprio `matrix_rectifier.ts` do mantosfc já precisou de uma
  heurística à parte por causa disso). Pequeno padding (3%) porque o box do operador é
  sugestão, não pixel-perfeito.

### Decisão técnica — `X-OpenAI-Api-Key`/`X-OpenAI-Model`, mesmo desenho do Gemini

`openai_credentials.ts` espelha `gemini_credentials.ts` byte a byte na forma (header
primário, fallback só dev, 422 nunca 401), mas com uma diferença: como detecção e extração
usam **famílias de modelo diferentes** (chat-vision vs. image-edit), o resolver recebe
`{defaultModel, allowedModels}` por chamada em vez de ter uma allowlist fixa única.

### Decisão técnica — `GenerationEndpoint` estendido, não uma tabela nova

Os dois endpoints novos logam na MESMA tabela `generations` que todo o resto do mantosfc já
usa (`endpoint: 'mantos_extract_detect'|'mantos_extract_extract'`) — sem tabela nova, sem
dashboard novo, reusando 100% o que já existe (admin já vê tudo em `/admin/generations`).

### Gotcha de permissão — TrustedInstaller travou o `mantosfc` inteiro (não só o SisCut)

Ao tentar editar `mantosfc/backend/app/models/generation.ts` pela primeira vez, todo `Edit`/
`Write`/`npm install` falhava com `EPERM`. Diagnóstico (`icacls`): a árvore inteira do
`mantosfc` (backend, frontend, rembg, rembg-rust, docs) estava com dono
`NT SERVICE\TrustedInstaller` e `Mandatory Label\High Mandatory Level:(NW)` — o MESMO padrão
já documentado na memória `gotcha_installer_trustedinstaller_lock.md`, mas dessa vez afetando
o `mantosfc`, não o `siscut` (causa-raiz aqui desconhecida — não veio de rodar um instalador
do SisCut, é outro repositório).

**Fix, com UAC (Davi aceitou o prompt):**
```
takeown /f <DIR> /r /d s
icacls <DIR> /setowner <user> /t /c /q
icacls <DIR> /reset /t /c /q
icacls <DIR> /setintegritylevel Medium /t /c /q
```
**Lição nova que a memória antiga não tinha:** rodar isso recursivo (`/t`) sobre a árvore
INTEIRA (com dois `node_modules` de ~100k+ arquivos cada) trava por muito tempo — mais de uma
hora sem terminar, e o processo mostrava só ~7s de CPU acumulado nesse tempo todo, ou seja, o
gargalo não é CPU, é I/O (antivírus real-time scan por arquivo, mesma causa-raiz do `EPERM`
original do `npm install`). **Solução mais rápida:** rodar o fix ESCOPADO só nas pastas que
precisam de escrita de verdade (`backend/app`, `backend/start`, `backend/database`,
`backend/tests`, `backend/config`, os dois `package*.json`) — terminou em minutos. Detalhe
extra: o `/setintegritylevel` no ARQUIVO sozinho não basta — a pasta CONTAINER também precisa
estar em Medium, porque criar o `.tmp` do save atômico é uma escrita NA PASTA, não só no
arquivo. Corrigir só o arquivo e esquecer a pasta pai continua dando `EPERM`.

### Decisão técnica — companheiro de upscale: binário upstream, não bundle

Confirmado na prática: `assets/upscale/realesrgan-ncnn-vulkan.exe` não existe neste ambiente
(não tenho como baixar/verificar um binário de terceiros de forma responsável por aqui).
`UpscaleRunner` degrada para `UpscaleStatus.BinaryMissing` e importa o original sem 2× — o
produto funciona sem, só sem o upscale. `scripts/deploy-dev.ps1` avisa (não falha) quando o
binário está ausente. Ver `THIRD-PARTY-NOTICES.txt` pro passo manual que falta (baixar,
verificar checksum, colocar em `assets/upscale/`).

### Decisão técnica — instalador continua placeholder, deploy real via script

Não escrevi o instalador single-EXE polido (precisa de ícone aprovado pelo Davidson + wizard
WinForms + ILRepack — nada disso existe ainda). Em vez disso, `scripts/deploy-dev.ps1` faz o
build completo, roda o gate de JS, monta o payload e copia pra
`Programs64\Addons\MantosExtract\` de toda instalação do CorelDRAW encontrada na máquina —
testado de ponta a ponta neste ambiente (sem CorelDRAW instalado aqui, então ele para com um
aviso claro em vez de fingir sucesso).

### Estado real ao final desta sessão (verificado, não assumido)

- `dotnet build MantosExtract.sln -c Release`: 0 erros/0 avisos.
- `dotnet test MantosExtract.sln`: **276/276 verdes** (Auth, I18n, Detect, Extract, Upscale,
  Layout, NameSanitizer — cobre toda a lógica pura das 4 fases).
- `node scripts/check-ui-js.js`: ok (sintaxe + toda função chamada/handler resolve).
- `scripts/deploy-dev.ps1`: roda de ponta a ponta, monta payload de 22 arquivos.
- Backend mantosfc: `tsc --noEmit` limpo, `eslint` limpo nos arquivos novos/tocados (o resto
  do `routes.ts` já tinha ~30 violações de prettier PRÉ-EXISTENTES, não mexidas — fora de
  escopo). Teste funcional (`mantos_extract_routes.spec.ts`) escrito espelhando
  `generation_middleware_chain.spec.ts`, **mas não executado** — não há Postgres acessível
  neste ambiente (sem Docker, sem `.env`) pra rodar `node ace test` de verdade.
- **Nunca validado num CorelDRAW real** (nenhum aqui) nem contra a OpenAI real (nenhuma chave
  aqui). Todo o "funciona" acima é build+teste unitário+typecheck — a validação de
  comportamento real (COM, WebView2, resposta de verdade da OpenAI) é o que falta, documentado
  peça por peça em cada `plans/Phase_N.md` → VALIDATION.

---

## 2026-09-03 — Bootstrap do repositório

### Perguntado e respondido (decisão de produto, via `AskUserQuestion`)

**P1 — Detecção debita crédito?**
Achado real no código: `credit_check_middleware.ts` do mantosfc debita 1 crédito em
QUALQUER resposta 2xx dentro do grupo de rotas `/api/v1` que usa
`[creditCheck, activeSubscription]` (`routes.ts:67-78`) — é o mesmo grupo onde os dois
endpoints novos (`/mantos-extract/detect`, `/mantos-extract/extract`) precisam entrar,
porque é o único ponto de integração credit-aware que já existe. Isso contradizia a leitura
implícita do mockup 4.4 ("3 selecionados, 3 créditos", que só bate se detectar for grátis).
**Resposta do Dave: detecção também cobra 1 crédito.** Consequência: o custo real de um lote
com N elementos confirmados é 1 (detectar) + N (extrair), não N. **Ação:** a tela de seleção
(4.4) vai mostrar o crédito já gasto na detecção separado do custo da extração pendente, não
só "N selecionados, N créditos". Registrado como M2 no CLAUDE.md.

**P2 — Dono da chave OpenAI?**
O spec só dizia "a chave só existe no servidor, nunca no addin" (seção 3), o que é verdade
tanto num modelo de chave única da plataforma quanto num modelo BYOK por tenant (como o
Gemini já faz hoje via `X-Gemini-Api-Key`) — não dava pra inferir qual dos dois sem
perguntar, porque muda se precisa de UI de configuração por tenant ou não.
**Resposta do Dave: BYOK, igual o Gemini.** Consequência de UX que NÃO estava nos mockups
originais (4.1–4.8 não têm tela de configurar chave): o addin precisa de uma tela de
Configurações (entrada natural: o ícone "[.]" já presente no canto de quase todo mockup) onde
o operador cola a própria chave OpenAI uma vez. Ela fica guardada localmente (DPAPI, mesmo
cofre da sessão) e vai como header `X-OpenAI-Api-Key` em toda chamada aos dois endpoints
novos — meu próprio design de UI pra cumprir a decisão do Dave (decisão técnica, não voltei a
perguntar). Registrado como M3 no CLAUDE.md.

### Verificado no código real (confirma o spec, sem contradição)

- `session_service.ts`: sessão é UUID puro (`randomUUID()`), TTL 24h por padrão pra creator
  (`SESSION_TTL_CREATOR_HOURS`, sem endpoint de refresh) — exatamente como a seção 7.7
  do spec assumia. `POST /api/v1/auth/login` devolve `{sessionId, expiresAt, role, user:{...,
  creditsRemaining}}`; `GET /api/v1/creator/me` (atrás de `tenantAuth()`) devolve
  `creditsRemaining` de novo, pra refresh de saldo sem precisar logar de novo.
- `IVGLayer.Import(String FileName, cdrFilter Filter?, StructImportOptions Options?)` **existe
  de fato** na typelib (`optimus/docs/vgcore-tlb-dump.txt`, dentro de `INTERFACE IVGLayer`),
  junto de `ImportEx` e `Application.CreateStructImportOptions()` (espelha
  `CreateStructExportOptions()` que o `CorelExporter.cs` já usa). Confirma a seção 7.3 — o
  método existe; o comportamento exato de seleção pós-import fica pra validação na VM (Fase 3).
- `TenantAuthMiddleware` (usado por `/creator/me`) também aplica `allowedIps`/`allowedHours`
  por tenant — igual o admin. **Não estava no spec.** Se uma confecção tiver restrição de
  IP/horário configurada no painel do mantosfc, o login do addin pode falhar com
  403 `E_IP_DENIED`/`E_TIME_DENIED` mesmo com credenciais certas. Decisão técnica (não
  perguntei, é só tratamento de erro): a Fase 1 reconhece esses dois códigos com mensagem
  específica, não cai no "erro genérico de rede" da tela 4.7.

### Corrigido — o precedente citado no spec não é o certo (decisão técnica)

O spec (seção 3/instrução 6) aponta `siscut/docs/PONTE-DE-ACAO.md` como o padrão a copiar pra
"shim invoca executável companheiro via IPC por arquivo". Lendo o documento real: a Ponte de
Ação é um protocolo **peer-to-peer** — um app EXTERNO dispara uma ação NO SisCut
(`request.json`/`response.json` com claim-por-delete, pensado pra outro addin da mesma
empresa acionar o SisCut). É a direção contrária do que o Mantos Extract precisa (o próprio
shim possui e invoca um processo filho que ele mesmo terminou). O precedente que bate de
verdade é `SisCut.Engine/EngineRunner.cs`: grava arquivo de entrada → `Process.Start` com
timeout duro e `Kill()` no estouro → mapeia exit code → lê arquivo de saída. **Decisão:** o
`UpscaleRunner` (Fase 4) segue o formato do `EngineRunner.cs`, não o da Ponte de Ação. Uma
nota foi deixada no CLAUDE.md pra ninguém reabrir essa confusão depois.

### Decisão técnica — binário de upscale, não wrapper próprio

O spec fala em "mini aplicação companheira ... compilada à parte" (seção 7.6), o que poderia
ler como "escrever um wrapper nosso em cima das bindings NCNN-Vulkan". Decisão: em vez disso,
invocar diretamente o binário pré-compilado `realesrgan-ncnn-vulkan.exe` (projeto
upstream, redistribuível, já resolve fallback pra CPU sem GPU dedicada — exatamente o que a
seção 6 pede) via o mesmo padrão do `EngineRunner.cs`. Satisfaz a intenção do spec (processo
externo, IPC por arquivo, sem Python, sem travar se demorar) sem reescrever um wrapper que já
existe e é mantido por terceiros. Se Dave quiser controle mais fino (progresso via stdout,
escolha de modelo pelo operador), isso é extensão da Fase 4, não replanejamento.

### Decisão técnica — layout da solução

`MantosExtract.sln` copiado 1:1 da forma do `Optimus.sln` (instrução 0.1 do spec), nomes
trocados: `MantosExtract.Core` (netstandard2.0), `MantosExtract.Interop` (net48, COM),
`MantosExtract.Windows` (net48, DPAPI + prefs), `MantosExtract.AddIn` (net48, WebView2 +
bridge + docker + payload do addon), `MantosExtract.Resources` (ícone),
`installer/MantosExtract.Installer` (esqueleto agora, conteúdo real na Fase 5, igual o resto
do spec só menciona instalador tarde), `tests/MantosExtract.Core.Tests` (net8.0, xUnit).
3 GUIDs novos gerados (botão/wpfhost/docker) — não colidem com SisCut/Optimus/AiSten.

### Escopo desta sessão

Implementada só a **Fase 1** (login + sessão persistida via DPAPI + saldo de crédito real
contra o mantosfc já existente). Fases 2-5 ficam só no plano (`plans/Phase_2..5.md`) — não
existe backend novo (`/mantos-extract/detect`/`extract`) ainda, porque a Fase 1 não depende
dele; escrever esse código agora seria adiantar trabalho de uma fase futura sem o preceder
(a Fase 1) validado primeiro, contra o próprio princípio de fatiamento vertical do spec.
