using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MantosExtract.Core.Api;

namespace MantosExtract.Core.Detect
{
    /// <summary>
    /// Calls the real <c>POST /api/v1/mantos-extract/detect</c> (mantosfc, new endpoint — see
    /// mantosfc/backend/app/controllers/v1/mantos_extract_detect_controller.ts). Two credentials
    /// per request, never conflated (CLAUDE.md "Duas superfícies de auth"): the mantosfc Bearer
    /// session (who is calling) and the tenant's own OpenAI key via X-OpenAI-Api-Key (BYOK,
    /// Dave 2026-09-03 — same header shape MantosCreator's Gemini flow already uses).
    /// </summary>
    public sealed class DetectionClient : IDetectionClient, IDisposable
    {
        private static readonly Uri DefaultBaseUri = new Uri("https://mantosfc.com");

        private readonly HttpClient _http;
        private readonly Uri _baseUri;
        private readonly bool _ownsHttp;

        public DetectionClient(HttpMessageHandler? handler = null, Uri? baseUri = null)
        {
            _baseUri = baseUri ?? DefaultBaseUri;
            try { ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12; } catch { }
            _ownsHttp = handler == null;
            _http = handler != null ? new HttpClient(handler, disposeHandler: false) : new HttpClient();
            // Detection can take longer than a plain API call (server-side OpenAI round trip) —
            // generous but bounded, matching the spirit of EngineRunner's hard timeouts.
            _http.Timeout = TimeSpan.FromSeconds(60);
        }

        public async Task<DetectionResult> DetectAsync(string sessionId, string openAiApiKey,
            byte[] imageBytes, string mimeType, CancellationToken ct)
        {
            using var form = new MultipartFormDataContent();
            var imageContent = new ByteArrayContent(imageBytes);
            imageContent.Headers.ContentType = new MediaTypeHeaderValue(mimeType);
            form.Add(imageContent, "image", "selection.png");

            using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(_baseUri, "/api/v1/mantos-extract/detect"))
            {
                Content = form,
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", sessionId);
            request.Headers.Add("X-OpenAI-Api-Key", openAiApiKey);

            HttpResponseMessage response;
            try { response = await _http.SendAsync(request, ct).ConfigureAwait(false); }
            catch (Exception ex) when (!(ex is OperationCanceledException))
            {
                throw new MantosExtractApiException("E_NETWORK",
                    "Sem conexão com o servidor. Verifique sua internet e tente novamente.", ex);
            }

            string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) throw BuildError(response.StatusCode, body);

            return DetectionResult.Parse(body);
        }

        private static MantosExtractApiException BuildError(HttpStatusCode status, string body)
        {
            string message = status switch
            {
                HttpStatusCode.Unauthorized => "Sessão inválida ou expirada.",
                HttpStatusCode.PaymentRequired => "Sem créditos disponíveis.",
                (HttpStatusCode)422 => "Não foi possível detectar elementos nesta imagem.",
                _ => $"O servidor respondeu com erro ({(int)status}).",
            };
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

            return new MantosExtractApiException(code, message);
        }

        public void Dispose()
        {
            if (_ownsHttp) _http.Dispose();
        }
    }
}
