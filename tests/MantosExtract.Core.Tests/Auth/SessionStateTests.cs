using System;
using MantosExtract.Core.Auth;
using Xunit;

namespace MantosExtract.Core.Tests.Auth
{
    public class SessionStateTests
    {
        [Fact]
        public void IsExpired_BeforeExpiry_ReturnsFalse()
        {
            var session = new SessionState("s1", new DateTimeOffset(2026, 9, 10, 0, 0, 0, TimeSpan.Zero), "tenant", "a@b.com", 10);
            var now = new DateTimeOffset(2026, 9, 5, 0, 0, 0, TimeSpan.Zero);

            Assert.False(session.IsExpired(now));
        }

        [Fact]
        public void IsExpired_AfterExpiry_ReturnsTrue()
        {
            var session = new SessionState("s1", new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero), "tenant", "a@b.com", 10);
            var now = new DateTimeOffset(2026, 9, 5, 0, 0, 0, TimeSpan.Zero);

            Assert.True(session.IsExpired(now));
        }

        [Fact]
        public void IsExpired_ExactlyAtExpiry_ReturnsTrue()
        {
            // >= , not > : mantosfc's session_service.ts checks expires_at > now() to consider a
            // session valid, so the boundary instant itself must already read as expired here.
            var expiresAt = new DateTimeOffset(2026, 9, 5, 12, 0, 0, TimeSpan.Zero);
            var session = new SessionState("s1", expiresAt, "tenant", "a@b.com", 10);

            Assert.True(session.IsExpired(expiresAt));
        }
    }
}
