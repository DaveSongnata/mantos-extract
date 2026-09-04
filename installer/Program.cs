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
    static void Main(string[] args)
    {
        // "Apps & features" invokes the kept copy of this EXE with /uninstall. Handled before
        // anything WebView2-related is touched: the uninstall path must work even on a machine
        // whose WebView2 runtime was removed after installation.
        if (HasFlag(args, "uninstall"))
        {
            RunUninstall(quiet: HasFlag(args, "quiet"));
            return;
        }

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
