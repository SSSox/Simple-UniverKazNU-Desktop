using System;
using Univer.Desktop.Core;
using Xunit;

namespace Univer.Desktop.Tests
{
    public class ResponseCookieTests
    {
        private static readonly DateTime Now = new DateTime(2026, 10, 10, 0, 0, 0, DateTimeKind.Utc);
        private static readonly Uri Url = new Uri("https://univer.kaznu.kz/student/umkd/get/1");
        [Theory]
        [InlineData("sid=; Path=/; Expires=Thu, 01 Jan 1970 00:00:00 GMT")]
        [InlineData("sid=; Path=/; Max-Age=0")]
        [InlineData("sid=expired; Path=/; Max-Age=-1")]
        public void ExpiredCookieRemainsAnExplicitDelta(string header)
        { ResponseCookie delta; Assert.True(ResponseCookie.TryParse(Url, header, Now, out delta)); Assert.Equal("sid", delta.Name); Assert.Equal("/", delta.Path); Assert.True(delta.Expires < Now); }
        [Fact]
        public void MaxAgeOverridesExpiresAndPreservesFlags()
        {
            ResponseCookie delta;
            Assert.True(ResponseCookie.TryParse(Url, "sid=value; Expires=Thu, 01 Jan 1970 00:00:00 GMT; Max-Age=3600; Secure; HttpOnly; SameSite=Strict", Now, out delta));
            Assert.Equal(Now.AddHours(1), delta.Expires); Assert.True(delta.Secure); Assert.True(delta.HttpOnly); Assert.Equal("strict", delta.SameSite);
            Assert.Equal("", delta.Domain); Assert.Equal("/student/umkd/get", delta.Path);
        }
        [Fact]
        public void ExplicitPathAndDomainArePreservedForExactDeletion()
        {
            ResponseCookie delta;
            Assert.True(ResponseCookie.TryParse(Url, "sid=; Domain=.kaznu.kz; Path=/different/path; Max-Age=0", Now, out delta));
            Assert.Equal(".kaznu.kz", delta.Domain); Assert.Equal("/different/path", delta.Path);
        }
        [Theory]
        [InlineData("sid=x; Domain=evil.test")]
        [InlineData("bad name=x")]
        [InlineData("sid=x\r\nInjected: x")]
        [InlineData("missing-equals")]
        public void InvalidOrForeignCookieIsRejected(string header)
        { ResponseCookie delta; Assert.False(ResponseCookie.TryParse(Url, header, Now, out delta)); }
        [Fact]
        public void HugeMaxAgeCannotOverflowTheDate()
        { ResponseCookie delta; Assert.True(ResponseCookie.TryParse(Url, "sid=x; Max-Age=9223372036854775807", Now, out delta)); Assert.Equal(DateTime.MaxValue, delta.Expires); }
    }
}
