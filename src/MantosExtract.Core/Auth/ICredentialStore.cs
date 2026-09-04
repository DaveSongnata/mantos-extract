namespace MantosExtract.Core.Auth
{
    /// <summary>
    /// Local persistence for the two secrets Mantos Extract holds on the operator's machine: the
    /// mantosfc session (Bearer UUID) and the tenant's own OpenAI key (BYOK, Dave 2026-09-03 —
    /// same model Gemini already uses in MantosCreator). Segregated behind an interface so Core
    /// stays pure/testable (fakes in tests); the real implementation
    /// (MantosExtract.Windows.SecureCredentialStore) is DPAPI-backed and lives in the net48
    /// layer, the only place ProtectedData/Windows APIs are allowed.
    /// </summary>
    public interface ICredentialStore
    {
        SessionState? LoadSession();
        void SaveSession(SessionState session);
        void ClearSession();

        string? LoadOpenAiKey();
        void SaveOpenAiKey(string apiKey);
        void ClearOpenAiKey();
    }
}
