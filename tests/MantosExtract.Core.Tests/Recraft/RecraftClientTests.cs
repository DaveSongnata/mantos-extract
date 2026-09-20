using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MantosExtract.Core.Api;
using MantosExtract.Core.Recraft;
using MantosExtract.Core.Tests.Auth;
using Xunit;

namespace MantosExtract.Core.Tests.Recraft
{
    public class RecraftClientTests
    {
        private static readonly Uri Base = new Uri("https://mantosfc.test");

        private static StubHttpMessageHandler SucceedingHandler(Action<HttpRequestMessage, string>? onPost = null,
            string downloadMime = "image/png", byte[]? download = null)
        {
            return new StubHttpMessageHandler(req =>
            {
                if (req.Method == HttpMethod.Post)
                {
                    onPost?.Invoke(req, req.Content!.ReadAsStringAsync().GetAwaiter().GetResult());
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(@"{""url"":""https://mantosfc.test/api/v1/images/r1""}",
                            Encoding.UTF8, "application/json"),
                    };
                }
                var content = new ByteArrayContent(download ?? new byte[] { 7, 7 });
                content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(downloadMime);
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
            });
        }

        [Theory]
        [InlineData(RecraftOperation.RemoveBackground, "/api/v1/mantos-extract/remove-background")]
        [InlineData(RecraftOperation.Vectorize, "/api/v1/mantos-extract/vectorize")]
        public async Task RunAsync_PostsToTheOperationRoute_WithSessionAndRecraftKey_ThenDownloads(
            RecraftOperation operation, string expectedPath)
        {
            string? path = null, bearer = null, recraftKey = null;
            bool sentOpenAiKey = true;
            var handler = SucceedingHandler((req, _) =>
            {
                path = req.RequestUri!.AbsolutePath;
                bearer = req.Headers.Authorization?.ToString();
                recraftKey = req.Headers.TryGetValues("X-Recraft-Api-Key", out var v) ? string.Join(",", v) : null;
                sentOpenAiKey = req.Headers.Contains("X-OpenAI-Api-Key");
            });
            var client = new RecraftClient(handler, Base);

            var result = await client.RunAsync(operation, "s1", "rk-key", new byte[] { 1, 2, 3 }, "image/png",
                CancellationToken.None);

            Assert.Equal(expectedPath, path);
            Assert.Equal("Bearer s1", bearer);
            Assert.Equal("rk-key", recraftKey);
            Assert.False(sentOpenAiKey, "a Recraft nunca recebe nem precisa da chave OpenAI");
            Assert.Equal(new byte[] { 7, 7 }, result.Bytes);
        }

        [Fact]
        public async Task RunAsync_SendsTheImageInTheImageField()
        {
            string? body = null;
            var handler = SucceedingHandler((_, b) => body = b);
            var client = new RecraftClient(handler, Base);

            await client.RunAsync(RecraftOperation.Vectorize, "s1", "k", new byte[] { 9 }, "image/png",
                CancellationToken.None);

            Assert.Contains("name=image", body);
        }

        [Fact]
        public async Task RunAsync_KeepsTheMimeTypeTheServerDownloadReports_SoAnSvgStaysAnSvg()
        {
            var handler = SucceedingHandler(downloadMime: "image/svg+xml", download: Encoding.UTF8.GetBytes("<svg/>"));
            var client = new RecraftClient(handler, Base);

            var result = await client.RunAsync(RecraftOperation.Vectorize, "s1", "k", new byte[] { 9 }, "image/png",
                CancellationToken.None);

            Assert.Equal("image/svg+xml", result.MimeType);
        }

        [Fact]
        public async Task RunAsync_ServerRefusal_KeepsTheServerCodeAndMessage()
        {
            var handler = StubHttpMessageHandler.Json((HttpStatusCode)422,
                @"{""code"":""E_RECRAFT_INVALID_KEY"",""message"":""A chave da Recraft foi recusada.""}");
            var client = new RecraftClient(handler, Base);

            var ex = await Assert.ThrowsAsync<MantosExtractApiException>(() =>
                client.RunAsync(RecraftOperation.RemoveBackground, "s1", "k", new byte[] { 1 }, "image/png",
                    CancellationToken.None));

            Assert.Equal("E_RECRAFT_INVALID_KEY", ex.Code);
        }

        [Fact]
        public async Task RunAsync_NetworkFailure_IsENetwork()
        {
            var client = new RecraftClient(StubHttpMessageHandler.Throwing(new HttpRequestException("dns")), Base);

            var ex = await Assert.ThrowsAsync<MantosExtractApiException>(() =>
                client.RunAsync(RecraftOperation.RemoveBackground, "s1", "k", new byte[] { 1 }, "image/png",
                    CancellationToken.None));

            Assert.Equal("E_NETWORK", ex.Code);
        }

        [Fact]
        public async Task RunAsync_HttpClientTimeout_IsReportedAsATimeout_NotAsACancellation()
        {
            // HttpClient.Timeout expira como TaskCanceledException MESMO sem o operador cancelar —
            // no Bridge isso aparecia como "cancelado". Aqui vira um erro próprio.
            var client = new RecraftClient(StubHttpMessageHandler.Throwing(new TaskCanceledException()), Base);

            var ex = await Assert.ThrowsAsync<MantosExtractApiException>(() =>
                client.RunAsync(RecraftOperation.Vectorize, "s1", "k", new byte[] { 1 }, "image/png",
                    CancellationToken.None));

            Assert.Equal("E_RECRAFT_TIMEOUT", ex.Code);
        }

        [Fact]
        public async Task RunAsync_OperatorCancellation_PropagatesAsCancellation()
        {
            using var cts = new CancellationTokenSource();
            cts.Cancel();
            var client = new RecraftClient(StubHttpMessageHandler.Throwing(new TaskCanceledException()), Base);

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                client.RunAsync(RecraftOperation.Vectorize, "s1", "k", new byte[] { 1 }, "image/png", cts.Token));
        }

        [Fact]
        public async Task RunAsync_ResponseWithoutUrl_IsMalformed()
        {
            var handler = StubHttpMessageHandler.Json(HttpStatusCode.OK, @"{""meta"":{}}");
            var client = new RecraftClient(handler, Base);

            var ex = await Assert.ThrowsAsync<MantosExtractApiException>(() =>
                client.RunAsync(RecraftOperation.RemoveBackground, "s1", "k", new byte[] { 1 }, "image/png",
                    CancellationToken.None));

            Assert.Equal("E_MALFORMED_RESPONSE", ex.Code);
        }
    }
}