using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;

namespace ID_Indetails.api.Services.ContentStore.Implementation;

/// <summary>What a signed link of the local store allows. The upload fields are for a part upload, the file fields for a download.</summary>
internal sealed record LocalLink(
    string Operation,
    string Key,
    long Expires,
    string? UploadId = null,
    int Part = 0,
    long Max = 0,
    string? FileName = null,
    string? ContentType = null,
    bool Inline = false);

/// <summary>
/// Signs the local store's links, which stand in for presigned S3 links: <c>base64url(payload).base64url(HMAC-SHA256)</c>.
/// A link made for one operation is refused for the other.
/// </summary>
internal sealed class LocalLinkSigner(string signingKey)
{
    public const string PutOperation = "put";
    public const string GetOperation = "get";

    private readonly byte[] _key = signingKey.Length > 0 ? Encoding.UTF8.GetBytes(signingKey) : RandomNumberGenerator.GetBytes(32);

    public string Sign(LocalLink link)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(link);
        return $"{WebEncoders.Base64UrlEncode(payload)}.{WebEncoders.Base64UrlEncode(HMACSHA256.HashData(_key, payload))}";
    }

    /// <summary>The link's content, or null when the token isn't ours, was changed, has expired or is for another operation.</summary>
    public LocalLink? Verify(string token, string operation, DateTime now)
    {
        var parts = token.Split('.');
        if (parts.Length != 2)
        {
            return null;
        }

        try
        {
            var payload = WebEncoders.Base64UrlDecode(parts[0]);
            var signature = WebEncoders.Base64UrlDecode(parts[1]);
            if (!CryptographicOperations.FixedTimeEquals(HMACSHA256.HashData(_key, payload), signature))
            {
                return null;
            }

            var link = JsonSerializer.Deserialize<LocalLink>(payload);
            return link is not null && link.Operation == operation && link.Expires > ToUnixSeconds(now) ? link : null;
        }
        catch (Exception ex) when (ex is FormatException or JsonException)
        {
            return null;
        }
    }

    public static long ToUnixSeconds(DateTime utc) => new DateTimeOffset(DateTime.SpecifyKind(utc, DateTimeKind.Utc)).ToUnixTimeSeconds();
}
