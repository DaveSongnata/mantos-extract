using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using MantosExtract.Core.Api;
using MantosExtract.Core.Detect;
using MantosExtract.Core.Tests.Auth;
using Xunit;

namespace MantosExtract.Core.Tests.Detect
{
    public class DetectionClientTests
    {
        [Fact]
        public async Task DetectAsync_Success_SendsBothCredentialHeaders_AndParsesResult()
        {
            var handler = StubHttpMessageHandler.Json(HttpStatusCode.OK,
                @"{""elements"":[{""id"":""el_1"",""label"":""logo"",""bbox"":{""x_min"":1,""y_min"":2,""x_max"":3,""y_max"":4}}]}");
            var client = new DetectionClient(handler, new Uri("https://mantosfc.test"));

            DetectionResult result = await client.DetectAsync("session-abc", "sk-tenant-key",
                new byte[] { 1, 2, 3 }, "image/png", CancellationToken.None);

            Assert.Single(result.Elements);
            Assert.Equal("session-abc", handler.LastRequest!.Headers.Authorization!.Parameter);
            Assert.Equal("sk-tenant-key", System.Linq.Enumerable.Single(handler.LastRequest.Headers.GetValues("X-OpenAI-Api-Key")));
            Assert.Contains("/api/v1/mantos-extract/detect", handler.LastRequest.RequestUri!.PathAndQuery);
        }

        [Fact]
        public async Task DetectAsync_NoCredits_ThrowsWithServerCode()
        {
            var handler = StubHttpMessageHandler.Json(HttpStatusCode.PaymentRequired,
                @"{""message"":""No credits remaining. Please upgrade your plan."",""code"":""E_NO_CREDITS""}");
            var client = new DetectionClient(handler, new Uri("https://mantosfc.test"));

            MantosExtractApiException ex = await Assert.ThrowsAsync<MantosExtractApiException>(
                () => client.DetectAsync("s1", "key", new byte[] { 1 }, "image/png", CancellationToken.None));

            Assert.Equal("E_NO_CREDITS", ex.Code);
        }

        [Fact]
        public async Task DetectAsync_MissingOpenAiKeyOnServer_ThrowsNotUnauthorized()
        {
            var handler = StubHttpMessageHandler.Json(HttpStatusCode.UnprocessableEntity,
                @"{""message"":""Credencial do Gemini ausente."",""code"":""E_MISSING_OPENAI_KEY""}");
            var client = new DetectionClient(handler, new Uri("https://mantosfc.test"));

            MantosExtractApiException ex = await Assert.ThrowsAsync<MantosExtractApiException>(
                () => client.DetectAsync("s1", "", new byte[] { 1 }, "image/png", CancellationToken.None));

            Assert.Equal("E_MISSING_OPENAI_KEY", ex.Code);
        }
    }
}
