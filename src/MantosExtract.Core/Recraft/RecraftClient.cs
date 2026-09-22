using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MantosExtract.Core.Api;
using MantosExtract.Core.Extract;

namespace MantosExtract.Core.Recraft
{
    /// <summary>
    /// Chama <c>POST /api/v1/mantos-extract/remove-background</c> e <c>/vectorize</c> (mantosfc) e
    /// baixa o resultado. A Recraft em si só é chamada pelo servidor.
    ///
    /// Só a sessão Bearer vai daqui (Dave, 2026-09-22). A chave da Recraft deixou de ser BYOK e
    /// virou chave única da plataforma, no <c>.env</c> do mantosfc — o addin não a guarda, não a
    /// envia e não tem como influenciá-la. Quem pode chamar, e quantas vezes no mês, é o servidor
    /// que decide (cota por plano, contada por empresa). É por isso que sumiram o header
    /// <c>X-Recraft-Api-Key</c> e a tela que pedia a chave.
    ///
    /// O contrato de resposta é o da extração (<c>{url, meta}</c>), então o download e o mapeamento
    /// de erros são os mesmos (<see cref="ExtractionClient"/>).
    /// </summary>
    public sealed class RecraftClient : IRecraftClient, IDisposable
    {
        private static readonly Uri DefaultBaseUri = new Uri("https://mantosfc.com");

        private readonly HttpClient _http;
        private readonly Uri _baseUri;
        private readonly bool _ownsHttp;

        public RecraftClient(HttpMessageHandler? handler = null, Uri? baseUri = null)
        {
            _baseUri = baseUri ?? DefaultBaseUri;
            try { ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12; } catch { }
            _ownsHttp = handler == null;
            _http = handler != null ? new HttpClient(handler, disposeHandler: false) : new HttpClient();
            // Latência medida da Recraft: 4 a 12 s. O servidor limita cada tentativa a 60 s e faz no
            // máximo 1 retry, então o pior caso do servidor é ~2 min; 3 min cobre isso com folga sem
            // deixar o operador esperando pra sempre (o botão de cancelar continua valendo).
            _http.Timeout = TimeSpan.FromMinutes(3);
        }

        public async Task<ExtractedImage> RunAsync(RecraftOperation operation, string sessionId,
            byte[] imageBytes, string mimeType, CancellationToken ct)
        {
            using var form = new MultipartFormDataContent();
            var imageContent = new ByteArrayContent(imageBytes);
            imageContent.Headers.ContentType = new MediaTypeHeaderValue(mimeType);
            form.Add(imageContent, "image", "selecao.png");

            using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(_baseUri, RecraftOperations.EndpointPath(operation)))
            {
                Content = form,
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", sessionId);

            string url = await PostForUrlAsync(request, ct).ConfigureAwait(false);
            return await DownloadAsync(url, ct).ConfigureAwait(false);
        }

        private async Task<string> PostForUrlAsync(HttpRequestMessage request, CancellationToken ct)
        {
            HttpResponseMessage response;
            try { response = await _http.SendAsync(request, ct).ConfigureAwait(false); }
            // HttpClient.Timeout expira como cancelamento MESMO sem o operador cancelar — sem esta
            // distinção o Bridge mostraria "cancelado" pra uma Recraft que só demorou.
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                throw new MantosExtractApiException("E_RECRAFT_TIMEOUT",
                    "A Recraft demorou demais para responder. Tente de novo em instantes.");
            }
            catch (Exception ex) when (!(ex is OperationCanceledException))
            {
                throw new MantosExtractApiException("E_NETWORK",
                    "Sem conexão com o servidor. Verifique sua internet e tente novamente.", ex);
            }

            string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) throw ExtractionClient.BuildError(response.StatusCode, body);

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
                    "O resultado ficou pronto, mas não consegui baixá-lo. Tente novamente.", ex);
            }

            if (!response.IsSuccessStatusCode)
                throw new MantosExtractApiException("E_DOWNLOAD_FAILED",
                    $"O resultado ficou pronto, mas o download falhou ({(int)response.StatusCode}).");

            byte[] bytes = await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
            string mimeType = response.Content.Headers.ContentType?.MediaType ?? "image/png";
            return new ExtractedImage(bytes, mimeType, url);
        }

        public void Dispose()
        {
            if (_ownsHttp) _http.Dispose();
        }
    }
}