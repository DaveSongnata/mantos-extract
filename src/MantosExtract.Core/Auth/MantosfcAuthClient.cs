using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace MantosExtract.Core.Auth
{
    /// <summary>
    /// Talks to the real mantosfc creator/tenant endpoints (<c>/api/v1/auth/login</c>,
    /// <c>/api/v1/creator/me</c>, <c>/api/v1/auth/logout</c>) — the SAME Bearer-session system
    /// MantosCreator's own web frontend uses, verified against creator_auth_controller.ts /
    /// tenant_auth_middleware.ts / session_service.ts. Deliberately unrelated to
    /// SisCut.Security.LicenseClient (HWID + Ed25519 desktop licensing, a different product).
    /// </summary>
    public sealed class MantosfcAuthClient : IMantosfcAuthClient, IDisposable
    {
        private static readonly Uri DefaultBaseUri = new Uri("https://mantosfc.com");

        private readonly HttpClient _http;
        private readonly Uri _baseUri;
        private readonly bool _ownsHttp;

        public MantosfcAuthClient(HttpMessageHandler? handler = null, Uri? baseUri = null)
        {
            _baseUri = baseUri ?? DefaultBaseUri;

            // CorelDRAW's host AppDomain can leave ServicePointManager on a legacy TLS default
            // (TLS 1.0) that mantosfc rejects at the HTTPS handshake — same note as
            // SisCut.Security.LicenseClient. Force TLS 1.2 (process-wide, idempotent).
            try { ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12; }
            catch { /* older platforms: leave the default */ }

            _ownsHttp = handler == null;
            _http = handler != null ? new HttpClient(handler, disposeHandler: false) : new HttpClient();
            _http.Timeout = TimeSpan.FromSeconds(30);
        }

        public async Task<LoginResponse> LoginAsync(string email, string password, CancellationToken ct)
        {
            string body = "{\"email\":" + JsonQuote(email) + ",\"password\":" + JsonQuote(password) + "}";
            using var content = new StringContent(body, Encoding.UTF8, "application/json");
            using HttpResponseMessage response = await SendAsync(HttpMethod.Post, "/api/v1/auth/login", content, null, ct)
                .ConfigureAwait(false);

            string respBody = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) throw BuildError(response.StatusCode, respBody);

            LoginResponse login = LoginResponse.Parse(respBody);
            if (!login.IsSupportedRole)
                throw new MantosfcAuthException("E_UNSUPPORTED_ROLE",
                    "Esta conta não tem acesso ao Mantos Extract. Peça ao Davidson uma conta de confecção.");
            return login;
        }

        public async Task<MeResponse> GetMeAsync(string sessionId, CancellationToken ct)
        {
            using HttpResponseMessage response = await SendAsync(HttpMethod.Get, "/api/v1/creator/me", null, sessionId, ct)
                .ConfigureAwait(false);

            string respBody = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) throw BuildError(response.StatusCode, respBody);

            return MeResponse.Parse(respBody);
        }

        public async Task LogoutAsync(string sessionId, CancellationToken ct)
        {
            using HttpResponseMessage response = await SendAsync(HttpMethod.Post, "/api/v1/auth/logout", null, sessionId, ct)
                .ConfigureAwait(false);
            // Logout is best-effort from the operator's point of view — the local session is
            // cleared regardless (AuthOrchestrator), so a failure here is logged upstream, not
            // thrown as a blocking error.
            if (!response.IsSuccessStatusCode)
            {
                string respBody = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                throw BuildError(response.StatusCode, respBody);
            }
        }

        private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, HttpContent? content, string? bearerSessionId, CancellationToken ct)
        {
            using var request = new HttpRequestMessage(method, new Uri(_baseUri, path));
            if (content != null) request.Content = content;
            if (bearerSessionId != null)
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerSessionId);

            try
            {
                return await _http.SendAsync(request, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (!(ex is OperationCanceledException))
            {
                // RB2 (sem internet): friendly message only — the technical detail rides on the
                // inner exception for the addin's docker.log, never shown to the operator.
                throw new MantosfcAuthException("E_NETWORK",
                    "Sem conexão com o servidor. Verifique sua internet e tente novamente.", ex);
            }
        }

        private static MantosfcAuthException BuildError(HttpStatusCode status, string body)
        {
            string message = DefaultMessageFor(status);
            string code = "E_UNKNOWN";
            try
            {
                using JsonDocument doc = JsonDocument.Parse(body);
                if (doc.RootElement.TryGetProperty("message", out JsonElement m) && m.ValueKind == JsonValueKind.String)
                {
                    string? serverMessage = m.GetString();
                    if (!string.IsNullOrWhiteSpace(serverMessage)) message = serverMessage!;
                }
                if (doc.RootElement.TryGetProperty("code", out JsonElement c) && c.ValueKind == JsonValueKind.String)
                {
                    string? serverCode = c.GetString();
                    if (!string.IsNullOrWhiteSpace(serverCode)) code = serverCode!;
                }
            }
            catch { /* body wasn't JSON — keep the generic message/code by status */ }

            return new MantosfcAuthException(code, message);
        }

        private static string DefaultMessageFor(HttpStatusCode status) => status switch
        {
            HttpStatusCode.Unauthorized => "Sessão inválida ou expirada.",
            HttpStatusCode.Forbidden => "Acesso negado pelo servidor.",
            HttpStatusCode.PaymentRequired => "Sem créditos disponíveis.",
            _ => $"O servidor respondeu com erro ({(int)status})."
        };

        private static string JsonQuote(string value) => JsonSerializer.Serialize(value);

        public void Dispose()
        {
            if (_ownsHttp) _http.Dispose();
        }
    }
}
