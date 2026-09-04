using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace MantosExtract.Installer.Core;

public sealed class UninstallResult
{
    public List<string> Removed { get; } = new();
    public List<string> Failed { get; } = new();
    public bool CorelWasOpen { get; set; }

    public bool Success => !CorelWasOpen && Failed.Count == 0;
}

/// <summary>
/// Removes everything the installer put on the machine: the add-on folder inside every
/// CorelDRAW and the "Apps &amp; features" entry. Deliberately does NOT touch the operator's
/// language preference / session under <c>%LOCALAPPDATA%\MantosExtract</c> nor, above all, any
/// <c>.cdr</c> file anywhere.
/// </summary>
public sealed class Uninstaller
{
    /// <summary>Where the Uninstall entry lives, so Windows can offer the removal itself.</summary>
    public const string UninstallKey =
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\MantosExtract";

    public UninstallResult Run()
    {
        var result = new UninstallResult();

        // The add-in DLL is loaded in-process by CorelDRAW; deleting it while Corel runs fails
        // with a sharing violation and leaves a half-removed install.
        try { result.CorelWasOpen = Process.GetProcessesByName("CorelDRW").Length > 0; }
        catch { result.CorelWasOpen = false; }
        if (result.CorelWasOpen) return result;

        foreach (string dir in FindAddonDirs())
            Delete(dir, result);

        Delete(InstallerEngine.HomeDir, result);

        RemoveUninstallEntry(result);

        return result;
    }

    private static void Delete(string dir, UninstallResult result)
    {
        try
        {
            if (!Directory.Exists(dir)) return;
            Directory.Delete(dir, recursive: true);
            result.Removed.Add(dir);
        }
        catch (Exception ex)
        {
            result.Failed.Add(dir + " — " + ex.Message);
        }
    }

    private static void RemoveUninstallEntry(UninstallResult result)
    {
        try
        {
            using RegistryKey baseKey = RegistryKey.OpenBaseKey(
                RegistryHive.LocalMachine, RegistryView.Registry64);
            using RegistryKey? parent = baseKey.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall", writable: true);

            if (parent?.OpenSubKey("MantosExtract") == null) return;

            parent.DeleteSubKeyTree("MantosExtract", throwOnMissingSubKey: false);
            result.Removed.Add("HKLM\\" + UninstallKey);
        }
        catch (Exception ex) { result.Failed.Add("registro de desinstalação — " + ex.Message); }
    }

    /// <summary>Every <c>…\Addons\MantosExtract</c> folder currently on the machine.</summary>
    private static List<string> FindAddonDirs()
    {
        var result = new List<string>();
        try
        {
            string suiteRoot = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                "Corel", "CorelDRAW Graphics Suite");
            if (!Directory.Exists(suiteRoot)) return result;

            foreach (string versionDir in Directory.GetDirectories(suiteRoot))
            {
                string dir = Path.Combine(versionDir, "Programs64", "Addons", "MantosExtract");
                if (Directory.Exists(dir)) result.Add(dir);
            }
        }
        catch { }
        return result;
    }

    /// <summary>
    /// Registers the product in "Apps &amp; features" so it can be uninstalled the way Windows
    /// expects. A copy of the installer is kept beside the app to serve as the uninstaller — the
    /// customer must not have to hunt for the original download months later.
    /// </summary>
    public static void RegisterUninstallEntry(string version)
    {
        try
        {
            Directory.CreateDirectory(InstallerEngine.HomeDir);

            string self = Process.GetCurrentProcess().MainModule?.FileName ?? "";
            string kept = Path.Combine(InstallerEngine.HomeDir, "MantosExtract_Setup.exe");

            if (!string.IsNullOrEmpty(self) &&
                !self.Equals(kept, StringComparison.OrdinalIgnoreCase))
                File.Copy(self, kept, overwrite: true);

            using RegistryKey baseKey = RegistryKey.OpenBaseKey(
                RegistryHive.LocalMachine, RegistryView.Registry64);
            using RegistryKey key = baseKey.CreateSubKey(UninstallKey);
            if (key == null) return;

            key.SetValue("DisplayName", "Mantos Extract — Extração de Estampa CorelDRAW");
            key.SetValue("DisplayVersion", version);
            key.SetValue("Publisher", "Aisten Lab Technology");
            key.SetValue("InstallLocation", InstallerEngine.HomeDir);
            key.SetValue("UninstallString", "\"" + kept + "\" /uninstall");
            key.SetValue("QuietUninstallString", "\"" + kept + "\" /uninstall /quiet");
            key.SetValue("NoModify", 1, RegistryValueKind.DWord);
            key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
            key.SetValue("EstimatedSize", 2_000, RegistryValueKind.DWord);   // KB, approximate
        }
        catch
        {
            // No uninstall entry is a cosmetic loss; failing the install over it would not be.
        }
    }
}
