using MantosExtract.Core.Auth;
using Xunit;

namespace MantosExtract.Core.Tests.Auth
{
    public class MeResponseTests
    {
        [Fact]
        public void Parse_ReadsCreditsAndBlockedFlag()
        {
            MeResponse result = MeResponse.Parse(@"{""creditsRemaining"":15,""isBlocked"":false,""role"":""tenant""}");

            Assert.Equal(15, result.CreditsRemaining);
            Assert.False(result.IsBlocked);
        }

        [Fact]
        public void Parse_MissingFields_DefaultsRatherThanThrows()
        {
            MeResponse result = MeResponse.Parse(@"{}");

            Assert.Equal(0, result.CreditsRemaining);
            Assert.False(result.IsBlocked);
        }

        [Fact]
        public void Parse_MalformedJson_ThrowsMalformedResponse()
        {
            MantosfcAuthException ex = Assert.Throws<MantosfcAuthException>(() => MeResponse.Parse("not json"));
            Assert.Equal("E_MALFORMED_RESPONSE", ex.Code);
        }
    }
}
