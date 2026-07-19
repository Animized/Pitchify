using System.Security.Cryptography;
using System.Text;

namespace Pitchify.Helper;

public static class RequestSecurity
{
    public const string SpotifyOrigin = "https://xpui.app.spotify.com";

    public static bool IsAllowedOrigin(string? origin) =>
        string.IsNullOrEmpty(origin)
        || string.Equals(origin, SpotifyOrigin, StringComparison.Ordinal);

    public static bool IsAuthorized(string? authorizationHeader, string token)
    {
        const string prefix = "Bearer ";
        if (string.IsNullOrWhiteSpace(authorizationHeader)
            || !authorizationHeader.StartsWith(prefix, StringComparison.Ordinal))
        {
            return false;
        }

        var suppliedToken = authorizationHeader[prefix.Length..];
        var suppliedBytes = Encoding.UTF8.GetBytes(suppliedToken);
        var expectedBytes = Encoding.UTF8.GetBytes(token);
        return suppliedBytes.Length == expectedBytes.Length
            && CryptographicOperations.FixedTimeEquals(suppliedBytes, expectedBytes);
    }
}

