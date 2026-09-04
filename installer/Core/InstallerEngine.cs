using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;

namespace MantosExtract.Installer.Core;

/// <summary>The add-in itself installed, but one or more payload files could not be copied —
/// distinct from a hard failure so the message can carry the REAL per-file reason (disk space,
/// access denied, file in use...) instead of a single guessed cause. Same shape as
/// ../optimus/installer/Core/InstallerEngine.cs.</summary>
public sealed class InstallPartialFailureException : Exception
{
    public InstallPartialFailureException(string message) : base(message) { }
}

public sealed class InstallProgress
{
    public string File { get; }
    public int Percent { get; }
    public InstallProgress(string file, int percent) { File = file; Percent = percent; }
}

/// <summary>
/// Deploys the Mantos Extract add-on to every installed CorelDRAW's
/// <c>Programs64\Addons\MantosExtract\</c>: MantosExtract.AddIn.dll + all loose deps +
/// MantosExtract.Resources.dll + WebView2Loader.dll + the addon manifest (Coreldrw.addon
/// marker, config.xml, AppUI/UserUI.xslt) + the upscale binary (if bundled), so the addon
/// auto-loads with no manual steps. The whole payload is embedded as "addon.*" resources in
/// the installer EXE.
/// </summary>
public class InstallerEngine
{
    private readonly IProgress<InstallProgress> _progress;

    public InstallerEngine(IProgress<InstallProgress> progress)
    {
        _progress = progress;
    }

