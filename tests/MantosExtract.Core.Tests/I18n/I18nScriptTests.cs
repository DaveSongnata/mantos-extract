using MantosExtract.Core.I18n;
using Xunit;

namespace MantosExtract.Core.Tests.I18n
{
    public class I18nScriptTests
    {
        [Fact]
        public void Quote_EscapesScriptClosingTag_SoInjectedJsonCannotTerminateTheBlock()
        {
            string quoted = I18nScript.Quote("</script><script>alert(1)</script>");

            Assert.DoesNotContain("</script>", quoted);
            Assert.Contains("\\u003c", quoted);
        }

        [Fact]
        public void Quote_EscapesQuotesAndBackslashes()
        {
            string quoted = I18nScript.Quote("say \"hi\" \\ ok");

            Assert.Equal("\"say \\\"hi\\\" \\\\ ok\"", quoted);
        }

        [Fact]
        public void Build_ForPortuguese_SetsLangGlobalsAndIncludesKnownKey()
        {
            var localization = new LocalizationService();

            string script = I18nScript.Build(localization);

            Assert.Contains("window.MANTOSEXTRACT_LANG=\"pt-BR\"", script);
            Assert.Contains("window.MANTOSEXTRACT_LANG_KEY=\"pt\"", script);
            Assert.Contains("\"me.app.title\":\"Mantos Extract\"", script);
        }

        [Fact]
        public void Build_ForSpanish_UsesSpanishCatalog()
        {
            var localization = new LocalizationService();
            localization.SetLanguage(Language.Es);

            string script = I18nScript.Build(localization);

            Assert.Contains("window.MANTOSEXTRACT_LANG=\"es\"", script);
            Assert.Contains("\"me.login.submit\":\"Entrar\"", script);
        }
    }
}
