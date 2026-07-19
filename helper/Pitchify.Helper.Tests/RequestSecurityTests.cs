namespace Pitchify.Helper.Tests;

public sealed class RequestSecurityTests
{
    [Fact]
    public void AllowsOnlySpotifyOrRequestsWithoutAnOrigin()
    {
        Assert.True(RequestSecurity.IsAllowedOrigin(null));
        Assert.True(RequestSecurity.IsAllowedOrigin(RequestSecurity.SpotifyOrigin));
        Assert.False(RequestSecurity.IsAllowedOrigin("https://evil.example"));
    }

    [Fact]
    public void UsesExactBearerToken()
    {
        Assert.True(RequestSecurity.IsAuthorized("Bearer secret", "secret"));
        Assert.False(RequestSecurity.IsAuthorized("Bearer wrong", "secret"));
        Assert.False(RequestSecurity.IsAuthorized("secret", "secret"));
        Assert.False(RequestSecurity.IsAuthorized(null, "secret"));
    }
}

