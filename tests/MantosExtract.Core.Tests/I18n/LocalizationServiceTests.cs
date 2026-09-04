using System.Collections.Generic;
using MantosExtract.Core.I18n;
using Xunit;

namespace MantosExtract.Core.Tests.I18n
{
    public class LocalizationServiceTests
    {
        [Theory]
        [InlineData("es", Language.Es)]
        [InlineData("es-AR", Language.Es)]
        [InlineData("en", Language.En)]
        [InlineData("en-US", Language.En)]
        [InlineData("pt", Language.Pt)]
        [InlineData("pt-BR", Language.Pt)]
        [InlineData(null, Language.Pt)]
        [InlineData("", Language.Pt)]
        [InlineData("xx-unknown", Language.Pt)]
        public void Parse_AcceptsWhatTheUiSends_AndDefaultsToPortuguese(string? tag, Language expected) =>
            Assert.Equal(expected, LocalizationService.Parse(tag));

        [Fact]
        public void SetLanguage_PersistsThroughInjectedStore()
        {
            var store = new Dictionary<string, string>();
            var service = new LocalizationService((key, value) => store[key] = value);

            service.SetLanguage("es");

            Assert.Equal(Language.Es, service.Current);
            Assert.Equal("Es", store[LocalizationService.LanguagePrefKey]);
        }

        [Fact]
        public void Load_ReadsPersistedChoice()
        {
            var service = LocalizationService.Load(key => key == LocalizationService.LanguagePrefKey ? "En" : null);

            Assert.Equal(Language.En, service.Current);
        }

        [Fact]
        public void Load_NothingPersistedYet_DefaultsToPortuguese()
        {
            var service = LocalizationService.Load(_ => null);

            Assert.Equal(Language.Pt, service.Current);
        }
    }
}
