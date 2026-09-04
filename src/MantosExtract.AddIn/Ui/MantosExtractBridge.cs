using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Web.WebView2.Core;
using MantosExtract.Core;
using MantosExtract.Core.Api;
using MantosExtract.Core.Auth;
using MantosExtract.Core.Detect;
using MantosExtract.Core.Extract;
using MantosExtract.Core.I18n;
using MantosExtract.Core.Layout;
using MantosExtract.Core.Upscale;
using MantosExtract.Interop;
using MantosExtract.Windows;

namespace MantosExtract.AddIn.Ui
{
    /// <summary>
    /// Host-agnostic core of the Mantos Extract UI: owns the JS↔C# dispatch, drives auth,
    /// detection, extraction and upscale against the real mantosfc + local COM, and posts
    /// results back to the page. The docker (<see cref="MantosExtractDocker"/>) creates a
    /// WebView2, wires its <see cref="CoreWebView2"/> to one of these, and stays dumb — same
    /// split as Optimus.AddIn.Ui.OptimusBridge, which this mirrors structurally.
    /// </summary>
    public sealed class MantosExtractBridge
    {
        private readonly CoreWebView2 _core;
        private readonly ICorelHost _corel;
        private readonly Action<Action> _runOnUi;
        private readonly LocalizationService _i18n;
        private readonly Action? _reloadForLanguage;
        private bool _running;

        private readonly ICredentialStore _credentials = new SecureCredentialStore();
        private readonly IMantosfcAuthClient _authClient = new MantosfcAuthClient();
        private readonly AuthOrchestrator _auth;
        private readonly IDetectionClient _detectionClient = new DetectionClient();
        private readonly IExtractionClient _extractionClient = new ExtractionClient();
        private readonly UpscaleRunner _upscaleRunner = new UpscaleRunner(UpscalePaths.Resolve());

        // Per-docker-session state for the current detect→extract cycle. The bridge is the ONE
        // place that remembers "what did we detect" — the page only ever echoes back which ids
        // the operator confirmed (never raw bbox/label), so a compromised/buggy page cannot
        // smuggle in coordinates the server never actually detected.
        private byte[]? _lastExportedImageBytes;
        private string _lastExportedMimeType = "image/png";
        private IReadOnlyList<DetectedElement> _lastDetectedElements = Array.Empty<DetectedElement>();

        public MantosExtractBridge(CoreWebView2 core, object app, Action<Action> runOnUi,
                                    LocalizationService? i18n = null, Action? reloadForLanguage = null)
        {
            _core = core;
            _corel = new CorelHost(app);
            _runOnUi = runOnUi;
            _i18n = i18n ?? new LocalizationService();
            _reloadForLanguage = reloadForLanguage;
            _auth = new AuthOrchestrator(_authClient, _credentials);
            _core.WebMessageReceived += OnWebMessage;
        }

        private string L(string key) => _i18n[key];

        private void SetLanguage(string tag)
        {
            _i18n.SetLanguage(tag);
            MantosExtractLog.Write("Idioma: " + _i18n.Current);
            if (_reloadForLanguage != null) _runOnUi(_reloadForLanguage);
        }

        public void Detach()
        {
            try { _core.WebMessageReceived -= OnWebMessage; } catch { }
            try { (_authClient as IDisposable)?.Dispose(); } catch { }
            try { (_detectionClient as IDisposable)?.Dispose(); } catch { }
            try { (_extractionClient as IDisposable)?.Dispose(); } catch { }
        }

