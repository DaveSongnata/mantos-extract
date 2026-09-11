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
          reply({ type: "status", doc: "exemplo.cdr", sel: 1, selIsBitmap: true, build: "preview" });
          break;
        case "saveOpenAiKey":
          reply({ type: "openaiKey", ok: true, saved: !!cmd.key });
          break;
        case "idioma":
          break; // o host real recarrega a página inteira; nada pra simular aqui
        case "detect":
          reply({ type: "detectProgress", stage: "exporting" });
          setTimeout(function () { window.mantosExtractReceive({ type: "detectProgress", stage: "detecting" }); }, 400);
          setTimeout(function () {
            window.mantosExtractReceive({ type: "detect", ok: true, imageUrl: SAMPLE_IMAGE, elements: SAMPLE_ELEMENTS });
          }, 900);
          break;
        case "extract": {
          var ids = cmd.ids || [];
          ids.forEach(function (id, i) {
            setTimeout(function () { window.mantosExtractReceive({ type: "extractProgress", id: id, index: i, total: ids.length, stage: "extracting" }); }, 300 + i * 900);
            setTimeout(function () { window.mantosExtractReceive({ type: "extractProgress", id: id, index: i, total: ids.length, stage: "upscaling" }); }, 600 + i * 900);
            setTimeout(function () {
              var ok = !(i === 1 && ids.length > 1); // segundo elemento "falha" só pra mostrar o estado
              window.mantosExtractReceive({ type: "extractProgress", id: id, index: i, total: ids.length, stage: "done", ok: ok });
            }, 900 + i * 900);
          });
          setTimeout(function () {
            var failed = ids.length > 1 ? 1 : 0;
            credits -= (ids.length - failed);
            window.mantosExtractReceive({ type: "extract", done: true, succeeded: ids.length - failed, failed: failed, skippedNoCredits: 0 });
            window.mantosExtractReceive({ type: "credits", credits: credits });
          }, 900 + ids.length * 900 + 300);
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
    if (wanted === "select" || wanted === "extracting") {
      // index.html inteiro roda dentro de uma IIFE — beginExtraction/renderSelectScreen NÃO são
      // globais, então simula como um humano de verdade: dispara o detect real, marca as
      // checkboxes (inclusive "Fundo") via evento de DOM, clica em Extrair de verdade.
      setTimeout(function () {
        window.mantosExtractReceive({ type: "detect", ok: true, imageUrl: SAMPLE_IMAGE, elements: SAMPLE_ELEMENTS });
        setTimeout(function () {
          var boxes = document.querySelectorAll("#selectChecklist input.swiss-check");
          boxes.forEach(function (cb) { cb.checked = true; cb.dispatchEvent(new Event("change")); });
          if (wanted === "extracting") {
            var btn = document.getElementById("extractBtn");
            if (btn) btn.click();
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
