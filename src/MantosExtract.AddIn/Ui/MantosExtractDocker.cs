using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using MantosExtract.Core.I18n;
using MantosExtract.Windows;

namespace MantosExtract.AddIn.Ui
{
    /// <summary>
    /// THE production UI surface: a WPF UserControl the CorelDRAW addon framework hosts in an
    /// anchored docker (addon/AppUI.xslt, <c>type="wpfhost"</c>). Built in code (no XAML). The
    /// framework instantiates it via the constructor with the live in-process Application.
    /// WebView2 inits on Loaded with a writable %LOCALAPPDATA% user-data folder; the native DLL
    /// search path is restored right after. Every step logs to %TEMP%\MantosExtract\docker.log.
    ///
    /// Mirrors optimus/src/Optimus.AddIn/Ui/OptimusDocker.cs verbatim in structure — see
    /// docs/mantos-extract-spec.md §0.1 and plans/index.md ("Reúso vs. construção").
    /// </summary>
    public sealed class MantosExtractDocker : UserControl
    {
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool SetDllDirectory(string? lpPathName);

        private readonly object? _injectedApp;
        private readonly WebView2 _web = new WebView2();
        private readonly LocalizationService _localization = LanguageStore.Service();
        private MantosExtractBridge? _bridge;
        private string _i18nScriptId = "";
        private bool _started;
        private bool _disposed;

        // CRITICAL: the CorelDRAW addon host loads MantosExtract.AddIn.dll but does NOT add the
        // addon folder to the .NET assembly probe path, and there is no app.config. Resolve
        // every managed dep from the folder this DLL sits in, ignoring the requested version
        // (load the sibling we shipped). Registered in the static ctor so it's live before any
        // dep is touched — same fix as Optimus (O-lesson, see ../../CLAUDE.md).
        static MantosExtractDocker()
        {
            AppDomain.CurrentDomain.AssemblyResolve += ResolveFromAddonDir;

            // A bug must NEVER silently take CorelDRAW down: guarantee a trace lands in the log,
            // and neutralise the two managed crash vectors we can (unobserved task faults).
            try
            {
                AppDomain.CurrentDomain.UnhandledException += (_, e) =>
                {
                    try { MantosExtractLog.Write("FATAL UnhandledException (terminating=" + e.IsTerminating + "): " + e.ExceptionObject); } catch { }
                };
            }
            catch { }
            try
            {
                System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (_, e) =>
                {
                    try { MantosExtractLog.Write("UnobservedTaskException (neutralized): " + e.Exception); } catch { }
                    e.SetObserved();
                };
            }
            catch { }
        }

        private static Assembly? ResolveFromAddonDir(object? sender, ResolveEventArgs args)
        {
            try
            {
                string dir = Path.GetDirectoryName(typeof(MantosExtractDocker).Assembly.Location) ?? "";
                string name = new AssemblyName(args.Name).Name + ".dll";
                string path = Path.Combine(dir, name);
                if (File.Exists(path))
                {
                    MantosExtractLog.Write("AssemblyResolve: " + name + " <- addon dir");
                    return Assembly.LoadFrom(path);
                }
                return null;
            }
            catch (Exception ex) { MantosExtractLog.Write("AssemblyResolve error: " + ex.Message); return null; }
        }

        // The addon framework calls this constructor; it MAY pass the CorelDRAW Application.
        public MantosExtractDocker(object app)
        {
            try
            {
                _injectedApp = app;
                MantosExtractLog.Write("=== MantosExtract docker v" + Build.Tag + " ===  ctor: injected app is "
                    + (app == null ? "NULL" : app.GetType().FullName));

                try
                {
                    Dispatcher.UnhandledException += (_, ex) =>
                    {
                        try { MantosExtractLog.Write("Dispatcher.UnhandledException (handled, Corel safe): " + ex.Exception); } catch { }
                        ex.Handled = true;
                    };
                }
                catch { }

                MinWidth = 320;
                Content = _web;
                Loaded += OnLoaded;
                Unloaded += OnUnloaded;

                // BACKSTOP: CorelDRAW's native docking host does not always walk WPF's own
                // disconnect logic when the addon panel is torn down, so Unloaded may never
                // fire, _web goes unDisposed, and is later collected as garbage — a WebView2
                // control finalized instead of disposed crashes the WHOLE PROCESS. Lesson paid
                // on Optimus (O17, ../../CLAUDE.md); SisCut does not have this backstop yet.
                // ProcessExit is the last reliable point to force a clean, explicit Dispose().
                AppDomain.CurrentDomain.ProcessExit += (_, _) => DisposeWebView();
            }
            catch (Exception ex)
            {
                try { MantosExtractLog.Write("ctor FAILED (docker disabled, Corel safe): " + ex); } catch { }
            }
        }

        // Some hosts call a parameterless ctor; cover it so instantiation never fails.
        public MantosExtractDocker() : this(null!) { }

        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            if (_started) return;
            _started = true;
            MantosExtractLog.Write("OnLoaded: start (v" + Build.Tag + ")");

