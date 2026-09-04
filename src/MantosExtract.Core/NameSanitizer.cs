using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace MantosExtract.Core
{
    /// <summary>
    /// Turns a detected element's label (or the operator's own text) into a name safe for both
    /// a Corel shape/layer name AND a Windows filename — docs/mantos-extract-spec.md §6:
    /// "Nome de elemento ou de cliente com caractere especial (comum, tipo 'José &amp; Cia')".
    /// One sanitizer for both uses on purpose: the extracted PNG's temp filename and the
    /// imported shape's Corel name are always derived from the same label, so they stay
    /// recognizably paired in docker.log without needing two separate rules.
    /// </summary>
    public static class NameSanitizer
    {
        private static readonly char[] Invalid = System.IO.Path.GetInvalidFileNameChars();

        public static string Sanitize(string? label, string fallback = "elemento")
        {
            if (string.IsNullOrWhiteSpace(label)) return fallback;

            var sb = new StringBuilder(label!.Length);
            foreach (char c in label!.Trim())
            {
                if (Invalid.Contains(c)) continue;
                // Windows filenames also reject these even though GetInvalidFileNameChars()
                // does not always list all of them consistently across .NET versions.
                if (c == ':' || c == '*' || c == '?' || c == '"' || c == '<' || c == '>' || c == '|') continue;
                sb.Append(c);
            }

            string cleaned = CollapseWhitespace(sb.ToString()).Trim();
            if (string.IsNullOrEmpty(cleaned)) return fallback;

            // Windows reserves trailing dots/spaces and a handful of device names.
            cleaned = cleaned.TrimEnd('.', ' ');
            if (string.IsNullOrEmpty(cleaned)) return fallback;
            if (ReservedDeviceNames.Contains(cleaned.ToUpperInvariant())) return fallback + "_" + cleaned;

            return cleaned.Length > 80 ? cleaned.Substring(0, 80).TrimEnd() : cleaned;
        }

        /// <summary>Appends a disambiguator (e.g. an index) so a batch of same-labelled
        /// elements ("logo", "logo") never collide on disk or in the Corel object list.</summary>
        public static string WithSuffix(string sanitizedBase, int index) => $"{sanitizedBase}_{index}";

        private static string CollapseWhitespace(string s)
        {
            var sb = new StringBuilder(s.Length);
            bool lastWasSpace = false;
            foreach (char c in s)
            {
                bool isSpace = char.IsWhiteSpace(c);
                if (isSpace && lastWasSpace) continue;
                sb.Append(isSpace ? ' ' : c);
                lastWasSpace = isSpace;
            }
            return sb.ToString();
        }

        private static readonly HashSet<string> ReservedDeviceNames = new HashSet<string>
        {
            "CON", "PRN", "AUX", "NUL",
            "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
            "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
        };
    }
}
