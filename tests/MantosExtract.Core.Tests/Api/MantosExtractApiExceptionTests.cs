using MantosExtract.Core.Api;
using Xunit;

namespace MantosExtract.Core.Tests.Api
{
    public class MantosExtractApiExceptionTests
    {
        [Theory]
        [InlineData("E_OPENAI_NO_CREDIT")]
        [InlineData("E_OPENAI_SPEND_LIMIT")]
        public void QuotaCodes_AreRecognised(string code)
        {
            Assert.True(new MantosExtractApiException(code, "msg").IsOpenAiQuotaExhausted);
        }

        [Theory]
        [InlineData("E_UNAUTHORIZED")]
        [InlineData("E_NETWORK")]
        [InlineData("E_RATE_LIMIT")]
        [InlineData("E_UNKNOWN")]
        [InlineData("")]
        public void OtherCodes_DoNotStopTheBatch(string code)
        {
            Assert.False(new MantosExtractApiException(code, "msg").IsOpenAiQuotaExhausted);
        }

        [Fact]
        public void NullCode_IsNotQuota()
        {
            Assert.False(MantosExtractApiException.IsOpenAiQuotaCode(null));
        }

        [Fact]
        public void ModelUnavailable_StopsTheBatchButIsNotQuota()
        {
            var ex = new MantosExtractApiException("E_OPENAI_MODEL_UNAVAILABLE", "msg");

            Assert.True(ex.IsOpenAiModelUnavailable);
            Assert.True(ex.StopsBatch);
            Assert.False(ex.IsOpenAiQuotaExhausted);
        }

        [Theory]
        [InlineData("E_OPENAI_NO_CREDIT")]
        [InlineData("E_OPENAI_SPEND_LIMIT")]
        public void QuotaCodes_AlsoStopTheBatch(string code)
        {
            Assert.True(new MantosExtractApiException(code, "msg").StopsBatch);
        }

        [Theory]
        [InlineData("E_OPENAI_MODERATION")]
        [InlineData("E_NETWORK")]
        [InlineData("E_UNKNOWN")]
        public void OrdinaryFailures_DoNotStopTheBatch(string code)
        {
            Assert.False(new MantosExtractApiException(code, "msg").StopsBatch);
        }
    }
}