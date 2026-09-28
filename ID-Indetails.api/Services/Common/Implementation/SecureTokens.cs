using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.WebUtilities;

namespace ID_Indetails.api.Services.Common.Implementation;

/// <summary>Random secrets and their one-way hashes. Only hashes are ever written to the database.</summary>
public static class SecureTokens
{
    /// <summary>A URL-safe random value with 256 bits of entropy.</summary>
    public static string NewToken(int bytes = 32)
        => WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(bytes));

    /// <summary>Lower-case hex SHA-256 of the value. 64 characters.</summary>
    public static string Hash(string value)
        => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    /// <summary>
    /// A refresh-token pair. The literal token is generated and discarded: the cookie carries
    /// its SHA-256 hash, and the database stores the hash of that cookie value. A copy of the
    /// database therefore contains nothing that can be replayed as a cookie.
    /// </summary>
    public static (string CookieValue, string StoredHash) NewRefreshToken()
    {
        var literal = NewToken(64);
        var cookieValue = WebEncoders.Base64UrlEncode(SHA256.HashData(Encoding.UTF8.GetBytes(literal)));
        return (cookieValue, Hash(cookieValue));
    }

    /// <summary>Compares two secrets in constant time, so timing reveals nothing about the expected value.</summary>
    public static bool FixedTimeEquals(string provided, string expected)
        => CryptographicOperations.FixedTimeEquals(
            SHA256.HashData(Encoding.UTF8.GetBytes(provided)),
            SHA256.HashData(Encoding.UTF8.GetBytes(expected)));
}
