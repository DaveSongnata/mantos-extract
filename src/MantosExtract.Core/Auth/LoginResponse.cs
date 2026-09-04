using System;
using System.Text.Json;

namespace MantosExtract.Core.Auth
{
    /// <summary>
    /// Parses the body of <c>POST /api/v1/auth/login</c> (mantosfc, unified login — verified
    /// against creator_auth_controller.ts). The endpoint answers with one of four roles
    /// (<c>admin</c>, <c>designer</c>, <c>tenant</c>, <c>sub_tenant</c>); Mantos Extract only
    /// serves confecção accounts, so only the last two are supported here — an admin or
    /// designer credential parses fine but <see cref="IsSupportedRole"/> is false, and the
    /// caller (AuthOrchestrator) turns that into a clear rejection instead of a broken screen.
    /// </summary>
    public sealed class LoginResponse
    {
        public string SessionId { get; }
        public DateTimeOffset ExpiresAt { get; }
        public string Role { get; }
        public int CreditsRemaining { get; }
        public string? CompanyName { get; }

        private LoginResponse(string sessionId, DateTimeOffset expiresAt, string role, int creditsRemaining, string? companyName)
        {
            SessionId = sessionId;
            ExpiresAt = expiresAt;
            Role = role;
            CreditsRemaining = creditsRemaining;
            CompanyName = companyName;
        }

        public bool IsSupportedRole => Role == "tenant" || Role == "sub_tenant";

        public static LoginResponse Parse(string json)
        {
            try
            {
                using JsonDocument doc = JsonDocument.Parse(json);
                JsonElement root = doc.RootElement;

                string sessionId = RequireString(root, "sessionId");
                string role = RequireString(root, "role");
                DateTimeOffset expiresAt = RequireDate(root, "expiresAt");

                int credits = 0;
                string? companyName = null;
                if (root.TryGetProperty("user", out JsonElement user) && user.ValueKind == JsonValueKind.Object)
                {
                    credits = user.TryGetProperty("creditsRemaining", out JsonElement c) && c.ValueKind == JsonValueKind.Number
                        ? c.GetInt32() : 0;
                    companyName = user.TryGetProperty("companyName", out JsonElement cn) && cn.ValueKind == JsonValueKind.String
                        ? cn.GetString() : null;
                }

                return new LoginResponse(sessionId, expiresAt, role, credits, companyName);
            }
            catch (MantosfcAuthException) { throw; }
            catch (Exception ex)
            {
                throw new MantosfcAuthException("E_MALFORMED_RESPONSE",
                    "O servidor respondeu de um jeito inesperado. Tente novamente em instantes.", ex);
            }
        }

        private static string RequireString(JsonElement root, string name)
        {
            if (root.TryGetProperty(name, out JsonElement e) && e.ValueKind == JsonValueKind.String)
            {
                string? value = e.GetString();
                if (!string.IsNullOrEmpty(value)) return value!;
            }
            throw new MantosfcAuthException("E_MALFORMED_RESPONSE",
                "O servidor respondeu de um jeito inesperado. Tente novamente em instantes.");
        }

        private static DateTimeOffset RequireDate(JsonElement root, string name)
        {
            string raw = RequireString(root, name);
            if (DateTimeOffset.TryParse(raw, System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal,
                    out DateTimeOffset value))
                return value;
            throw new MantosfcAuthException("E_MALFORMED_RESPONSE",
                "O servidor respondeu de um jeito inesperado. Tente novamente em instantes.");
        }
    }
}