            // This is an `async void` handler: an escaping exception goes to the host dispatcher
            // and CLOSES CorelDRAW. Everything degrades to a log line — the docker just doesn't
            // appear, but Corel stays alive.
            try
            {
                try
                {
                    SetDllDirectory(MantosExtractBridge.InstallDir());
                    try
                    {
                        CoreWebView2Environment env = await CoreWebView2Environment.CreateAsync(null, MantosExtractBridge.UserDataDir());
                        await _web.EnsureCoreWebView2Async(env);
                    }
                    finally { SetDllDirectory(null); }
                    MantosExtractLog.Write("OnLoaded: WebView2 ready");
                }
                catch (Exception ex)
                {
                    MantosExtractLog.Write("OnLoaded: WebView2 FAILED: " + ex);
                    return;
                }

                object app = ResolveApp();

                // The catalog is injected BEFORE the page script runs; the docker's HTML carries
                // no text of its own, so arriving late would show empty labels inside CorelDRAW.
                _i18nScriptId = await _web.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(
                    I18nScript.Build(_localization));

                // Genuinely marshals onto the UI thread — NOT "a => a()" (that form is only safe
                // while every bridge call happens to already run on the UI thread; login/HTTP
                // calls resume on a threadpool thread by default).
                _bridge = new MantosExtractBridge(_web.CoreWebView2, app, a => Dispatcher.Invoke(a),
                                                   _localization, ReloadForLanguage);
                _web.CoreWebView2.NavigationCompleted += OnNavigationCompleted;

                _web.CoreWebView2.SetVirtualHostNameToFolderMapping(
                    "mantosextract.app", MantosExtractBridge.ExtractUi(), CoreWebView2HostResourceAccessKind.Allow);

                // Second mapping: a writable folder the bridge drops preview/result thumbnails
                // into (the selection photo, extracted elements) so the page can <img src=...>
                // them directly instead of round-tripping multi-MB photos through
                // ExecuteScriptAsync as base64 (Fase 2 design note, CHANGELOG.md).
                _web.CoreWebView2.SetVirtualHostNameToFolderMapping(
                    "mantosextract.assets", MantosExtractBridge.AssetsDir(), CoreWebView2HostResourceAccessKind.Allow);

                _web.CoreWebView2.Navigate("https://mantosextract.app/index.html");
                MantosExtractLog.Write("OnLoaded: navigated (idioma=" + _localization.Current + ")");
            }
            catch (Exception ex)
            {
                MantosExtractLog.Write("OnLoaded: FAILED (docker disabled, Corel stays alive): " + ex);
            }
        }

        /// <summary>Re-injects the catalog in the new language and reloads the docker. The old
        /// injection is REMOVED first — these scripts accumulate, and two catalogs would leave
        /// the panel in mixed languages. Wrapped end to end: never takes CorelDRAW down.</summary>
        private async void ReloadForLanguage()
        {
            try
            {
                if (_web.CoreWebView2 == null) return;

                if (!string.IsNullOrEmpty(_i18nScriptId))
                    _web.CoreWebView2.RemoveScriptToExecuteOnDocumentCreated(_i18nScriptId);

                _i18nScriptId = await _web.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(
                    I18nScript.Build(_localization));

                _web.CoreWebView2.Reload();
                MantosExtractLog.Write("Docker recarregado no idioma " + _localization.Current);
            }
            catch (Exception ex)
            {
                MantosExtractLog.Write("Troca de idioma falhou (Corel segue vivo): " + ex.Message);
            }
        }

        private void OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
        {
            try { _bridge?.PostStatus(); } catch (Exception ex) { MantosExtractLog.Write("NavCompleted post failed: " + ex.Message); }
        }

        private void OnUnloaded(object sender, RoutedEventArgs e) => DisposeWebView();

        /// <summary>Idempotent: reachable from both the normal WPF teardown path (OnUnloaded)
        /// and the ProcessExit backstop above, safe to call from either more than once.</summary>
        private void DisposeWebView()
        {
            if (_disposed) return;
            _disposed = true;
            MantosExtractLog.Write("DisposeWebView: disposing");
            try { if (_web.CoreWebView2 != null) _web.CoreWebView2.NavigationCompleted -= OnNavigationCompleted; } catch { }
            try { _bridge?.Detach(); } catch { }
            try { _web.Dispose(); } catch (Exception ex) { MantosExtractLog.Write("DisposeWebView: _web.Dispose() failed: " + ex.Message); }
        }

        /// <summary>Returns the CorelDRAW Application the bridge drives. The framework injects a
        /// live in-process COM object through the constructor; only if it injects null do we
        /// fall back to the Running Object Table.</summary>
        private object ResolveApp()
        {
            if (_injectedApp != null)
            {
                MantosExtractLog.Write("ResolveApp: using injected app (" + _injectedApp.GetType().FullName + ")");
                return _injectedApp;
            }

            MantosExtractLog.Write("ResolveApp: injected app is NULL, trying ROT");
            foreach (string progId in new[] { "CorelDRAW.Application", "CorelDRAW.Application.25", "CorelDRAW.Application.24" })
            {
                try { return Marshal.GetActiveObject(progId); }
                catch (Exception ex) { MantosExtractLog.Write("ResolveApp: ROT " + progId + " failed: " + ex.Message); }
            }

            MantosExtractLog.Write("ResolveApp: NO app available");
            return _injectedApp!;
        }
    }
}
