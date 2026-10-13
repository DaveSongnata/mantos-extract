using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace MantosExtract.Installer;

static class Program
{
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool SetDllDirectory(string? lpPathName);

    [STAThread]
    static int Main(string[] args)
    {
        // "Apps & features" invokes the kept copy of this EXE with /uninstall. Handled before
        // anything WebView2-related is touched: the uninstall path must work even on a machine
        // whose WebView2 runtime was removed after installation.
        if (HasFlag(args, "uninstall"))
        {
            RunUninstall(quiet: HasFlag(args, "quiet"));
            return 0;
        }

        // /silent (2026-10-13, for tools/corel-com/Deploy-MantosExtract.ps1 -Installer): the SAME
        // InstallerEngine the wizard runs, with no window and no WebView2 — the E2E installs the
        // real setup .exe it just built instead of a hand-copied payload.
        if (HasFlag(args, "silent"))
            return RunSilentInstall();

        // Must be set up BEFORE any code that references these assemblies is JIT-compiled.
        // Microsoft.Bcl.AsyncInterfaces is EXCLUDED from ILRepack (its IAsyncDisposable is
        // type-forwarded by net48), so we resolve it from an embedded copy instead.
        AppDomain.CurrentDomain.AssemblyResolve += OnAssemblyResolve;

        // Extract the native WebView2Loader.dll to temp and put it on the DLL search path
        // BEFORE any WebView2 code runs. Everything managed is ILRepacked into this EXE.
        try
        {
            string loaderDir = ExtractNativeDll("WebView2Loader.dll");
            SetDllDirectory(loaderDir);
        }
        catch { /* WebView2 init surfaces a friendly error if the runtime is truly missing */ }

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new InstallerForm());
        return 0;
    }

    /// <summary>Path where a silent install reports its outcome (a WinExe has no console to
    /// print to). One line per file plus a final "OK"/"PARCIAL"/"FALHOU" line.</summary>
    public static string SilentLogPath => Path.Combine(Path.GetTempPath(), "MantosExtract_Installer", "install-silent.log");

    /// <summary>Exit codes: 0 installed, 2 installed but some files failed, 1 not installed
    /// (Corel open, Corel missing, no space...). The reason is always in <see cref="SilentLogPath"/>.</summary>
    static int RunSilentInstall()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SilentLogPath)!);
        using var log = new StreamWriter(SilentLogPath, append: false);
        log.WriteLine(DateTime.Now.ToString("s") + " instalação silenciosa v" + Core.InstallerEngine.Version);
        try
        {
            var progress = new SyncProgress(p => log.WriteLine(p.Percent.ToString().PadLeft(3) + "% " + p.File));
            new Core.InstallerEngine(progress).Run();
            log.WriteLine("OK");
            return 0;
        }
        catch (Core.InstallPartialFailureException ex)
        {
            log.WriteLine("PARCIAL: " + ex.Message);
            return 2;
        }
        catch (Exception ex)
        {
            log.WriteLine("FALHOU: " + ex.Message);
            return 1;
        }
    }

    /// <summary><see cref="Progress{T}"/> posts to the thread pool, which would race the log
    /// writer's dispose; the silent install reports inline instead.</summary>
    sealed class SyncProgress : IProgress<Core.InstallProgress>
    {
        readonly Action<Core.InstallProgress> _report;
        public SyncProgress(Action<Core.InstallProgress> report) { _report = report; }
        public void Report(Core.InstallProgress value) => _report(value);
    }

    static bool HasFlag(string[] args, string name)
    {
        foreach (string a in args ?? Array.Empty<string>())
        {
            string t = a.TrimStart('-', '/').Trim().ToLowerInvariant();
            if (t == name) return true;
        }
        return false;
    }

    /// <summary>
    /// Removes the product and reports what happened. Deliberately a plain message box rather
    /// than the WebView2 wizard: an uninstall must not depend on the browser runtime still
    /// being present.
    /// </summary>
    static void RunUninstall(bool quiet)
    {
        var result = new Core.Uninstaller().Run();

        if (quiet) return;

        if (result.CorelWasOpen)
        {
            MessageBox.Show(
                "O CorelDRAW está aberto. Feche-o completamente e desinstale novamente.",
                "Mantos Extract", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        string detail = result.Failed.Count == 0
            ? "O Mantos Extract foi removido do CorelDRAW.\n\n"
            + "Suas fotos e artes não foram tocadas."
            : "O Mantos Extract foi removido, mas alguns itens resistiram:\n\n"
            + string.Join("\n", result.Failed);

        MessageBox.Show(detail, "Mantos Extract",
                        MessageBoxButtons.OK,
                        result.Failed.Count == 0 ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
    }

    // Fallback loader for the one assembly left out of ILRepack.
    static Assembly? OnAssemblyResolve(object? sender, ResolveEventArgs args)
    {
        string simpleName = new AssemblyName(args.Name).Name ?? "";
        return simpleName == "Microsoft.Bcl.AsyncInterfaces"
            ? LoadEmbeddedAssembly("Microsoft.Bcl.AsyncInterfaces.dll")
            : null;
    }

    static string ExtractNativeDll(string fileName)
    {
        string dir = Path.Combine(Path.GetTempPath(), "MantosExtract_Installer");
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, fileName);
        if (!File.Exists(path))
        {
            using var s = Assembly.GetExecutingAssembly().GetManifestResourceStream(fileName)
                ?? throw new InvalidOperationException($"Embedded resource not found: {fileName}");
            using var fs = File.Create(path);
            s.CopyTo(fs);
        }
        return dir;
    }

    static Assembly? LoadEmbeddedAssembly(string resourceName)
    {
        using var s = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName);
        if (s is null) return null;
        var bytes = new byte[s.Length];
        _ = s.Read(bytes, 0, bytes.Length);
        return Assembly.Load(bytes);
    }
}
