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

        private AuthOrchestratorResult(AuthScreen screen, SessionState? session, string? errorMessage, string? warningMessage)
        {
            Screen = screen;
            Session = session;
            ErrorMessage = errorMessage;
            WarningMessage = warningMessage;
        }

        public static AuthOrchestratorResult ToLogin(string? errorMessage = null) =>
            new AuthOrchestratorResult(AuthScreen.Login, null, errorMessage, null);

        public static AuthOrchestratorResult ToHome(SessionState session, string? warningMessage = null) =>
            new AuthOrchestratorResult(AuthScreen.Home, session, null, warningMessage);
    }
}
