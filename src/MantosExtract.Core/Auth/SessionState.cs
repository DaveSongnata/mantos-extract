using System;

namespace MantosExtract.Core.Auth
{
    /// <summary>
    /// What gets persisted locally (via DPAPI, see MantosExtract.Windows.SecureCredentialStore)
    /// between CorelDRAW sessions. Never carries the password — only the mantosfc session UUID
    /// (Bearer token) and enough to render the header without a network round-trip.
    /// </summary>
    public sealed class SessionState
    {
        public string SessionId { get; set; } = "";
        public DateTimeOffset ExpiresAt { get; set; }
        public string Role { get; set; } = "";
        public string Email { get; set; } = "";
        public int CreditsRemaining { get; set; }

        public SessionState() { }

        public SessionState(string sessionId, DateTimeOffset expiresAt, string role, string email, int creditsRemaining)
        {
            SessionId = sessionId;
            ExpiresAt = expiresAt;
            Role = role;
            Email = email;
            CreditsRemaining = creditsRemaining;
        }

        /// <summary>
        /// mantosfc's session TTL is a fixed window (SESSION_TTL_CREATOR_HOURS, default 24h) with
        /// NO refresh endpoint (verified in session_service.ts) — so there is nothing to renew
        /// locally. Once expired the only path is a fresh login.
        /// </summary>
        public bool IsExpired(DateTimeOffset now) => now >= ExpiresAt;
    }
}
