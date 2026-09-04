using System;
using MantosExtract.Core.Auth;
using Xunit;

namespace MantosExtract.Core.Tests.Auth
{
    public class LoginResponseTests
    {
        [Fact]
        public void Parse_TenantRole_ReadsSessionAndCredits()
        {
            string json = @"{
                ""sessionId"": ""a1b2c3d4-e5f6-7890-abcd-ef1234567890"",
                ""expiresAt"": ""2026-09-04T12:00:00.000Z"",
                ""role"": ""tenant"",
                ""subscriptionExpired"": false,
                ""needsTermsAcceptance"": false,
                ""termsUrl"": ""/privacidade"",
                ""user"": { ""id"": 1, ""name"": ""Confecção Teste"", ""email"": ""dono@confeccao.com"",
                            ""companyName"": ""Confecção Teste Ltda"", ""creditsRemaining"": 42,
                            ""plan"": { ""id"": 1, ""name"": ""Pro"", ""slug"": ""pro"", ""creditsPerMonth"": 200 } }
            }";

            LoginResponse result = LoginResponse.Parse(json);

            Assert.Equal("a1b2c3d4-e5f6-7890-abcd-ef1234567890", result.SessionId);
            Assert.Equal("tenant", result.Role);
            Assert.True(result.IsSupportedRole);
            Assert.Equal(42, result.CreditsRemaining);
            Assert.Equal("Confecção Teste Ltda", result.CompanyName);
            Assert.Equal(new DateTimeOffset(2026, 9, 4, 12, 0, 0, TimeSpan.Zero), result.ExpiresAt);
        }

        [Fact]
        public void Parse_SubTenantRole_IsSupported()
        {
            string json = @"{""sessionId"":""s1"",""expiresAt"":""2026-09-04T12:00:00.000Z"",""role"":""sub_tenant"",
                              ""user"":{""creditsRemaining"":5}}";

            LoginResponse result = LoginResponse.Parse(json);

            Assert.True(result.IsSupportedRole);
            Assert.Equal(5, result.CreditsRemaining);
        }

        [Fact]
        public void Parse_AdminRole_ParsesButIsUnsupported()
        {
            // Real shape: unified login answers admin credentials with NO "user" field at all
            // (creator_auth_controller.ts) — must not throw just because "user" is absent.
            string json = @"{""sessionId"":""s1"",""expiresAt"":""2026-09-04T12:00:00.000Z"",""role"":""admin""}";

            LoginResponse result = LoginResponse.Parse(json);

            Assert.False(result.IsSupportedRole);
            Assert.Equal(0, result.CreditsRemaining);
        }

        [Fact]
        public void Parse_DesignerRole_ParsesButIsUnsupported()
        {
            // Real shape: designer login answers with a "designer" object, not "user".
            string json = @"{""sessionId"":""s1"",""expiresAt"":""2026-09-04T12:00:00.000Z"",""role"":""designer"",
                              ""designer"":{""id"":9,""slug"":""ana"",""displayName"":""Ana""}}";

            LoginResponse result = LoginResponse.Parse(json);

            Assert.False(result.IsSupportedRole);
        }

        [Theory]
        [InlineData("not json at all")]
        [InlineData(@"{""role"":""tenant""}")] // missing sessionId
        [InlineData(@"{""sessionId"":""s1"",""role"":""tenant""}")] // missing expiresAt
        [InlineData(@"{""sessionId"":""s1"",""expiresAt"":""not-a-date"",""role"":""tenant""}")]
        public void Parse_MalformedBody_ThrowsMalformedResponse(string json)
        {
            MantosfcAuthException ex = Assert.Throws<MantosfcAuthException>(() => LoginResponse.Parse(json));
            Assert.Equal("E_MALFORMED_RESPONSE", ex.Code);
        }
    }
}
