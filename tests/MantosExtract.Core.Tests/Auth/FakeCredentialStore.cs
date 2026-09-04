using MantosExtract.Core.Auth;

namespace MantosExtract.Core.Tests.Auth
{
    /// <summary>In-memory ICredentialStore — no DPAPI, no filesystem. The real implementation
    /// (MantosExtract.Windows.SecureCredentialStore) is net48-only and gets no unit tests here
    /// by construction; that is the point of the interface.</summary>
    internal sealed class FakeCredentialStore : ICredentialStore
    {
        public SessionState? Session { get; private set; }
        public string? OpenAiKey { get; private set; }

        public SessionState? LoadSession() => Session;
        public void SaveSession(SessionState session) => Session = session;
        public void ClearSession() => Session = null;

        public string? LoadOpenAiKey() => OpenAiKey;
        public void SaveOpenAiKey(string apiKey) => OpenAiKey = apiKey;
        public void ClearOpenAiKey() => OpenAiKey = null;
    }
}
