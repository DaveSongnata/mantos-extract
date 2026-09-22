using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MantosExtract.Core.Api;
using MantosExtract.Core.Detect;
using MantosExtract.Core.Extract;
using MantosExtract.Core.Tests.Auth;
using Xunit;

namespace MantosExtract.Core.Tests.Extract
{
    public class ExtractionClientTests
    {
        [Fact]
        public async Task ExtractAsync_Success_PostsThenDownloads_ReturnsBytes()
        {
            byte[] pngBytes = { 0x89, 0x50, 0x4E, 0x47 }; // PNG magic bytes, doesn't need to be a real image for this test
            var handler = new StubHttpMessageHandler(req =>
            {
                if (req.Method == HttpMethod.Post)
                {
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(
                            @"{""url"":""https://mantosfc.test/api/v1/images/abc"",""meta"":{}}",
                            Encoding.UTF8, "application/json"),
                    };
                }
                var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(pngBytes) };
                response.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");
                return response;
            });
            var client = new ExtractionClient(handler, new Uri("https://mantosfc.test"));
            var box = new BoundingBox(10, 20, 300, 400);

            ExtractedImage result = await client.ExtractAsync("session-abc", "sk-key", "medium",
                new byte[] { 1, 2, 3 }, "image/png", box, "logo", CancellationToken.None);

