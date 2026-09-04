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

            ExtractedImage result = await client.ExtractAsync("session-abc", "sk-key",
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
                client.ExtractAsync("s1", "key", new byte[] { 1 }, "image/png", box, "logo", CancellationToken.None));

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
                client.ExtractAsync("s1", "key", new byte[] { 1 }, "image/png", box, "logo", CancellationToken.None));

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

            await client.ExtractAsync("s1", "key", new byte[] { 9 }, "image/png", box, "logo", CancellationToken.None);

            Assert.Contains("111", capturedPostBody);
            Assert.Contains("222", capturedPostBody);
            Assert.Contains("333", capturedPostBody);
            Assert.Contains("444", capturedPostBody);
            Assert.Contains("logo", capturedPostBody);
        }
    }
}
