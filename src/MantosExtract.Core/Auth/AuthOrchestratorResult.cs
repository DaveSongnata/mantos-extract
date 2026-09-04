namespace MantosExtract.Core.Auth
{
    public enum AuthScreen
    {
        Login,
        Home,
    }

    /// <summary>
    /// What the bridge shows the page after any auth operation. A single shape covers success,
    /// a login-form error, and a "stayed on Home but the credit refresh failed" warning — the
    /// page only ever needs to read <see cref="Screen"/> plus one optional message.
    /// </summary>
    public sealed class AuthOrchestratorResult
    {
        public AuthScreen Screen { get; }
        public SessionState? Session { get; }
        public string? ErrorMessage { get; }
        public string? WarningMessage { get; }

        /// <summary>Full exception detail (code + message + InnerException, via
        /// <c>Exception.ToString()</c>) for a failed auth call — NEVER shown to the operator,
        /// only written to docker.log by the bridge. Without this, every network failure looked
        /// identical in the log ("Sem conexão com o servidor") with no way to tell a DNS
        /// failure from a TLS handshake failure from a plain timeout (Dave hit exactly this
        /// while testing 2026-09-04 — the log had zero detail past "OnWebMessage: login").</summary>
        public string? DebugDetail { get; }

        private AuthOrchestratorResult(AuthScreen screen, SessionState? session, string? errorMessage, string? warningMessage, string? debugDetail)
        {
            Screen = screen;
            Session = session;
            ErrorMessage = errorMessage;
            WarningMessage = warningMessage;
            DebugDetail = debugDetail;
        }

        public static AuthOrchestratorResult ToLogin(string? errorMessage = null, string? debugDetail = null) =>
            new AuthOrchestratorResult(AuthScreen.Login, null, errorMessage, null, debugDetail);

        public static AuthOrchestratorResult ToHome(SessionState session, string? warningMessage = null, string? debugDetail = null) =>
            new AuthOrchestratorResult(AuthScreen.Home, session, null, warningMessage, debugDetail);
    }
}
