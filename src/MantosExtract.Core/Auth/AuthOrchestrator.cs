using System;
using System.Threading;
using System.Threading.Tasks;

namespace MantosExtract.Core.Auth
{
    /// <summary>
    /// Decides which screen the docker shows, given the local credential store and the real
    /// mantosfc responses — the only piece of Fase 1 logic with a decision to test. Pure: both
    /// dependencies are interfaces, and the clock is injectable, so every branch (no local
    /// session, expired locally, revoked server-side, network down, unsupported role) is a unit
    /// test with no CorelDRAW, no WebView2, no real HTTP.
    /// </summary>
    public sealed class AuthOrchestrator
    {
        private readonly IMantosfcAuthClient _client;
        private readonly ICredentialStore _store;
        private readonly Func<DateTimeOffset> _now;

        public AuthOrchestrator(IMantosfcAuthClient client, ICredentialStore store, Func<DateTimeOffset>? now = null)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _now = now ?? (() => DateTimeOffset.UtcNow);
        }

        /// <summary>Called once when the docker loads. Never shows the login screen if a
        /// still-valid local session exists — that is Fase 1's whole acceptance criterion.</summary>
        public async Task<AuthOrchestratorResult> BootstrapAsync(CancellationToken ct)
        {
            SessionState? session = _store.LoadSession();
            if (session == null || session.IsExpired(_now()))
            {
                _store.ClearSession();
                return AuthOrchestratorResult.ToLogin();
            }

            try
            {
                MeResponse me = await _client.GetMeAsync(session.SessionId, ct).ConfigureAwait(false);
                session.CreditsRemaining = me.CreditsRemaining;
                _store.SaveSession(session);
                return AuthOrchestratorResult.ToHome(session);
            }
            catch (MantosfcAuthException ex) when (ex.Code == "E_UNAUTHORIZED")
            {
                // Session revoked/expired server-side (admin action, or the TTL lapsed between
                // this machine's clock and the server's) — the ONLY case that forces re-login.
                _store.ClearSession();
                return AuthOrchestratorResult.ToLogin();
            }
            catch (MantosfcAuthException ex)
            {
                // Network down, IP/time-window denial, etc: the operator already has a valid
                // local session and a last-known credit balance — showing Home with a warning
                // banner beats bouncing them to a login screen they cannot even submit offline.
                return AuthOrchestratorResult.ToHome(session, ex.Message);
            }
        }

        public async Task<AuthOrchestratorResult> LoginAsync(string email, string password, CancellationToken ct)
        {
            try
            {
                LoginResponse login = await _client.LoginAsync(email, password, ct).ConfigureAwait(false);
                var session = new SessionState(login.SessionId, login.ExpiresAt, login.Role, email, login.CreditsRemaining);
                _store.SaveSession(session);
                return AuthOrchestratorResult.ToHome(session);
            }
            catch (MantosfcAuthException ex)
            {
                return AuthOrchestratorResult.ToLogin(ex.Message);
            }
        }

        public async Task<AuthOrchestratorResult> LogoutAsync(CancellationToken ct)
        {
            SessionState? session = _store.LoadSession();
            if (session != null)
            {
                try { await _client.LogoutAsync(session.SessionId, ct).ConfigureAwait(false); }
                catch (MantosfcAuthException) { /* best-effort: local session clears regardless */ }
            }
            _store.ClearSession();
            return AuthOrchestratorResult.ToLogin();
        }
    }
}