            Assert.Equal(pngBytes, result.Bytes);
            Assert.Equal("image/png", result.MimeType);
            Assert.Equal("https://mantosfc.test/api/v1/images/abc", result.SourceUrl);
        }

        [Fact]
        public async Task ExtractAsync_PostFails_NeverAttemptsDownload()
        {
            bool downloadAttempted = false;
            var handler = new StubHttpMessageHandler(req =>
            {
                if (req.Method == HttpMethod.Get) downloadAttempted = true;
                return new HttpResponseMessage(HttpStatusCode.UnprocessableEntity)
                {
                    Content = new StringContent(@"{""message"":""Não foi possível extrair."",""code"":""E_EXTRACTION_FAILED""}",
                        Encoding.UTF8, "application/json"),
                };
            });
            var client = new ExtractionClient(handler, new Uri("https://mantosfc.test"));
            var box = new BoundingBox(10, 20, 300, 400);

            await Assert.ThrowsAsync<MantosExtractApiException>(() =>
                client.ExtractAsync("s1", "key", "medium", new byte[] { 1 }, "image/png", box, "logo", CancellationToken.None));

            Assert.False(downloadAttempted);
        }

        [Fact]
        public async Task ExtractAsync_DownloadFails_ThrowsDownloadFailed()
        {
            var handler = new StubHttpMessageHandler(req =>
            {
                if (req.Method == HttpMethod.Post)
                {
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(@"{""url"":""https://mantosfc.test/api/v1/images/abc""}",
                            Encoding.UTF8, "application/json"),
                    };
                }
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            });
            var client = new ExtractionClient(handler, new Uri("https://mantosfc.test"));
            var box = new BoundingBox(10, 20, 300, 400);

            MantosExtractApiException ex = await Assert.ThrowsAsync<MantosExtractApiException>(() =>
                client.ExtractAsync("s1", "key", "medium", new byte[] { 1 }, "image/png", box, "logo", CancellationToken.None));

            Assert.Equal("E_DOWNLOAD_FAILED", ex.Code);
        }

        [Fact]
        public async Task ExtractAsync_SendsBoundingBoxAndLabelAsFormFields()
        {
            // GET (download) runs AFTER the POST and would overwrite a generic "last request"
            // capture, so the POST body is captured from inside the handler itself.
            string? capturedPostBody = null;
            var handler = new StubHttpMessageHandler(req =>
            {
                if (req.Method == HttpMethod.Post)
                {
                    capturedPostBody = req.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(@"{""url"":""https://mantosfc.test/api/v1/images/abc""}",
                            Encoding.UTF8, "application/json"),
                    };
                }
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(new byte[] { 1 }) };
            });
            var client = new ExtractionClient(handler, new Uri("https://mantosfc.test"));
            var box = new BoundingBox(111, 222, 333, 444);

            await client.ExtractAsync("s1", "key", "medium", new byte[] { 9 }, "image/png", box, "logo", CancellationToken.None);

            Assert.Contains("111", capturedPostBody);
            Assert.Contains("222", capturedPostBody);
            Assert.Contains("333", capturedPostBody);
            Assert.Contains("444", capturedPostBody);
            Assert.Contains("logo", capturedPostBody);
        }

        [Fact]
        public async Task ExtractAsync_SendsPresetNameAsHeader()
        {
            string? capturedQualityHeader = null;
            var handler = new StubHttpMessageHandler(req =>
            {
                if (req.Method == HttpMethod.Post)
                {
                    capturedQualityHeader = req.Headers.TryGetValues("X-Extract-Preset", out var vals)
                        ? string.Join(",", vals) : null;
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(@"{""url"":""https://mantosfc.test/api/v1/images/abc""}",
                            Encoding.UTF8, "application/json"),
                    };
                }
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(new byte[] { 1 }) };
            });
            var client = new ExtractionClient(handler, new Uri("https://mantosfc.test"));
            var box = new BoundingBox(1, 2, 3, 4);

            await client.ExtractAsync("s1", "key", "high", new byte[] { 9 }, "image/png", box, "logo", CancellationToken.None);

            Assert.Equal("high", capturedQualityHeader);
        }

        [Fact]
        public async Task ExtractAsync_BlankOrUnknownPreset_DefaultsToMediumHeader()
        {
            string? capturedQualityHeader = null;
            var handler = new StubHttpMessageHandler(req =>
            {
                if (req.Method == HttpMethod.Post)
                {
                    capturedQualityHeader = req.Headers.TryGetValues("X-Extract-Preset", out var vals)
                        ? string.Join(",", vals) : null;
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(@"{""url"":""https://mantosfc.test/api/v1/images/abc""}",
                            Encoding.UTF8, "application/json"),
                    };
                }
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(new byte[] { 1 }) };
            });
            var client = new ExtractionClient(handler, new Uri("https://mantosfc.test"));
            var box = new BoundingBox(1, 2, 3, 4);

            await client.ExtractAsync("s1", "key", "", new byte[] { 9 }, "image/png", box, "logo", CancellationToken.None);

            Assert.Equal("medium", capturedQualityHeader);
        }

        [Fact]
        public async Task ExtractAsync_LegacyModelFlag_SendsFlagHeaderNeverAModelName()
        {
            var headers = new System.Collections.Generic.Dictionary<string, string>();
            var handler = new StubHttpMessageHandler(req =>
            {
                if (req.Method == HttpMethod.Post)
                {
                    foreach (var h in req.Headers) headers[h.Key] = string.Join(",", h.Value);
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(@"{""url"":""https://mantosfc.test/api/v1/images/abc""}",
                            Encoding.UTF8, "application/json"),
                    };
                }
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(new byte[] { 1 }) };
            });
            var client = new ExtractionClient(handler, new Uri("https://mantosfc.test"));

            await client.ExtractAsync("s1", "key", "medium", new byte[] { 9 }, "image/png",
                new BoundingBox(1, 2, 3, 4), "logo", CancellationToken.None, legacyModel: true);

            Assert.Equal("1", headers["X-Extract-Legacy-Model"]);
            Assert.False(headers.ContainsKey("X-OpenAI-Model"), "o cliente nunca manda nome de modelo");
        }

        [Fact]
        public async Task ExtractAsync_DefaultsToNoLegacyHeader()
        {
            bool hasLegacyHeader = true;
            var handler = new StubHttpMessageHandler(req =>
            {
                if (req.Method == HttpMethod.Post)
                {
                    hasLegacyHeader = req.Headers.Contains("X-Extract-Legacy-Model");
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(@"{""url"":""https://mantosfc.test/api/v1/images/abc""}",
                            Encoding.UTF8, "application/json"),
                    };
                }
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(new byte[] { 1 }) };
            });
            var client = new ExtractionClient(handler, new Uri("https://mantosfc.test"));

            await client.ExtractAsync("s1", "key", "medium", new byte[] { 9 }, "image/png",
                new BoundingBox(1, 2, 3, 4), "logo", CancellationToken.None);

            Assert.False(hasLegacyHeader);
        }

        [Fact]
        public async Task ExtractAsync_ModelUnavailable_SurfacesServerCodeAndMessage()
        {
            var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage((HttpStatusCode)422)
            {
                Content = new StringContent(
                    @"{""code"":""E_OPENAI_MODEL_UNAVAILABLE"",""message"":""Sua chave ainda nao tem acesso.""}",
                    Encoding.UTF8, "application/json"),
            });
            var client = new ExtractionClient(handler, new Uri("https://mantosfc.test"));

            var ex = await Assert.ThrowsAsync<MantosExtractApiException>(() =>
                client.ExtractAsync("s1", "key", "medium", new byte[] { 1 }, "image/png",
                    new BoundingBox(1, 2, 3, 4), "logo", CancellationToken.None));

            Assert.Equal("E_OPENAI_MODEL_UNAVAILABLE", ex.Code);
            Assert.True(ex.IsOpenAiModelUnavailable);
            Assert.Equal("Sua chave ainda nao tem acesso.", ex.Message);
        }

        [Fact]
        public async Task RefineAsync_PostsImageAndInstructionToTheRefineEndpoint_ThenDownloadsTheResult()
        {
            string? path = null, body = null, preset = null, bearer = null;
            var handler = new StubHttpMessageHandler(req =>
            {
                if (req.Method == HttpMethod.Post)
                {
                    path = req.RequestUri!.AbsolutePath;
                    body = req.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
                    preset = req.Headers.TryGetValues("X-Extract-Preset", out var p) ? string.Join(",", p) : null;
                    bearer = req.Headers.Authorization?.ToString();
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(@"{""url"":""https://mantosfc.test/api/v1/images/refined""}",
                            Encoding.UTF8, "application/json"),
                    };
                }
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(new byte[] { 7, 7 }) };
            });
            var client = new ExtractionClient(handler, new Uri("https://mantosfc.test"));

            ExtractedImage result = await client.RefineAsync("s1", "sk-key", "max", new byte[] { 9, 9, 9 },
                "image/png", "remove a pessoa de dentro do carro", null, CancellationToken.None);

            Assert.Equal("/api/v1/mantos-extract/refine", path);
            Assert.Contains("remove a pessoa de dentro do carro", body);
            Assert.Contains("name=instruction", body);
            Assert.Contains("name=image", body);
            Assert.DoesNotContain("name=reference", body);
            Assert.Equal("max", preset);
            Assert.Equal("Bearer s1", bearer);
            Assert.Equal(new byte[] { 7, 7 }, result.Bytes);
        }

        [Fact]
        public async Task RefineAsync_WithReference_SendsItAsItsOwnPart_NextToTheImage()
        {
            string? body = null;
            var handler = new StubHttpMessageHandler(req =>
            {
                if (req.Method == HttpMethod.Post)
                {
                    body = req.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(@"{""url"":""https://mantosfc.test/api/v1/images/r""}",
                            Encoding.UTF8, "application/json"),
                    };
                }
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(new byte[] { 1 }) };
            });
            var client = new ExtractionClient(handler, new Uri("https://mantosfc.test"));

            await client.RefineAsync("s1", "k", "medium", new byte[] { 1 }, "image/png", "ajeite a posição do Zeus",
                new byte[] { 2 }, CancellationToken.None);

            Assert.Contains("name=image", body);
            Assert.Contains("name=reference", body);
            Assert.Contains("filename=referencia.png", body);
        }

        [Fact]
        public async Task RefineAsync_LegacyFlag_IsSentAsFlagHeader()
        {
            string? legacy = null;
            var handler = new StubHttpMessageHandler(req =>
            {
                if (req.Method == HttpMethod.Post)
                {
                    legacy = req.Headers.TryGetValues("X-Extract-Legacy-Model", out var v) ? string.Join(",", v) : null;
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(@"{""url"":""https://mantosfc.test/api/v1/images/r""}",
                            Encoding.UTF8, "application/json"),
                    };
                }
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(new byte[] { 1 }) };
            });
            var client = new ExtractionClient(handler, new Uri("https://mantosfc.test"));

            await client.RefineAsync("s1", "k", "medium", new byte[] { 1 }, "image/png", "pinte de azul",
                null, CancellationToken.None, legacyModel: true);

            Assert.Equal("1", legacy);
        }

        [Fact]
        public async Task RefineAsync_ServerModerationRefusal_KeepsCodeAndMessage()
        {
            var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage((HttpStatusCode)422)
            {
                Content = new StringContent(
                    @"{""code"":""E_OPENAI_MODERATION"",""message"":""A OpenAI recusou.""}",
                    Encoding.UTF8, "application/json"),
            });
            var client = new ExtractionClient(handler, new Uri("https://mantosfc.test"));

            var ex = await Assert.ThrowsAsync<MantosExtractApiException>(() =>
                client.RefineAsync("s1", "k", "medium", new byte[] { 1 }, "image/png", "faz algo",
                    null, CancellationToken.None));

            Assert.Equal("E_OPENAI_MODERATION", ex.Code);
            Assert.Equal("A OpenAI recusou.", ex.Message);
        }
    }
}