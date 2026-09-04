using System.Collections.Generic;
using System.Linq;

namespace MantosExtract.Core.I18n
{
    /// <summary>
    /// The UI string catalog in PT/ES/EN. pt-BR is authoritative (the language the product was
    /// designed in and the customer reads); ES/EN mirror it key by key —
    /// LocalizedStringsCoverageTests enforces that every pt key exists (explicitly, not by
    /// inheriting the fallback) in the other two. Single file for now (Fase 1 has one area,
    /// <c>me.*</c>); split into partial-class parts by area if it ever nears 500 lines, same
    /// convention as Optimus.Core.I18n.LocalizedStrings.
    /// </summary>
    public static class LocalizedStrings
    {
        public static readonly IReadOnlyList<Language> Languages = new[] { Language.Pt, Language.Es, Language.En };

        private static readonly Dictionary<string, string> Pt = Strings.Pt();
        private static readonly Dictionary<string, string> Es = Strings.Es();
        private static readonly Dictionary<string, string> En = Strings.En();

        private static Dictionary<string, string> For(Language language) => language switch
        {
            Language.Es => Es,
            Language.En => En,
            _ => Pt,
        };

        public static IReadOnlyCollection<string> Keys => Pt.Keys.ToList();

        /// <summary>Falls back to pt-BR and then to the key itself: a missing translation must
        /// show readable text, never an empty label.</summary>
        public static string Get(Language language, string key)
        {
            if (string.IsNullOrEmpty(key)) return "";
            if (For(language).TryGetValue(key, out string? value) && !string.IsNullOrEmpty(value))
                return value;
            return Pt.TryGetValue(key, out string? fallback) ? fallback : key;
        }

        /// <summary>True when <paramref name="language"/> declares <paramref name="key"/> itself
        /// rather than inheriting the pt-BR fallback — pt/es/en legitimately share some literal
        /// values ("Entrar"?), so comparing text would prove nothing.</summary>
        public static bool HasExplicit(Language language, string key) => For(language).ContainsKey(key);

        public static IReadOnlyDictionary<string, string> All(Language language)
        {
            var result = new Dictionary<string, string>(Pt.Count);
            foreach (string key in Pt.Keys) result[key] = Get(language, key);
            return result;
        }
    }
}
