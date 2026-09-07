using System;
using System.IO;
using System.Text;

namespace MantosExtract.Windows
{
    /// <summary>
    /// Persists the operator's extraction quality preference (Dave, 2026-09-07 — the slider in
    /// Configurações) — a one-line text file under <c>%LOCALAPPDATA%\MantosExtract</c>, same
    /// shape as <see cref="LanguageStore"/> (a plain preference, not a secret, so no DPAPI:
    /// CLAUDE.md's distinction between simple prefs and SecureCredentialStore). Kept as its own
    /// small file rather than folding a second key into LanguageStore, which is deliberately
    /// single-purpose (its Read/Write both guard on LocalizationService.LanguagePrefKey).
    /// </summary>
    public static class ExtractionQualityStore
    {
        public const string Low = "low";
        public const string Medium = "medium";
        public const string High = "high";

        private static string Dir => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MantosExtract");

        private static string FilePath => Path.Combine(Dir, "extraction-quality.txt");

        /// <summary>Never throws, never returns something outside the allowed set — an
        /// unreadable or corrupted file degrades to the "medium" default, never to a value the
        /// server would reject with 422 E_INVALID_OPENAI_QUALITY.</summary>
        public static string Read()
        {
            try
            {
                string value = File.Exists(FilePath) ? File.ReadAllText(FilePath, Encoding.UTF8).Trim() : Medium;
                return value == Low || value == Medium || value == High ? value : Medium;
            }
            catch (Exception) { return Medium; }
        }

        public static void Write(string value)
        {
            if (value != Low && value != Medium && value != High) return;

            try
            {
                Directory.CreateDirectory(Dir);
                File.WriteAllText(FilePath, value, Encoding.UTF8);
            }
            catch (Exception) { /* an unwritable profile costs the preference, not the app */ }
        }
    }
}
