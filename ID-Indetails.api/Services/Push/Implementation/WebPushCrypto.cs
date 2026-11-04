using System.Buffers.Binary;
using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ID_Indetails.api.Services.Push.Implementation;

/// <summary>
/// Web Push's cryptography, on .NET's own: a push's body is encrypted to the browser (RFC 8291, <c>aes128gcm</c>), so
/// the push service carrying it can't read it, and signed for with the server's VAPID key (RFC 8292), so the push
/// service only accepts pushes from this server. Keys are base64url, as browsers and VAPID tools write them.
/// </summary>
public static class WebPushCrypto
{
    private const int SaltLength = 16;
    private const int TagLength = 16;
    private const int RecordSize = 4096;
    private const byte LastRecordDelimiter = 2;

    /// <summary>How long a VAPID signature is good for. Push services refuse more than a day.</summary>
    private static readonly TimeSpan SignatureLifetime = TimeSpan.FromHours(12);

    private static readonly ECCurve P256 = ECCurve.NamedCurves.nistP256;

    /// <summary>Encrypts <paramref name="plaintext"/> to the browser's public key and authentication secret.</summary>
    public static byte[] Encrypt(byte[] plaintext, byte[] browserPublicKey, byte[] authSecret)
    {
        using var serverKey = ECDiffieHellman.Create(P256);
        return Encrypt(plaintext, browserPublicKey, authSecret, serverKey, RandomNumberGenerator.GetBytes(SaltLength));
    }

    /// <summary>The same with the server's one-off key and the salt given, which only the RFC's example needs.</summary>
    public static byte[] Encrypt(byte[] plaintext, byte[] browserPublicKey, byte[] authSecret, ECDiffieHellman serverKey, byte[] salt)
    {
        // importing the browser's key checks that it is a point on the curve, as RFC 8291 requires
        using var browserKey = ECDiffieHellman.Create(new ECParameters { Curve = P256, Q = PointOf(browserPublicKey) });
        var serverPublicKey = BytesOf(serverKey.ExportParameters(false).Q);
        var sharedSecret = serverKey.DeriveRawSecretAgreement(browserKey.PublicKey);

        byte[] keyInfo = [.. "WebPush: info\0"u8, .. browserPublicKey, .. serverPublicKey];
        var ikm = HKDF.DeriveKey(HashAlgorithmName.SHA256, sharedSecret, 32, authSecret, keyInfo);
        var contentKey = HKDF.DeriveKey(HashAlgorithmName.SHA256, ikm, 16, salt, "Content-Encoding: aes128gcm\0"u8.ToArray());
        var nonce = HKDF.DeriveKey(HashAlgorithmName.SHA256, ikm, 12, salt, "Content-Encoding: nonce\0"u8.ToArray());

        // one record: the text, then the last record's delimiter, without padding
        byte[] record = [.. plaintext, LastRecordDelimiter];
        var ciphertext = new byte[record.Length];
        var tag = new byte[TagLength];
        using (var aes = new AesGcm(contentKey, TagLength))
        {
            aes.Encrypt(nonce, record, ciphertext, tag);
        }

        // the header: the salt, the record size and the server's one-off public key, which the browser needs to decrypt
        var recordSize = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(recordSize, RecordSize);
        return [.. salt, .. recordSize, (byte)serverPublicKey.Length, .. serverPublicKey, .. ciphertext, .. tag];
    }

    /// <summary>
    /// The <c>Authorization</c> header of a push to the push service at <paramref name="audience"/> (its origin): a JWT
    /// naming the server's contact (<paramref name="subject"/>), signed with its private key, and the public key it's
    /// checked against.
    /// </summary>
    public static string VapidAuthorization(string audience, string subject, DateTime now, string publicKey, string privateKey)
    {
        var header = Base64Url.EncodeToString("""{"typ":"JWT","alg":"ES256"}"""u8);
        var expires = new DateTimeOffset(now.Add(SignatureLifetime), TimeSpan.Zero).ToUnixTimeSeconds();
        var claims = Base64Url.EncodeToString(JsonSerializer.SerializeToUtf8Bytes(new { aud = audience, exp = expires, sub = subject }));
        var unsigned = $"{header}.{claims}";

        using var key = ECDsa.Create(new ECParameters
        {
            Curve = P256,
            D = Base64Url.DecodeFromChars(privateKey),
            Q = PointOf(Base64Url.DecodeFromChars(publicKey)),
        });
        var signature = key.SignData(Encoding.ASCII.GetBytes(unsigned), HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        return $"vapid t={unsigned}.{Base64Url.EncodeToString(signature)}, k={publicKey}";
    }

    /// <summary>Whether the two keys are a VAPID key pair: the private key signs what the public key verifies.</summary>
    public static bool IsKeyPair(string publicKey, string privateKey)
    {
        try
        {
            var point = PointOf(Base64Url.DecodeFromChars(publicKey));
            var data = "id-indetails"u8.ToArray();
            using var signer = ECDsa.Create(new ECParameters { Curve = P256, D = Base64Url.DecodeFromChars(privateKey), Q = point });
            using var verifier = ECDsa.Create(new ECParameters { Curve = P256, Q = point });
            return verifier.VerifyData(data, signer.SignData(data, HashAlgorithmName.SHA256), HashAlgorithmName.SHA256);
        }
        catch (Exception e) when (e is CryptographicException or FormatException or ArgumentException or PlatformNotSupportedException)
        {
            return false;
        }
    }

    /// <summary>Whether <paramref name="key"/> is a browser's public key: base64url, an uncompressed point on P-256.</summary>
    public static bool IsBrowserKey(string key)
    {
        try
        {
            using var _ = ECDiffieHellman.Create(new ECParameters { Curve = P256, Q = PointOf(Base64Url.DecodeFromChars(key)) });
            return true;
        }
        catch (Exception e) when (e is CryptographicException or FormatException or ArgumentException or PlatformNotSupportedException)
        {
            return false;
        }
    }

    /// <summary>An uncompressed point (65 bytes: 4, then x and y) as .NET takes it.</summary>
    private static ECPoint PointOf(byte[] uncompressed)
        => uncompressed is [4, ..] && uncompressed.Length == 65
            ? new ECPoint { X = uncompressed[1..33], Y = uncompressed[33..] }
            : throw new ArgumentException("Not an uncompressed P-256 point.", nameof(uncompressed));

    private static byte[] BytesOf(ECPoint point) => [4, .. point.X!, .. point.Y!];
}