        // Runs on CorelDRAW's STA event thread — an uncaught exception here would take the
        // whole process down. Never let one escape.
        private void OnWebMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            try
            {
                string json;
                try { json = e.WebMessageAsJson; }
                catch (Exception ex) { MantosExtractLog.Write("OnWebMessage read FAILED: " + ex.Message); return; }

                string cmd;
                string email = "", password = "", tag = "", openAiKey = "";
                string[] confirmedIds = Array.Empty<string>();
                using (JsonDocument doc = JsonDocument.Parse(json))
                {
                    JsonElement root = doc.RootElement;
                    cmd = Str(root, "cmd");
                    email = Str(root, "email");
                    password = Str(root, "password");
                    tag = Str(root, "tag");
                    openAiKey = Str(root, "key");
                    confirmedIds = StrArray(root, "ids");
                }

                bool isStatusQuery = cmd == "status";

                if (_running && !isStatusQuery)
                {
                    MantosExtractLog.Write("OnWebMessage: DROPPED (busy) cmd=" + cmd);
                    Post(new { type = "busy", cmd });
                    return;
                }

                if (!IsChatty(cmd)) MantosExtractLog.Write("OnWebMessage: " + cmd);

                switch (cmd)
                {
                    case "status": PostStatus(); break;
                    case "jsError": MantosExtractLog.Write("JS ERROR: " + json); break;
                    case "idioma": SetLanguage(tag); break;

                    case "bootstrap": RunAsync(async ct => PostAuth(await _auth.BootstrapAsync(ct))); break;
                    case "login": RunAsync(async ct => PostAuth(await _auth.LoginAsync(email, password, ct))); break;
                    case "logout": RunAsync(async ct => PostAuth(await _auth.LogoutAsync(ct))); break;

                    case "saveOpenAiKey": SaveOpenAiKey(openAiKey); break;

                    case "detect": RunAsync(ct => RunDetectAsync(ct)); break;
                    case "extract": RunAsync(ct => RunExtractAsync(confirmedIds, ct)); break;

                    default: MantosExtractLog.Write("OnWebMessage: comando desconhecido '" + cmd + "'"); break;
                }
            }
            catch (Exception ex)
            {
                MantosExtractLog.Write("OnWebMessage FAILED (Corel stays alive): " + ex);
            }
        }

        private void RunAsync(Func<CancellationToken, Task> work)
        {
            _running = true;
            _ = RunAndRelease(work);
        }

        private async Task RunAndRelease(Func<CancellationToken, Task> work)
        {
            try { await work(CancellationToken.None).ConfigureAwait(false); }
            catch (Exception ex)
            {
                MantosExtractLog.Write("RunAsync FAILED: " + ex);
                Post(new { type = "auth", screen = "login", error = L("me.common.error.unknown") });
            }
            finally { _running = false; }
        }

        // ---- auth (Fase 1) -------------------------------------------------------------------

        private void PostAuth(AuthOrchestratorResult result)
        {
            if (result.Screen == AuthScreen.Login)
            {
                Post(new { type = "auth", screen = "login", error = result.ErrorMessage });
                return;
            }

            SessionState session = result.Session!;
            Post(new
            {
                type = "auth",
                screen = "home",
                email = session.Email,
                role = session.Role,
                credits = session.CreditsRemaining,
                warning = result.WarningMessage,
                hasOpenAiKey = _credentials.LoadOpenAiKey() != null,
            });
        }

