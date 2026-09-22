// preview-mock.js — DEV-ONLY visual harness, nunca embarcado no produto (só
// wwwroot/index.html está listado como <EmbeddedResource> no
// MantosExtract.AddIn.csproj; este arquivo nunca sai da árvore-fonte).
//
// Deixa abrir o index.html direto num navegador comum (file://) e clicar em cada tela
// exatamente como o host de verdade dirigiria — com um painel flutuante pra pular direto
// pra qualquer tela, sem precisar simular o fluxo inteiro toda vez.
//
// NUNCA ativa dentro do WebView2 real do CorelDRAW: window.chrome.webview sempre existe lá,
// e o guard abaixo sai de cena imediatamente nesse caso. Na produção, a tag <script src=...>
// que carrega este arquivo simplesmente dá 404 (o arquivo não é copiado pro payload) — sem
// nenhum efeito funcional, é só uma linha a mais no log de rede que ninguém olha.
(function () {
  "use strict";
  if (window.chrome && window.chrome.webview) return; // host real presente — nunca mocka

  var SAMPLE_IMAGE = "data:image/svg+xml;utf8," + encodeURIComponent(
    '<svg xmlns="http://www.w3.org/2000/svg" width="600" height="750">' +
    '<rect width="600" height="750" fill="#dedcd5"/>' +
    '<rect x="150" y="120" width="300" height="380" fill="#b7b6b0"/>' +
    '<text x="300" y="700" font-family="Arial" font-size="24" fill="#121212" text-anchor="middle">FOTO DE EXEMPLO (preview)</text>' +
    "</svg>"
  );

  var SAMPLE_ELEMENTS = [
    { id: "el_1", label: "logo", xMin: 220, yMin: 180, xMax: 380, yMax: 300 },
    { id: "el_2", label: "texto MARCOS", xMin: 180, yMin: 420, xMax: 420, yMax: 470 },
    { id: "el_3", label: "estrela", xMin: 260, yMin: 500, xMax: 340, yMax: 560 },
  ];

  var credits = 42;
  var refineSlots = { target: null, reference: null };

  function reply(msg) { setTimeout(function () { window.mantosExtractReceive(msg); }, 250); }

  window.__mantosExtractPreview = {
    receive: function (cmd) {
      switch (cmd.cmd) {
        case "bootstrap":
        case "login":
          reply({ type: "auth", screen: "home", email: cmd.email || "operador@confeccao.com.br", role: "tenant", credits: credits });
          break;
        case "logout":
          reply({ type: "auth", screen: "login" });
          break;
        case "status":
          reply({ type: "status", doc: "exemplo.cdr", sel: 1, selIsBitmap: true, canUpscale: true, build: "preview" });
          break;
        case "saveOpenAiKey":
          reply({ type: "openaiKey", ok: true, saved: !!cmd.key });
          break;
        case "idioma":
          break; // o host real recarrega a página inteira; nada pra simular aqui
        // Vagas do modal de refino: a imagem a alterar já vem preenchida ao abrir, e a referência
        // entra pelo "Usar seleção" — mesmo contrato do MantosExtractBridge.Refine.cs.
        case "refineOpen":
          refineSlots = { target: { url: SAMPLE_IMAGE }, reference: null };
          reply({ type: "refineSlots", target: refineSlots.target, reference: null });
          break;
        case "refineCapture":
          refineSlots[cmd.slot] = { url: SAMPLE_IMAGE };
          reply({ type: "refineSlots", target: refineSlots.target, reference: refineSlots.reference });
          break;
        case "refineClear":
          refineSlots[cmd.slot] = null;
          reply({ type: "refineSlots", target: refineSlots.target, reference: refineSlots.reference });
          break;
        case "refine":
          reply({ type: "refine", stage: "running" });
          setTimeout(function () { window.mantosExtractReceive({ type: "refine", stage: "done", ok: true }); }, 1500);
          break;
        case "detect":
          reply({ type: "detectProgress", stage: "exporting" });
          setTimeout(function () { window.mantosExtractReceive({ type: "detectProgress", stage: "detecting" }); }, 400);
          setTimeout(function () {
            window.mantosExtractReceive({ type: "detect", ok: true, imageUrl: SAMPLE_IMAGE, elements: SAMPLE_ELEMENTS });
          }, 900);
          break;
        case "extract": {
          // Sem estágio "upscaling" aqui (Dave, 2026-09-11): a extração entrega direto e o
          // upscale virou opcional, por botão, na tela de resultado.
          var ids = cmd.ids || [];
          ids.forEach(function (id, i) {
            setTimeout(function () { window.mantosExtractReceive({ type: "extractProgress", id: id, index: i, total: ids.length, stage: "extracting" }); }, 300 + i * 900);
            setTimeout(function () {
              var ok = !(i === 1 && ids.length > 1); // segundo elemento "falha" só pra mostrar o estado
              // ?copyright na URL: a peça que falha vem como recusa por direitos autorais.
              var copyright = !ok && location.search.indexOf("copyright") >= 0;
              window.mantosExtractReceive({ type: "extractProgress", id: id, index: i, total: ids.length, stage: "done", ok: ok,
                code: copyright ? "E_OPENAI_MODERATION" : (ok ? null : "E_UNKNOWN"),
                error: copyright ? "A OpenAI recusou processar esta imagem por política de conteúdo dela — provavelmente por conter uma marca, logo ou personagem protegido por direitos autorais. Isso não é um erro do Mantos Extract; tente outro elemento."
                                 : (ok ? null : "O servidor respondeu com erro (500).") });
            }, 900 + i * 900);
          });
          setTimeout(function () {
            var failed = ids.length > 1 ? 1 : 0;
            credits -= (ids.length - failed);
            // ?nocredit na URL: simula a conta OpenAI do cliente sem crédito (screenshot do aviso).
            var noCredit = location.search.indexOf("nocredit") >= 0;
            window.mantosExtractReceive({ type: "extract", done: true, succeeded: ids.length - failed, failed: failed, skippedNoCredits: 0, canUpscale: true,
              code: noCredit ? "E_OPENAI_NO_CREDIT" : null,
              error: noCredit ? "Sua conta da OpenAI ficou sem crédito. A chave está correta — o que acabou foi o saldo. Adicione crédito em platform.openai.com/settings/organization/billing e tente de novo." : null });
            window.mantosExtractReceive({ type: "credits", credits: credits });
          }, 900 + ids.length * 900 + 300);
          break;
        }
        case "upscaleElement": {
          var upId = cmd.id;
          reply({ type: "upscaleProgress", id: upId, stage: "running" });
          setTimeout(function () {
            // "el-2" falha de propósito, pra o preview mostrar também o estado de erro.
            var ok = upId !== "el-2";
            window.mantosExtractReceive({
              type: "upscaleProgress", id: upId, stage: "done", ok: ok,
              error: ok ? null : "Não consegui melhorar a resolução desta peça.",
            });
          }, 1400);
          break;
        }
      }
    }
  };

  // ---- painel flutuante de navegação rápida (só existe em modo preview) ----------------
  document.addEventListener("DOMContentLoaded", function () {
    var SCREENS = ["screen-loading", "screen-login", "screen-home", "screen-detecting", "screen-select",
                   "screen-extracting", "screen-result", "screen-error", "screen-credits-zero", "screen-settings"];
    var panel = document.createElement("div");
    panel.style.cssText = "position:fixed;bottom:8px;right:8px;z-index:9999;background:#121212;" +
      "border:2px solid #e0271c;padding:6px;display:flex;flex-wrap:wrap;gap:4px;max-width:230px;" +
      "font-family:Arial,sans-serif;box-shadow:0 0 0 2px #121212;";
    var label = document.createElement("div");
    label.textContent = "PREVIEW — pular pra tela:";
    label.style.cssText = "color:#fff;font-size:9px;font-weight:900;width:100%;margin-bottom:2px;text-transform:uppercase;";
    panel.appendChild(label);
    function jumpTo(id) {
      SCREENS.forEach(function (s) { var el = document.getElementById(s); if (el) el.hidden = (s !== id); });
    }
    SCREENS.forEach(function (id) {
      var b = document.createElement("button");
      b.textContent = id.replace("screen-", "");
      b.style.cssText = "font-size:9px;padding:3px 5px;background:#fff;border:1px solid #000;cursor:pointer;";
      b.addEventListener("click", function () { jumpTo(id); });
      panel.appendChild(b);
    });
    document.body.appendChild(panel);

    // ?screen=home na URL pula direto pra tela, sem precisar clicar — usado por scripts de
    // screenshot automatizado (headless), nunca por um humano abrindo o preview normalmente.
    // screen=select/extracting também populam dados de exemplo (via o MESMO caminho real de
    // window.mantosExtractReceive que um detect/extract de verdade usaria), pra a tela não
    // renderizar vazia — inclui o item especial "Fundo" marcado, pra dar pra conferir ele
    // também num screenshot automatizado.
    var qs = new URLSearchParams(location.search);
    var wanted = qs.get("screen");
    if (wanted === "home-upscaling") {
      // Tela inicial com o upscale da imagem selecionada em andamento (clique real no botão).
      setTimeout(function () {
        var home = document.getElementById("screen-home");
        SCREENS.forEach(function (s) { var el = document.getElementById(s); if (el) el.hidden = (s !== "screen-home"); });
        window.mantosExtractReceive({ type: "status", doc: "exemplo.cdr", sel: 1, selIsBitmap: true, canUpscale: true });
        setTimeout(function () { document.getElementById("homeUpscaleBtn").click(); }, 200);
      }, 1200);
    } else if (wanted === "select" || wanted === "extracting" || wanted === "result" || wanted === "result-upscaling") {
      // index.html inteiro roda dentro de uma IIFE — beginExtraction/renderSelectScreen NÃO são
      // globais, então simula como um humano de verdade: dispara o detect real, marca as
      // checkboxes (inclusive "Fundo") via evento de DOM, clica em Extrair de verdade.
      setTimeout(function () {
        window.mantosExtractReceive({ type: "detect", ok: true, imageUrl: SAMPLE_IMAGE, elements: SAMPLE_ELEMENTS });
        setTimeout(function () {
          var boxes = document.querySelectorAll("#selectChecklist input.swiss-check");
          boxes.forEach(function (cb) { cb.checked = true; cb.dispatchEvent(new Event("change")); });
          if (wanted !== "select") {
            var btn = document.getElementById("extractBtn");
            if (btn) btn.click();
          }
          // result/result-upscaling: deixa a extração simulada terminar sozinha e, no segundo
          // caso, clica de verdade num botão "Upscale" da tela de resultado — o mesmo caminho
          // que o operador percorre, pra o screenshot mostrar o estado real e não um mock.
          // 6500ms: a extração simulada acima leva ~4,8s a partir do clique em "Extrair", então
          // esperar menos que isso acharia a tela de resultado ainda vazia.
          if (wanted === "result-upscaling") {
            setTimeout(function () {
              var upBtn = document.querySelector("#resultList .btn-sm");
              if (upBtn) upBtn.click();
            }, 6500);
          }
        }, 150);
      }, 1200);
    } else if (wanted === "navtest") {
      // Reproduz o bug de navegação achado em auditoria (Dave, 2026-09-11): detecta, marca 1
      // elemento, abre Configurações, abre Histórico, abre Configurações DE NOVO, clica em
      // Voltar — antes do fix isso ficava preso alternando Config/Histórico pra sempre; depois
      // do fix, "Voltar" deve cair em screen-select. Só existe pra verificação automatizada
      // (screenshot), nunca usado por um humano no preview normal.
      setTimeout(function () {
        window.mantosExtractReceive({ type: "detect", ok: true, imageUrl: SAMPLE_IMAGE, elements: SAMPLE_ELEMENTS });
        setTimeout(function () {
          document.getElementById("settingsBtn").click();
          document.getElementById("historyBtn").click();
          document.getElementById("settingsBtn").click();
          document.getElementById("settingsBack").click();
        }, 150);
      }, 1200);
    } else if (wanted) {
      setTimeout(function () { jumpTo("screen-" + wanted); }, 1200);
    }
  });
})();
