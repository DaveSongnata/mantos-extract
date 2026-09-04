using System;
using System.Threading;
using System.Threading.Tasks;
using MantosExtract.Core.Auth;

namespace MantosExtract.Core.Tests.Auth
{
    /// <summary>Scriptable IMantosfcAuthClient — each method returns/throws whatever the test
    /// configured, no real HTTP. Also records calls so tests can assert on call order/args
    /// (e.g. "GetMeAsync must never run after a LogoutAsync").</summary>
    internal sealed class FakeAuthClient : IMantosfcAuthClient
    {
        public Func<string, string, LoginResponse>? OnLogin;
        public Func<string, MeResponse>? OnGetMe;
        public Action<string>? OnLogout;

        public string? LastMeSessionId { get; private set; }
        public int LogoutCallCount { get; private set; }

        public Task<LoginResponse> LoginAsync(string email, string password, CancellationToken ct)
        {
            if (OnLogin == null) throw new InvalidOperationException("OnLogin not configured");
            return Task.FromResult(OnLogin(email, password));
        }

        public Task<MeResponse> GetMeAsync(string sessionId, CancellationToken ct)
        {
            LastMeSessionId = sessionId;
            if (OnGetMe == null) throw new InvalidOperationException("OnGetMe not configured");
            return Task.FromResult(OnGetMe(sessionId));
        }

        public Task LogoutAsync(string sessionId, CancellationToken ct)
        {
            LogoutCallCount++;
            OnLogout?.Invoke(sessionId);
            return Task.CompletedTask;
        }
    }
}
