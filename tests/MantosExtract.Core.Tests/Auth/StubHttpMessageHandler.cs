using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace MantosExtract.Core.Tests.Auth
{
    /// <summary>Records the last request and answers with a canned response — no real network,
    /// used to exercise MantosfcAuthClient's HTTP plumbing (status mapping, error parsing,
    /// Bearer header) without a live mantosfc.</summary>
    internal sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;

        public HttpRequestMessage? LastRequest { get; private set; }
        public string? LastRequestBody { get; private set; }

        public StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> respond)
        {
            _respond = respond;
        }

        public static StubHttpMessageHandler Json(HttpStatusCode status, string body) =>
            new StubHttpMessageHandler(_ => new HttpResponseMessage(status)
            {
                Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json"),
            });

        public static StubHttpMessageHandler Throwing(Exception exception) =>
            new StubHttpMessageHandler(_ => throw exception);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            LastRequestBody = request.Content != null
                ? await request.Content.ReadAsStringAsync().ConfigureAwait(false)
                : null;
            return _respond(request);
        }
    }
}
