using MantosExtract.Core.I18n;
using Xunit;

namespace MantosExtract.Core.Tests.I18n
{
    /// <summary>Enforces that every pt-BR key is explicitly declared (not silently inherited
    /// via the pt-BR fallback) in ES and EN — a string added in one language only must fail the
    /// build, not ship a half-translated screen. Same guard Optimus.Core.I18n carries
    /// (O26/UiKeyCoverageTests lesson: a key built by string concatenation in the HTML would be
    /// invisible to this test too — none of ours are, but keep data-i18n keys literal in the
    /// HTML if that ever changes).</summary>
    public class LocalizedStringsCoverageTests
    {
        [Theory]
        [MemberData(nameof(PtKeys))]
        public void EveryPtKey_HasExplicitSpanish(string key) =>
            Assert.True(LocalizedStrings.HasExplicit(Language.Es, key), $"chave '{key}' ausente em ES");

        [Theory]
        [MemberData(nameof(PtKeys))]
        public void EveryPtKey_HasExplicitEnglish(string key) =>
            Assert.True(LocalizedStrings.HasExplicit(Language.En, key), $"chave '{key}' ausente em EN");

        [Theory]
        [MemberData(nameof(PtKeys))]
        public void NoLanguage_HasEmptyValue(string key)
        {
            foreach (Language language in LocalizedStrings.Languages)
                Assert.False(string.IsNullOrWhiteSpace(LocalizedStrings.Get(language, key)),
                    $"chave '{key}' vazia em {language}");
        }

        public static TheoryData<string> PtKeys()
        {
            var data = new TheoryData<string>();
            foreach (string key in LocalizedStrings.Keys) data.Add(key);
            return data;
        }
    }
}
