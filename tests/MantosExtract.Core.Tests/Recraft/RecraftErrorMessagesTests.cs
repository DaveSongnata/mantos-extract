using MantosExtract.Core.I18n;
using MantosExtract.Core.Recraft;
using Xunit;

namespace MantosExtract.Core.Tests.Recraft
{
    public class RecraftErrorMessagesTests
    {
        [Theory]
        [InlineData("E_MISSING_RECRAFT_KEY", "me.recraft.error.noKey")]
        [InlineData("E_RECRAFT_INVALID_KEY", "me.recraft.error.invalidKey")]
        [InlineData("E_RECRAFT_NO_CREDIT", "me.recraft.error.noCredit")]
        [InlineData("E_RECRAFT_RATE_LIMIT", "me.recraft.error.rateLimit")]
        [InlineData("E_RECRAFT_UNAVAILABLE", "me.recraft.error.unavailable")]
        [InlineData("E_RECRAFT_BAD_RESPONSE", "me.recraft.error.unavailable")]
        [InlineData("E_RECRAFT_TIMEOUT", "me.recraft.error.timeout")]
        [InlineData("E_RECRAFT_BAD_INPUT", "me.recraft.error.badInput")]
        [InlineData("E_NETWORK", "me.recraft.error.network")]
        [InlineData("E_DOWNLOAD_FAILED", "me.recraft.error.download")]
        public void KeyFor_MapsEveryKnownCodeToAnI18nKey(string code, string expectedKey)
        {
            Assert.Equal(expectedKey, RecraftErrorMessages.KeyFor(code));
        }

        [Theory]
        [InlineData("E_UNKNOWN")]
        [InlineData("E_IMAGE_TOO_LARGE")]
        [InlineData("")]
        [InlineData(null)]
        public void KeyFor_ReturnsNullForCodesItDoesNotOwn_SoTheServerMessageIsShown(string? code)
        {
            Assert.Null(RecraftErrorMessages.KeyFor(code));
        }

        [Fact]
        public void EveryMappedKey_ExistsInAllThreeLanguages()
        {
            foreach (string code in RecraftErrorMessages.KnownCodes)
            {
                string? key = RecraftErrorMessages.KeyFor(code);
                Assert.NotNull(key);
                foreach (Language language in LocalizedStrings.Languages)
                    Assert.True(LocalizedStrings.HasExplicit(language, key!), $"{key} ausente em {language}");
            }
        }
    }
}