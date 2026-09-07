using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MantosExtract.Core.Api;
using MantosExtract.Core.Detect;

namespace MantosExtract.Core.Extract
{
    /// <summary>
    /// Calls the real <c>POST /api/v1/mantos-extract/extract</c> (mantosfc, new endpoint) and
    /// downloads the resulting PNG. Response shape mirrors the existing generation endpoints
    /// (parts/elements/inpaint): <c>{url, meta:{...}}</c> — the image itself is a second GET on
    /// a public URL (mantosfc/backend/start/routes.ts: <c>GET /api/v1/images/:id</c> has no
    /// auth middleware, same as the R2-hosted URL when R2 is configured), never re-uploaded or
    /// re-authenticated.
    /// </summary>
    public sealed class ExtractionClient : IExtractionClient, IDisposable
    {
        private static readonly Uri DefaultBaseUri = new Uri("https://mantosfc.com");

        private readonly HttpClient _http;
        private readonly Uri _baseUri;
        private readonly bool _ownsHttp;

        public ExtractionClient(HttpMessageHandler? handler = null, Uri? baseUri = null)
        {
            _baseUri = baseUri ?? DefaultBaseUri;
            try { ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12; } catch { }
            _ownsHttp = handler == null;
            _http = handler != null ? new HttpClient(handler, disposeHandler: false) : new HttpClient();
            _http.Timeout = TimeSpan.FromSeconds(90); // image-edit round trip is the slowest call in the pipeline
        }

        public async Task<ExtractedImage> ExtractAsync(string sessionId, string openAiApiKey, string quality,
            byte[] imageBytes, string mimeType, BoundingBox box, string label, CancellationToken ct)
        {
            string url = await PostExtractionAsync(sessionId, openAiApiKey, quality, imageBytes, mimeType, box, label, ct)
                .ConfigureAwait(false);
            return await DownloadAsync(url, ct).ConfigureAwait(false);
        }

        private async Task<string> PostExtractionAsync(string sessionId, string openAiApiKey, string quality,
            byte[] imageBytes, string mimeType, BoundingBox box, string label, CancellationToken ct)
        {
            using var form = new MultipartFormDataContent();
            var imageContent = new ByteArrayContent(imageBytes);
            imageContent.Headers.ContentType = new MediaTypeHeaderValue(mimeType);
            form.Add(imageContent, "image", "selection.png");
            form.Add(new StringContent(box.XMin.ToString()), "x_min");
            form.Add(new StringContent(box.YMin.ToString()), "y_min");
            form.Add(new StringContent(box.XMax.ToString()), "x_max");
            form.Add(new StringContent(box.YMax.ToString()), "y_max");
            form.Add(new StringContent(label ?? ""), "label");

            using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(_baseUri, "/api/v1/mantos-extract/extract"))
            {
                Content = form,
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", sessionId);
            request.Headers.Add("X-OpenAI-Api-Key", openAiApiKey);
            // Cost/quality dial the operator sets in Configurações (Dave, 2026-09-07) — orthogonal
            // to fidelity (mantosfc's gpt-image-2 always preserves the source at high fidelity
            // regardless of this value; see CLAUDE.md/CHANGELOG on the hallucination fix).
            request.Headers.Add("X-OpenAI-Quality", string.IsNullOrWhiteSpace(quality) ? "medium" : quality);

            HttpResponseMessage response;
            try { response = await _http.SendAsync(request, ct).ConfigureAwait(false); }
            catch (Exception ex) when (!(ex is OperationCanceledException))
            {
                throw new MantosExtractApiException("E_NETWORK",
                    "Sem conexão com o servidor. Verifique sua internet e tente novamente.", ex);
            }

            string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) throw BuildError(response.StatusCode, body);

            try
            {
                using JsonDocument doc = JsonDocument.Parse(body);
                if (doc.RootElement.TryGetProperty("url", out JsonElement u) && u.ValueKind == JsonValueKind.String)
                {
                    string? url = u.GetString();
                    if (!string.IsNullOrWhiteSpace(url)) return url!;
                }
            }
            catch { /* falls through to the malformed-response throw below */ }

            throw new MantosExtractApiException("E_MALFORMED_RESPONSE",
                "O servidor respondeu de um jeito inesperado. Tente novamente em instantes.");
        }

        private async Task<ExtractedImage> DownloadAsync(string url, CancellationToken ct)
        {
            HttpResponseMessage response;
            try { response = await _http.GetAsync(url, ct).ConfigureAwait(false); }
            catch (Exception ex) when (!(ex is OperationCanceledException))
            {
                throw new MantosExtractApiException("E_NETWORK",
                    "A extração terminou, mas não consegui baixar a imagem. Tente novamente.", ex);
            }

            if (!response.IsSuccessStatusCode)
                throw new MantosExtractApiException("E_DOWNLOAD_FAILED",
                    $"A extração terminou, mas o download da imagem falhou ({(int)response.StatusCode}).");

            byte[] bytes = await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
            string mimeType = response.Content.Headers.ContentType?.MediaType ?? "image/png";
            return new ExtractedImage(bytes, mimeType, url);
        }

        private static MantosExtractApiException BuildError(HttpStatusCode status, string body)
        {
            string message = status switch
            {
                HttpStatusCode.Unauthorized => "Sessão inválida ou expirada.",
                HttpStatusCode.PaymentRequired => "Sem créditos disponíveis.",
                (HttpStatusCode)422 => "Não foi possível extrair este elemento.",
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
