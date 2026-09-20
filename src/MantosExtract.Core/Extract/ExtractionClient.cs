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
            // 90s estourou em execução real (Dave, 2026-09-08): el_3 deu TaskCanceledException em
            // SendAsync — exatamente o sintoma de HttpClient.Timeout vencendo, confirmado lendo o
            // stack trace (bate com PostExtractionAsync, não com um cancelamento manual). O
            // servidor não impõe timeout nenhum na própria chamada à OpenAI (fetch nativo, sem
            // AbortSignal) nem há proxy reverso com timeout mais curto neste repo — o gargalo real
            // era só este valor. Dave pediu explicitamente pelo menos 10 minutos de folga pra
            // véspera de produção, preferindo eliminar esse modo de falha por completo a só reduzir
            // a chance dele acontecer (o operador ainda tem "tentar de novo" pros elementos que
            // falharem de verdade, então um teto bem mais alto não deixa nada preso pra sempre).
            _http.Timeout = TimeSpan.FromMinutes(10);
        }

        // `legacyModel` vem DEPOIS do token de propósito (parâmetro opcional): é uma flag, nunca um
        // nome de modelo — o servidor é quem decide o modelo (mantos_extract_presets.ts). Só vira
        // true depois do operador aceitar o modal "usar o modelo anterior" (chave sem acesso ao 2.5).
        public async Task<ExtractedImage> ExtractAsync(string sessionId, string openAiApiKey, string quality,
            byte[] imageBytes, string mimeType, BoundingBox box, string label, CancellationToken ct,
            bool legacyModel = false)
        {
            using var form = new MultipartFormDataContent();
            form.Add(ImagePart(imageBytes, mimeType), "image", "selection.png");
            form.Add(new StringContent(box.XMin.ToString()), "x_min");
            form.Add(new StringContent(box.YMin.ToString()), "y_min");
            form.Add(new StringContent(box.XMax.ToString()), "x_max");
            form.Add(new StringContent(box.YMax.ToString()), "y_max");
            form.Add(new StringContent(label ?? ""), "label");

            string url = await PostForUrlAsync("/api/v1/mantos-extract/extract", form, sessionId, openAiApiKey,
                quality, legacyModel, ct).ConfigureAwait(false);
            return await DownloadAsync(url, ct).ConfigureAwait(false);
        }

        /// <summary>"Fundo" (Dave, 2026-09-11) — endpoint e corpo de request DIFERENTES de
        /// ExtractAsync de propósito (sem bbox/label, a foto inteira vai pro servidor), mas
        /// reusa DownloadAsync/BuildError (mesmo contrato de resposta {url, meta}).</summary>
        public async Task<ExtractedImage> ExtractBackgroundAsync(string sessionId, string openAiApiKey, string quality,
            byte[] imageBytes, string mimeType, CancellationToken ct, bool legacyModel = false)
        {
            using var form = new MultipartFormDataContent();
            form.Add(ImagePart(imageBytes, mimeType), "image", "foto.png");

            string url = await PostForUrlAsync("/api/v1/mantos-extract/extract-background", form, sessionId,
                openAiApiKey, quality, legacyModel, ct).ConfigureAwait(false);
            return await DownloadAsync(url, ct).ConfigureAwait(false);
        }

        public async Task<ExtractedImage> RefineAsync(string sessionId, string openAiApiKey, string quality,
            byte[] imageBytes, string mimeType, string instruction, CancellationToken ct,
            bool legacyModel = false)
        {
            using var form = new MultipartFormDataContent();
            form.Add(ImagePart(imageBytes, mimeType), "image", "selecao.png");
            form.Add(new StringContent(instruction ?? ""), "instruction");

            string url = await PostForUrlAsync("/api/v1/mantos-extract/refine", form, sessionId, openAiApiKey,
                quality, legacyModel, ct).ConfigureAwait(false);
            return await DownloadAsync(url, ct).ConfigureAwait(false);
        }
        private static ByteArrayContent ImagePart(byte[] imageBytes, string mimeType)
        {
            var imageContent = new ByteArrayContent(imageBytes);
            imageContent.Headers.ContentType = new MediaTypeHeaderValue(mimeType);
            return imageContent;
        }

        /// <summary>POST comum aos três endpoints de imagem (elemento, fundo, refino): sessão
        /// Bearer + chave OpenAI BYOK + qualidade + flag de modelo anterior, e a resposta
        /// <c>{url, meta}</c>. Devolve só a URL.</summary>
        private async Task<string> PostForUrlAsync(string path, MultipartFormDataContent form, string sessionId,
            string openAiApiKey, string quality, bool legacyModel, CancellationToken ct)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(_baseUri, path))
            {
                Content = form,
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", sessionId);
            request.Headers.Add("X-OpenAI-Api-Key", openAiApiKey);
            // Só o NOME do preset que o operador escolheu nas Configurações (Dave, 2026-09-18): o
            // servidor traduz em modelo/qualidade/tamanho — o cliente nunca manda parâmetros crus.
            request.Headers.Add("X-Extract-Preset", ExtractionPresets.Normalize(quality));
            if (legacyModel) request.Headers.Add("X-Extract-Legacy-Model", "1");

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

        internal static MantosExtractApiException BuildError(HttpStatusCode status, string body)
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
