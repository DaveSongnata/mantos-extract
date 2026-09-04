using System;
using System.Threading;
using System.Threading.Tasks;
using MantosExtract.Core.Auth;
using Xunit;

namespace MantosExtract.Core.Tests.Auth
{
    public class AuthOrchestratorTests
    {
        private static readonly DateTimeOffset Now = new DateTimeOffset(2026, 9, 5, 12, 0, 0, TimeSpan.Zero);

        private static AuthOrchestrator Build(FakeAuthClient client, FakeCredentialStore store) =>
            new AuthOrchestrator(client, store, () => Now);

        [Fact]
        public async Task Bootstrap_NoLocalSession_GoesToLogin_WithoutCallingServer()
        {
            var client = new FakeAuthClient();
            var store = new FakeCredentialStore();

            AuthOrchestratorResult result = await Build(client, store).BootstrapAsync(CancellationToken.None);

            Assert.Equal(AuthScreen.Login, result.Screen);
            Assert.Null(client.LastMeSessionId); // never hit /creator/me with nothing to send
        }

        [Fact]
        public async Task Bootstrap_LocallyExpiredSession_ClearsItAndGoesToLogin_WithoutCallingServer()
        {
            var client = new FakeAuthClient();
            var store = new FakeCredentialStore();
            store.SaveSession(new SessionState("s1", Now.AddHours(-1), "tenant", "a@b.com", 10));

            AuthOrchestratorResult result = await Build(client, store).BootstrapAsync(CancellationToken.None);

            Assert.Equal(AuthScreen.Login, result.Screen);
            Assert.Null(store.LoadSession());
            Assert.Null(client.LastMeSessionId);
        }

        [Fact]
        public async Task Bootstrap_ValidLocalSession_RefreshesCreditsFromServer_GoesToHome()
        {
            // This IS Fase 1's acceptance criterion: a still-valid local session skips login.
            var client = new FakeAuthClient { OnGetMe = _ => MeResponse.Parse(@"{""creditsRemaining"":99,""isBlocked"":false}") };
            var store = new FakeCredentialStore();
            store.SaveSession(new SessionState("s1", Now.AddHours(1), "tenant", "a@b.com", 10));

            AuthOrchestratorResult result = await Build(client, store).BootstrapAsync(CancellationToken.None);

            Assert.Equal(AuthScreen.Home, result.Screen);
            Assert.Equal(99, result.Session!.CreditsRemaining);
            Assert.Equal(99, store.LoadSession()!.CreditsRemaining); // persisted back
            Assert.Equal("s1", client.LastMeSessionId);
        }

        [Fact]
        public async Task Bootstrap_ServerRevokedSession_ClearsLocalAndGoesToLogin()
        {
            var client = new FakeAuthClient { OnGetMe = _ => throw new MantosfcAuthException("E_UNAUTHORIZED", "Sessão inválida ou expirada.") };
            var store = new FakeCredentialStore();
            store.SaveSession(new SessionState("s1", Now.AddHours(1), "tenant", "a@b.com", 10));

            AuthOrchestratorResult result = await Build(client, store).BootstrapAsync(CancellationToken.None);

            Assert.Equal(AuthScreen.Login, result.Screen);
            Assert.Null(store.LoadSession());
        }

        [Fact]
        public async Task Bootstrap_NetworkDown_KeepsLocalSessionOnHome_WithWarning()
        {
            // A valid local session and a last-known credit balance beat bouncing the operator
            // to a login screen they cannot even submit offline.
            var client = new FakeAuthClient { OnGetMe = _ => throw new MantosfcAuthException("E_NETWORK", "Sem conexão com o servidor. Verifique sua internet e tente novamente.") };
            var store = new FakeCredentialStore();
            store.SaveSession(new SessionState("s1", Now.AddHours(1), "tenant", "a@b.com", 10));

            AuthOrchestratorResult result = await Build(client, store).BootstrapAsync(CancellationToken.None);

            Assert.Equal(AuthScreen.Home, result.Screen);
            Assert.Equal(10, result.Session!.CreditsRemaining); // last known, unchanged
            Assert.NotNull(result.WarningMessage);
            Assert.NotNull(store.LoadSession()); // session kept, not cleared
        }

        [Fact]
        public async Task Login_Success_PersistsSessionAndGoesToHome()
        {
            var client = new FakeAuthClient
            {
                OnLogin = (email, password) => LoginResponse.Parse(
                    @"{""sessionId"":""new-session"",""expiresAt"":""2026-09-06T12:00:00.000Z"",""role"":""tenant"",""user"":{""creditsRemaining"":50}}"),
            };
            var store = new FakeCredentialStore();

            AuthOrchestratorResult result = await Build(client, store).LoginAsync("dono@confeccao.com", "senha", CancellationToken.None);

            Assert.Equal(AuthScreen.Home, result.Screen);
            Assert.Equal("new-session", store.LoadSession()!.SessionId);
            Assert.Equal(50, store.LoadSession()!.CreditsRemaining);
        }

        [Fact]
        public async Task Login_ServerRejection_StaysOnLoginWithMessage_AndDoesNotPersist()
        {
            var client = new FakeAuthClient
            {
                OnLogin = (email, password) => throw new MantosfcAuthException("E_UNAUTHORIZED", "Email ou senha inválidos."),
            };
            var store = new FakeCredentialStore();

            AuthOrchestratorResult result = await Build(client, store).LoginAsync("a@b.com", "wrong", CancellationToken.None);

            Assert.Equal(AuthScreen.Login, result.Screen);
            Assert.Equal("Email ou senha inválidos.", result.ErrorMessage);
            Assert.Null(store.LoadSession());
        }

        [Fact]
        public async Task Logout_ClearsLocalSession_EvenWhenServerCallFails()
        {
            var client = new FakeAuthClient { OnLogout = _ => throw new MantosfcAuthException("E_NETWORK", "Sem conexão.") };
            var store = new FakeCredentialStore();
            store.SaveSession(new SessionState("s1", Now.AddHours(1), "tenant", "a@b.com", 10));

            AuthOrchestratorResult result = await Build(client, store).LogoutAsync(CancellationToken.None);

            Assert.Equal(AuthScreen.Login, result.Screen);
            Assert.Null(store.LoadSession());
        }

        [Fact]
        public async Task Logout_NoLocalSession_DoesNotCallServer()
        {
            var client = new FakeAuthClient();
            var store = new FakeCredentialStore();

            await Build(client, store).LogoutAsync(CancellationToken.None);

            Assert.Equal(0, client.LogoutCallCount);
        }
    }
}
