using System;
using System.Text.Json;

namespace MantosExtract.Core.Auth
{
    /// <summary>
    /// Parses the body of <c>GET /api/v1/creator/me</c> (mantosfc) — used to refresh the credit
    /// balance shown in the header without forcing a new login. Verified against
    /// backend/app/controllers/v1/creator/me_controller.ts.
    /// </summary>
    public sealed class MeResponse
    {
        public int CreditsRemaining { get; }
        public bool IsBlocked { get; }

        private MeResponse(int creditsRemaining, bool isBlocked)
        {
            CreditsRemaining = creditsRemaining;
            IsBlocked = isBlocked;
        }

        public static MeResponse Parse(string json)
        {
            try
            {
                using JsonDocument doc = JsonDocument.Parse(json);
                JsonElement root = doc.RootElement;
                int credits = root.TryGetProperty("creditsRemaining", out JsonElement c) && c.ValueKind == JsonValueKind.Number
                    ? c.GetInt32() : 0;
                bool blocked = root.TryGetProperty("isBlocked", out JsonElement b) && b.ValueKind == JsonValueKind.True;
                return new MeResponse(credits, blocked);
            }
            catch (Exception ex)
            {
                throw new MantosfcAuthException("E_MALFORMED_RESPONSE",
                    "O servidor respondeu de um jeito inesperado. Tente novamente em instantes.", ex);
            }
        }
    }
}