    /// <summary>Home directory for the installer's own leftovers (kept uninstaller copy,
    /// THIRD-PARTY-NOTICES.txt). Not under the CorelDRAW addon folder — that one gets wiped
    /// and recreated on every install/uninstall.</summary>
    public static string HomeDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "MantosExtract");

    public void Run()
    {
        // CorelDRAW loads MantosExtract.AddIn.dll in-process; that lock turns File.Create into
        // an ugly IOException on a re-install, so stop early with a clear message instead.
        EnsureCorelClosed();

        var asm = Assembly.GetExecutingAssembly();
        var addonRes = asm.GetManifestResourceNames()
            .Where(n => n.StartsWith("addon.", StringComparison.Ordinal))
            .OrderBy(n => n).ToArray();

        if (addonRes.Length == 0)
            throw new InvalidOperationException("Pacote do instalador incompleto (payload do addon ausente).");

        var addonDirs = FindCorelAddonDirs();
        if (addonDirs.Count == 0)
            throw new InvalidOperationException(
                "CorelDRAW não foi encontrado em C:\\Program Files\\Corel\\. Instale o CorelDRAW antes do Mantos Extract.");

        CheckFreeSpace(addonDirs);

        int total = addonRes.Length * addonDirs.Count;
        int done = 0;

        // Delete-then-copy: wipe the old folder first so a stale DLL/XSLT can never linger and
        // get loaded instead of the new one. Safe because EnsureCorelClosed ran.
        //
        // Each file is extracted independently (try/catch INSIDE the loop, not around it): one
        // resource failing must not silently abort every file that would have been copied after
        // it. The install still completes; what failed is collected with its ACTUAL exception,
        // never swallowed and never guessed at.
        var failures = new List<string>();
        foreach (string dir in addonDirs)
        {
            try { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); } catch { }
            Directory.CreateDirectory(dir);
            foreach (string res in addonRes)
            {
                try { ExtractOne(asm, res, "addon.", dir, total, ref done); }
                catch (Exception ex)
                {
                    done++;
                    failures.Add($"{res.Substring("addon.".Length)} ({dir}): {ex.Message}");
                }
            }
        }

        WriteThirdPartyNotices(asm);
        Uninstaller.RegisterUninstallEntry(Version);

        if (failures.Count > 0)
            throw new InstallPartialFailureException(
                $"O Mantos Extract foi instalado, mas {failures.Count} arquivo(s) não puderam ser copiados: " +
                string.Join(" | ", failures));
    }

    /// <summary>
    /// Fails fast, before copying a single byte, when the target drive doesn't have room for the
    /// payload — a plain "sem espaço em disco" is a far more likely explanation for a large file
    /// (o binário de upscale, quando presente) silently failing do que qualquer coisa exótica.
    /// </summary>
    private static void CheckFreeSpace(List<string> addonDirs)
    {
        const long requiredBytes = 150L * 1024 * 1024; // payload de hoje (~1 MB) + margem
                                                          // generosa pro binário de upscale
                                                          // (Real-ESRGAN NCNN-Vulkan + modelo,
                                                          // quando bundlado) x algumas versões
                                                          // de Corel.
        try
        {
            string drive = Path.GetPathRoot(addonDirs[0]) ?? "C:\\";
            var info = new DriveInfo(drive);
            if (info.AvailableFreeSpace < requiredBytes)
                throw new InvalidOperationException(
                    $"Pouco espaço livre em {drive} ({info.AvailableFreeSpace / (1024 * 1024)} MB). " +
                    $"O Mantos Extract precisa de pelo menos {requiredBytes / (1024 * 1024)} MB livres para instalar. " +
                    "Libere espaço e instale de novo.");
        }
        catch (InvalidOperationException) { throw; }
        catch (Exception) { /* couldn't even check free space — let the real copy attempt speak instead */ }
    }

    /// <summary>
    /// Version stamped into the "Apps &amp; features" entry. Read from this assembly, which
    /// <c>scripts/build-all.ps1</c> stamps from <c>Build.cs</c> — so the number the customer
    /// sees in Windows can never drift from the build they were actually given.
    /// </summary>
    public static string Version
    {
        get
        {
            try
            {
                Version? v = Assembly.GetExecutingAssembly().GetName().Version;
                return v == null ? "0.1.0" : v.Major + "." + v.Minor + "." + v.Build;
            }
            catch { return "0.1.0"; }
        }
    }

    /// <summary>
    /// Ships <c>THIRD-PARTY-NOTICES.txt</c> next to the installed app. Licence attribution has
    /// to travel with the binaries, not live only in the repository — that is the whole point
    /// of the obligation.
    /// </summary>
    private static void WriteThirdPartyNotices(Assembly asm)
    {
        try
        {
            using Stream? s = asm.GetManifestResourceStream("notices.THIRD-PARTY-NOTICES.txt");
            if (s == null) return;

            Directory.CreateDirectory(HomeDir);
            using FileStream fs = File.Create(Path.Combine(HomeDir, "THIRD-PARTY-NOTICES.txt"));
            s.CopyTo(fs);
        }
        catch { /* attribution file missing is not a reason to fail an install already completed */ }
    }

    private void ExtractOne(Assembly asm, string resourceName, string prefix, string destDir, int total, ref int done)
    {
        // Most payload files are flat, but the upscale binary (if bundled) sits under an
        // "upscale/" subfolder — encoded here with '/' in the LogicalName and reconstructed as
        // a real subdirectory on extraction (same trick as Optimus's Whisper native DLLs).
        string fileName = resourceName.Substring(prefix.Length).Replace('/', Path.DirectorySeparatorChar);
        string destPath = Path.Combine(destDir, fileName);
        string? destSubDir = Path.GetDirectoryName(destPath);
        if (!string.IsNullOrEmpty(destSubDir)) Directory.CreateDirectory(destSubDir);

        long expectedLength;
        using (var stream = asm.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Recurso ausente: {resourceName}"))
        using (var fs = File.Create(destPath))
        {
            expectedLength = stream.Length;
            stream.CopyTo(fs);
        }

        // A silently truncated copy is worse than a thrown exception: the file LOOKS installed,
        // and whatever depends on it fails much later with no obvious cause. Verified on disk,
        // not just trusted because CopyTo returned.
        long actualLength = new FileInfo(destPath).Length;
        if (actualLength != expectedLength)
            throw new IOException($"cópia incompleta: {actualLength} de {expectedLength} bytes");

        // Strip any "Mark of the Web" so the .NET loader inside CorelDRAW never refuses a DLL
        // with the loadFromRemoteSources / 0x80131515 error.
        ClearMarkOfTheWeb(destPath);

        done++;
        _progress.Report(new InstallProgress(fileName, total == 0 ? 100 : done * 100 / total));
    }

    /// <summary>Finds every <c>…\Corel\CorelDRAW Graphics Suite\&lt;version&gt;\Programs64\Addons</c>
    /// and returns the MantosExtract subfolder under each (the real addon location).</summary>
    private static List<string> FindCorelAddonDirs()
    {
        var result = new List<string>();
        string suiteRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            "Corel", "CorelDRAW Graphics Suite");
        if (!Directory.Exists(suiteRoot)) return result;

        foreach (string versionDir in Directory.GetDirectories(suiteRoot))
        {
            string addons = Path.Combine(versionDir, "Programs64", "Addons");
            if (Directory.Exists(addons))
                result.Add(Path.Combine(addons, "MantosExtract"));
        }
        return result;
    }

    // CorelDRAW's process is CorelDRW.exe → process name "CorelDRW".
    private static void EnsureCorelClosed()
    {
        bool running;
        try { running = Process.GetProcessesByName("CorelDRW").Length > 0; }
        catch { running = false; }

        if (running)
            throw new InvalidOperationException(
                "O CorelDRAW está aberto. Feche-o completamente e clique em Instalar novamente.");
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool DeleteFile(string lpFileName);

    private static void ClearMarkOfTheWeb(string path)
    {
        try { DeleteFile(path + ":Zone.Identifier"); }
        catch { /* no ADS / not supported → nothing to clear */ }
    }
}