        private void SaveOpenAiKey(string key)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(key))
                {
                    _credentials.ClearOpenAiKey();
                    Post(new { type = "openaiKey", ok = true, saved = false });
                    return;
                }

                _credentials.SaveOpenAiKey(key);
                Post(new { type = "openaiKey", ok = true, saved = true });
            }
            catch (Exception ex)
            {
                MantosExtractLog.Write("SaveOpenAiKey FAILED: " + ex.Message);
                Post(new { type = "openaiKey", ok = false, error = L("me.common.error.unknown") });
            }
        }

        // ---- detect (Fase 2) ------------------------------------------------------------------

        private async Task RunDetectAsync(CancellationToken ct)
        {
            SessionState? session = _credentials.LoadSession();
            if (session == null) { PostAuth(AuthOrchestratorResult.ToLogin()); return; }

            string? openAiKey = _credentials.LoadOpenAiKey();
            if (string.IsNullOrWhiteSpace(openAiKey))
            {
                Post(new { type = "detect", ok = false, code = "E_MISSING_OPENAI_KEY", error = L("me.detect.error.noKey") });
                return;
            }

            if (!_corel.SelectionIsSingleBitmap)
            {
                Post(new { type = "detect", ok = false, code = "E_NO_SELECTION", error = L("me.detect.error.noSelection") });
                return;
            }

            Post(new { type = "detectProgress", stage = "exporting" });

            string exportPath = Path.Combine(WorkDir(), "selecao_" + Guid.NewGuid().ToString("N") + ".png");
            try { _corel.ExportSelectionToPng(exportPath, 200); }
            catch (Exception ex)
            {
                MantosExtractLog.Write("ExportSelectionToPng FAILED: " + ex.Message);
                Post(new { type = "detect", ok = false, code = "E_EXPORT_FAILED", error = L("me.detect.error.exportFailed") });
                return;
            }

            byte[] imageBytes = File.ReadAllBytes(exportPath);
            _lastExportedImageBytes = imageBytes;
            _lastExportedMimeType = "image/png";

            string assetName = "preview_" + Guid.NewGuid().ToString("N") + ".png";
            File.Copy(exportPath, Path.Combine(AssetsDir(), assetName), overwrite: true);

            Post(new { type = "detectProgress", stage = "detecting" });

            DetectionResult result;
            try
            {
                result = await _detectionClient.DetectAsync(session.SessionId, openAiKey!, imageBytes, "image/png", ct)
                    .ConfigureAwait(false);
            }
            catch (MantosExtractApiException ex)
            {
                MantosExtractLog.Write("DetectAsync FAILED (" + ex.Code + "): " + ex.Message);
                if (ex.Code == "E_UNAUTHORIZED") { PostAuth(AuthOrchestratorResult.ToLogin()); return; }
                Post(new { type = "detect", ok = false, code = ex.Code, error = ex.Message });
                return;
            }

            _lastDetectedElements = result.Elements;

            Post(new
            {
                type = "detect",
                ok = true,
                imageUrl = "https://mantosextract.assets/" + assetName,
                elements = MapElementsForPage(result.Elements),
            });

            await RefreshCreditsAsync(session, ct).ConfigureAwait(false);
        }

        private static object[] MapElementsForPage(IReadOnlyList<DetectedElement> elements)
        {
            var mapped = new object[elements.Count];
            for (int i = 0; i < elements.Count; i++)
            {
                DetectedElement el = elements[i];
                mapped[i] = new
                {
                    id = el.Id,
                    label = el.Label,
                    xMin = el.Box.XMin,
                    yMin = el.Box.YMin,
                    xMax = el.Box.XMax,
                    yMax = el.Box.YMax,
                };
            }
            return mapped;
        }

        // ---- extract + upscale + import (Fases 3-4) -------------------------------------------

        private async Task RunExtractAsync(string[] confirmedIds, CancellationToken ct)
        {
            SessionState? session = _credentials.LoadSession();
            if (session == null) { PostAuth(AuthOrchestratorResult.ToLogin()); return; }

            string? openAiKey = _credentials.LoadOpenAiKey();
            if (string.IsNullOrWhiteSpace(openAiKey))
            {
                Post(new { type = "detect", ok = false, code = "E_MISSING_OPENAI_KEY", error = L("me.detect.error.noKey") });
                return;
            }

            if (_lastExportedImageBytes == null)
            {
                Post(new { type = "extract", done = true, succeeded = 0, failed = 0, error = L("me.common.error.unknown") });
                return;
            }

            List<DetectedElement> confirmed = ResolveConfirmedElements(confirmedIds);
            var placedThisBatch = new List<PlacedSlot>();
            var usedNames = new Dictionary<string, int>();
            var pageBounds = _corel.ActivePageBoundsMm();

            int succeeded = 0, failed = 0, skippedNoCredits = 0;
            bool stopBatch = false;

            for (int i = 0; i < confirmed.Count; i++)
            {
                if (stopBatch)
                {
                    // Never attempted — the row must still reach a terminal UI state (it
                    // started as "queued" and would otherwise stay stuck there forever once
                    // the batch's final "extract" message arrives).
                    skippedNoCredits++;
                    Post(new { type = "extractProgress", id = confirmed[i].Id, index = i, total = confirmed.Count, stage = "done", ok = false, code = "E_NO_CREDITS" });
                    continue;
                }

                DetectedElement element = confirmed[i];
                Post(new { type = "extractProgress", id = element.Id, index = i, total = confirmed.Count, stage = "extracting" });

                try
                {
                    ExtractedImage extracted = await _extractionClient
                        .ExtractAsync(session.SessionId, openAiKey!, _lastExportedImageBytes, _lastExportedMimeType,
                                       element.Box, element.Label, ct)
                        .ConfigureAwait(false);

                    string extractedPath = Path.Combine(WorkDir(), "extraido_" + Guid.NewGuid().ToString("N") + ".png");
                    File.WriteAllBytes(extractedPath, extracted.Bytes);

                    Post(new { type = "extractProgress", id = element.Id, index = i, total = confirmed.Count, stage = "upscaling" });
                    string finalPath = ApplyUpscale(extractedPath, element.Id);

                    string safeBase = NameSanitizer.Sanitize(element.Label);
                    string safeName = usedNames.TryGetValue(safeBase, out int count)
                        ? NameSanitizer.WithSuffix(safeBase, count + 1)
                        : safeBase;
                    usedNames[safeBase] = count + 1;

                    var (leftMm, bottomMm, widthMm, heightMm) = _corel.ImportPng(finalPath);
                    var (slotLeft, slotBottom) = ElementLayout.NextSlot(
                        pageBounds.WidthMm, pageBounds.HeightMm, widthMm, heightMm, placedThisBatch);
                    _corel.MoveLastImportedShape(slotLeft, slotBottom);
                    _corel.RenameLastImportedShape(safeName);
                    placedThisBatch.Add(new PlacedSlot(widthMm, heightMm));

                    succeeded++;
                    Post(new { type = "extractProgress", id = element.Id, index = i, total = confirmed.Count, stage = "done", ok = true });
                }
                catch (MantosExtractApiException ex)
                {
                    MantosExtractLog.Write("Extract FAILED (" + ex.Code + ") for " + element.Id + ": " + ex.Message);

                    if (ex.Code == "E_UNAUTHORIZED") { PostAuth(AuthOrchestratorResult.ToLogin()); return; }

                    if (ex.Code == "E_NO_CREDITS")
                    {
                        // Server never charges a non-2xx (credit_check_middleware.ts) — the
                        // remaining elements in the batch are correctly un-attempted, not
                        // "failed": docs/mantos-extract-spec.md §6, "avisa quantos ficaram de
                        // fora, não cobra os que não rodaram".
                        stopBatch = true;
                        skippedNoCredits++;
                        Post(new { type = "extractProgress", id = element.Id, index = i, total = confirmed.Count, stage = "done", ok = false, code = ex.Code, error = ex.Message });
                        continue;
                    }

                    failed++;
                    Post(new { type = "extractProgress", id = element.Id, index = i, total = confirmed.Count, stage = "done", ok = false, code = ex.Code, error = ex.Message });
                }
                catch (Exception ex)
                {
                    // COM/import failures land here — an element that extracted fine but could
                    // not be placed on the canvas is still a failure the operator must see, not
                    // a silently lost credit (spec §6 principle applied to the import side too).
                    MantosExtractLog.Write("Import FAILED for " + element.Id + ": " + ex);
                    failed++;
                    Post(new { type = "extractProgress", id = element.Id, index = i, total = confirmed.Count, stage = "done", ok = false, code = "E_IMPORT_FAILED", error = L("me.common.error.unknown") });
                }
            }

            Post(new { type = "extract", done = true, succeeded, failed, skippedNoCredits });
            await RefreshCreditsAsync(session, ct).ConfigureAwait(false);
        }

        /// <summary>Runs the upscale step and degrades gracefully (M5 / plans/Phase_4.md): any
        /// non-success status returns the ORIGINAL extracted PNG unchanged rather than blocking
        /// the element — the operator already paid the extraction credit for it.</summary>
        private string ApplyUpscale(string extractedPngPath, string elementId)
        {
            UpscaleResult upscale = _upscaleRunner.Run(extractedPngPath);
            switch (upscale.Status)
            {
                case UpscaleStatus.Success:
                    return upscale.OutputPath!;
                case UpscaleStatus.BinaryMissing:
                    MantosExtractLog.Write("Upscale SKIPPED (binário ausente) for " + elementId);
                    return extractedPngPath;
                default:
                    MantosExtractLog.Write("Upscale " + upscale.Status + " for " + elementId + " — usando original");
                    return extractedPngPath;
            }
        }

        private List<DetectedElement> ResolveConfirmedElements(string[] ids)
        {
            var byId = new Dictionary<string, DetectedElement>();
            foreach (DetectedElement el in _lastDetectedElements) byId[el.Id] = el;

            var confirmed = new List<DetectedElement>();
            foreach (string id in ids)
                if (byId.TryGetValue(id, out DetectedElement? el)) confirmed.Add(el);
            return confirmed;
        }

        private async Task RefreshCreditsAsync(SessionState session, CancellationToken ct)
        {
            try
            {
                MeResponse me = await _authClient.GetMeAsync(session.SessionId, ct).ConfigureAwait(false);
                session.CreditsRemaining = me.CreditsRemaining;
                _credentials.SaveSession(session);
                Post(new { type = "credits", credits = me.CreditsRemaining });
            }
            catch (Exception ex)
            {
                // Best effort — the operator already saw the result of their action; a failed
                // credit refresh is a stale badge, never a reason to re-show an error banner.
                MantosExtractLog.Write("RefreshCreditsAsync FAILED: " + ex.Message);
            }
        }

        // ---- status (local Corel snapshot, no network) -----------------------------------------

        public void PostStatus()
        {
            Post(new
            {
                type = "status",
                doc = _corel.DocumentName,
                sel = _corel.SelectionShapeCount,
                selIsBitmap = _corel.SelectionIsSingleBitmap,
                build = Build.Tag,
            });
        }

        private void Post(object message)
        {
            string json = JsonSerializer.Serialize(message);
            string script = "if(window.mantosExtractReceive){window.mantosExtractReceive(" + json + ");}";
            _runOnUi(() =>
            {
                try { _ = _core.ExecuteScriptAsync(script); }
                catch (Exception ex) { MantosExtractLog.Write("Post FAILED (" + json + "): " + ex.Message); }
            });
        }

        private static bool IsChatty(string cmd) => cmd == "status";

        private static string Str(JsonElement root, string name) =>
            root.TryGetProperty(name, out JsonElement e) && e.ValueKind == JsonValueKind.String ? (e.GetString() ?? "") : "";

        private static string[] StrArray(JsonElement root, string name)
        {
            if (!root.TryGetProperty(name, out JsonElement arr) || arr.ValueKind != JsonValueKind.Array)
                return Array.Empty<string>();
            var list = new List<string>();
            foreach (JsonElement e in arr.EnumerateArray())
                if (e.ValueKind == JsonValueKind.String) list.Add(e.GetString() ?? "");
            return list.ToArray();
        }

        // ---- UI asset plumbing --------------------------------------------------------------

        public static string InstallDir()
        {
            try
            {
                string loc = typeof(MantosExtractBridge).Assembly.Location;
                if (!string.IsNullOrEmpty(loc)) return Path.GetDirectoryName(loc)!;
            }
            catch { }
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "MantosExtract");
        }

        public static string ExtractUi()
        {
            string dir = Path.Combine(Path.GetTempPath(), "MantosExtract_ui");
            Directory.CreateDirectory(dir);
            Assembly asm = typeof(MantosExtractBridge).Assembly;
            using (Stream s = asm.GetManifestResourceStream("ui.index.html")
                ?? throw new InvalidOperationException("Recurso de UI ausente: ui.index.html"))
            using (FileStream fs = File.Create(Path.Combine(dir, "index.html")))
                s.CopyTo(fs);

            // Vídeo de fundo da tela de login (loop.mp4) — mesma pasta/virtual host do
            // index.html, então um <video src="loop.mp4"> relativo no HTML já resolve sozinho.
            // Ausente não é fatal: a tela de login cai pro fundo sólido (sem <video>, sem erro).
            using (Stream? s = asm.GetManifestResourceStream("ui.loop.mp4"))
            {
                if (s != null)
                    using (FileStream fs = File.Create(Path.Combine(dir, "loop.mp4")))
                        s.CopyTo(fs);
            }

            return dir;
        }

        public static string UserDataDir()
        {
            string dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "MantosExtract", "WebView2");
            Directory.CreateDirectory(dir);
            return dir;
        }

        /// <summary>Writable folder mapped to https://mantosextract.assets/ — preview and
        /// thumbnail images the page displays directly, never routed through ExecuteScriptAsync
        /// as base64 (a multi-MB phone photo would make that both slow and memory-heavy).</summary>
        public static string AssetsDir()
        {
            string dir = Path.Combine(Path.GetTempPath(), "MantosExtract", "assets");
            Directory.CreateDirectory(dir);
            return dir;
        }

        /// <summary>Not served to the page — intermediate files (exported selection, extracted/
        /// upscaled PNGs before import) that only C#/COM ever touch.</summary>
        public static string WorkDir()
        {
            string dir = Path.Combine(Path.GetTempPath(), "MantosExtract", "work");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }
}
