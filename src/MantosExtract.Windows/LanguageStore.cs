using System;
using System.IO;
using System.Text;
using MantosExtract.Core.I18n;

namespace MantosExtract.Windows
{
    /// <summary>
    /// Persists the operator's language choice — a one-line text file under
    /// <c>%LOCALAPPDATA%\MantosExtract</c>, deliberately not the registry and not a settings
    /// framework, same shape as Optimus.Windows.LanguageStore (reimplemented here, not shared —
    /// each addin owns its copy). Both operations are non-throwing: a locked-down profile falls
    /// back to pt-BR rather than refusing to open the docker.
    /// </summary>
    public static class LanguageStore
    {
        private static string Dir => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MantosExtract");

        private static string FilePath => Path.Combine(Dir, "ui-language.txt");

        public static string? Read(string key)
        {
            if (key != LocalizationService.LanguagePrefKey) return null;

            try
            {
                return File.Exists(FilePath) ? File.ReadAllText(FilePath, Encoding.UTF8).Trim() : null;
            }
            catch (Exception) { return null; }
        }

        public static void Write(string key, string value)
        {
            if (key != LocalizationService.LanguagePrefKey) return;

            try
            {
                Directory.CreateDirectory(Dir);
                File.WriteAllText(FilePath, value ?? "", Encoding.UTF8);
            }
            catch (Exception) { /* an unwritable profile costs the preference, not the app */ }
        }

        public static LocalizationService Service() => LocalizationService.Load(Read, Write);
    }
}
