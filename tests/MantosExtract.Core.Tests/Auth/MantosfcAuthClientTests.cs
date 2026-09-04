using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using MantosExtract.Core.Auth;
using Xunit;

namespace MantosExtract.Core.Tests.Auth
{
    public class MantosfcAuthClientTests
    {
        [Fact]
        public async Task LoginAsync_Success_ReturnsParsedResponse()
        {
            var handler = StubHttpMessageHandler.Json(HttpStatusCode.OK,
                @"{""sessionId"":""s1"",""expiresAt"":""2026-09-04T12:00:00.000Z"",""role"":""tenant"",
                   ""user"":{""creditsRemaining"":42}}");
            var client = new MantosfcAuthClient(handler, new Uri("https://mantosfc.test"));

            LoginResponse result = await client.LoginAsync("dono@confeccao.com", "senha123", CancellationToken.None);

            Assert.Equal("s1", result.SessionId);
            Assert.Equal(42, result.CreditsRemaining);
            Assert.Contains("/api/v1/auth/login", handler.LastRequest!.RequestUri!.PathAndQuery);
            Assert.DoesNotContain("dono@confeccao.com", handler.LastRequest.RequestUri.ToString()); // never in the URL
            Assert.Contains("dono@confeccao.com", handler.LastRequestBody);
        }

        [Fact]
        public async Task LoginAsync_UnsupportedRole_ThrowsWithoutHittingAnotherEndpoint()
        {
            var handler = StubHttpMessageHandler.Json(HttpStatusCode.OK,
                @"{""sessionId"":""s1"",""expiresAt"":""2026-09-04T12:00:00.000Z"",""role"":""admin""}");
            var client = new MantosfcAuthClient(handler, new Uri("https://mantosfc.test"));

            MantosfcAuthException ex = await Assert.ThrowsAsync<MantosfcAuthException>(
                () => client.LoginAsync("admin@mantosfc.com", "x", CancellationToken.None));

            Assert.Equal("E_UNSUPPORTED_ROLE", ex.Code);
        }

        [Theory]
        [InlineData(HttpStatusCode.Unauthorized, "Email ou senha inválidos.", "E_UNAUTHORIZED")]
        [InlineData(HttpStatusCode.Forbidden, "Acesso negado a partir deste IP.", "E_IP_DENIED")]
        [InlineData(HttpStatusCode.Forbidden, "Acesso fora do horário permitido.", "E_TIME_DENIED")]
        public async Task LoginAsync_ServerRejection_RelaysServerMessageAndCode(HttpStatusCode status, string serverMessage, string code)
        {
            var handler = StubHttpMessageHandler.Json(status,
                "{\"message\":\"" + serverMessage + "\",\"code\":\"" + code + "\"}");
            var client = new MantosfcAuthClient(handler, new Uri("https://mantosfc.test"));

            MantosfcAuthException ex = await Assert.ThrowsAsync<MantosfcAuthException>(
                () => client.LoginAsync("a@b.com", "x", CancellationToken.None));

            // The server already answers in pt-BR (verified against creator_auth_controller.ts)
            // — the client relays it verbatim rather than re-deriving its own wording.
            Assert.Equal(serverMessage, ex.Message);
            Assert.Equal(code, ex.Code);
        }

        [Fact]
        public async Task LoginAsync_NetworkFailure_ThrowsFriendlyMessage()
        {
            var handler = StubHttpMessageHandler.Throwing(new HttpRequestException("connection refused"));
            var client = new MantosfcAuthClient(handler, new Uri("https://mantosfc.test"));

            MantosfcAuthException ex = await Assert.ThrowsAsync<MantosfcAuthException>(
                () => client.LoginAsync("a@b.com", "x", CancellationToken.None));

            Assert.Equal("E_NETWORK", ex.Code);
            Assert.DoesNotContain("connection refused", ex.Message); // technical detail stays on InnerException
            Assert.NotNull(ex.InnerException);
        }

        [Fact]
        public async Task GetMeAsync_SendsBearerHeaderWithSessionId()
        {
            var handler = StubHttpMessageHandler.Json(HttpStatusCode.OK, @"{""creditsRemaining"":7,""isBlocked"":false}");
            var client = new MantosfcAuthClient(handler, new Uri("https://mantosfc.test"));

            MeResponse result = await client.GetMeAsync("session-uuid-123", CancellationToken.None);

            Assert.Equal(7, result.CreditsRemaining);
            AuthenticationHeaderValue? auth = handler.LastRequest!.Headers.Authorization;
            Assert.NotNull(auth);
            Assert.Equal("Bearer", auth!.Scheme);
            Assert.Equal("session-uuid-123", auth.Parameter);
        }

        [Fact]
        public async Task GetMeAsync_Unauthorized_ThrowsUnauthorizedCode()
        {
            var handler = StubHttpMessageHandler.Json(HttpStatusCode.Unauthorized,
                @"{""message"":""Sessão inválida ou expirada."",""code"":""E_UNAUTHORIZED""}");
            var client = new MantosfcAuthClient(handler, new Uri("https://mantosfc.test"));

            MantosfcAuthException ex = await Assert.ThrowsAsync<MantosfcAuthException>(
                () => client.GetMeAsync("stale-session", CancellationToken.None));

            Assert.Equal("E_UNAUTHORIZED", ex.Code);
        }
    }
}
