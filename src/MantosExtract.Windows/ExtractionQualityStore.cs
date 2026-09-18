using System;
using System.IO;
using System.Text;
using MantosExtract.Core.Extract;

namespace MantosExtract.Windows
{
    /// <summary>
    /// Persists the operator's extraction preset (low..max, Dave 2026-09-07/2026-09-18 — the slider in
    /// Configurações) — a one-line text file under <c>%LOCALAPPDATA%\MantosExtract</c>, same
    /// shape as <see cref="LanguageStore"/> (a plain preference, not a secret, so no DPAPI:
    /// CLAUDE.md's distinction between simple prefs and SecureCredentialStore). Kept as its own
    /// small file rather than folding a second key into LanguageStore, which is deliberately
    /// single-purpose (its Read/Write both guard on LocalizationService.LanguagePrefKey).
    /// </summary>
    public static class ExtractionQualityStore
    {
        private static string Dir => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MantosExtract");

        private static string FilePath => Path.Combine(Dir, "extraction-quality.txt");

        /// <summary>Never throws, never returns something outside the allowed set — an
        /// unreadable or corrupted file degrades to the "medium" default, never to a value the
        /// server would reject with 422 E_INVALID_EXTRACT_PRESET.</summary>
        public static string Read()
        {
            try
            {
                string value = File.Exists(FilePath) ? File.ReadAllText(FilePath, Encoding.UTF8).Trim() : ExtractionPresets.Default;
                return ExtractionPresets.Normalize(value);
            }
            catch (Exception) { return ExtractionPresets.Default; }
        }

        public static void Write(string value)
        {
            if (!ExtractionPresets.IsValid(value)) return;

            try
            {
                Directory.CreateDirectory(Dir);
                File.WriteAllText(FilePath, value, Encoding.UTF8);
            }
            catch (Exception) { /* an unwritable profile costs the preference, not the app */ }
        }
    }
}
