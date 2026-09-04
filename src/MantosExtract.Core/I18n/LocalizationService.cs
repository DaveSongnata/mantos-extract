using System;
using System.Collections.Generic;

namespace MantosExtract.Core.I18n
{
    public enum Language { Pt, Es, En }

    /// <summary>
    /// Resolves UI strings for the current language and persists the choice. Persistence is
    /// injected (no SQLite, no settings service in this product — same shape as
    /// Optimus.Core.I18n.LocalizationService, reimplemented here rather than shared, per repo
    /// convention: each addin owns its copy). Default is pt-BR: the customer is Brazilian, and
    /// a wrong default is worse than a missing language picker.
    /// </summary>
    public sealed class LocalizationService
    {
        public const string LanguagePrefKey = "ui.language";

        private readonly Action<string, string>? _persist;

        public LocalizationService(Action<string, string>? persist = null)
        {
            _persist = persist;
        }

        public Language Current { get; private set; } = Language.Pt;

        public string this[string key] => LocalizedStrings.Get(Current, key);

        public void SetLanguage(Language language)
        {
            Current = language;
            _persist?.Invoke(LanguagePrefKey, language.ToString());
        }

        /// <summary>Accepts what a UI actually sends ("pt", "pt-BR", "es", "en-US") and falls
        /// back to pt-BR rather than throwing — an unknown tag must never leave the operator
        /// with a blank interface.</summary>
        public void SetLanguage(string tag) => SetLanguage(Parse(tag));

        public static Language Parse(string? tag)
        {
            if (string.IsNullOrWhiteSpace(tag)) return Language.Pt;

            string t = tag!.Trim().ToLowerInvariant();
            if (t.StartsWith("es", StringComparison.Ordinal)) return Language.Es;
            if (t.StartsWith("en", StringComparison.Ordinal)) return Language.En;
            return Language.Pt;
        }

        public static string TagOf(Language language) => language switch
        {
            Language.Es => "es",
            Language.En => "en",
            _ => "pt-BR",
        };

        public static LocalizationService Load(Func<string, string?> read, Action<string, string>? persist = null)
        {
            var service = new LocalizationService(persist);
            if (read != null) service.Current = Parse(read(LanguagePrefKey));
            return service;
        }

        /// <summary>The whole catalog for the current language — the HTML holds no text of its
        /// own, so this is what makes it translatable at all.</summary>
        public IReadOnlyDictionary<string, string> Dictionary() => LocalizedStrings.All(Current);
    }
}
