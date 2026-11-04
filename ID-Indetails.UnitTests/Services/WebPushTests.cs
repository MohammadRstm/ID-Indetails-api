using System.Buffers.Text;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ID_Indetails.api.Business.Push;
using ID_Indetails.api.Configuration.Options;
using ID_Indetails.api.Services.Common.Abstraction;
using ID_Indetails.api.Services.Push.Abstraction;
using ID_Indetails.api.Services.Push.Implementation;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Shouldly;

namespace ID_Indetails.UnitTests.Services;

public class WebPushTests
{
    private static readonly DateTime Now = new(2026, 11, 3, 18, 0, 0, DateTimeKind.Utc);

    /// <summary>A browser's subscription on Google's push service, with a key pair the test can decrypt with.</summary>
    private static readonly ECDiffieHellman Browser = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);

    private static readonly byte[] AuthSecret = RandomNumberGenerator.GetBytes(16);

    private static readonly WebPushSubscription Subscription = new(
        "https://fcm.googleapis.com/fcm/send/abc:def",
        Base64Url.EncodeToString(PublicKeyOf(Browser)),
        Base64Url.EncodeToString(AuthSecret));

    private static readonly (string Public, string Private) Vapid = NewVapidKeys();

    private static byte[] B64(string value) => Base64Url.DecodeFromChars(value.Replace(" ", ""));

    [Fact]
    public void The_body_is_encrypted_exactly_as_in_the_example_of_RFC_8291()
    {
        // RFC 8291, section 5 and appendix A
        using var server = ECDiffieHellman.Create(new ECParameters
        {
            Curve = ECCurve.NamedCurves.nistP256,
            D = B64("yfWPiYE-n46HLnH0KqZOF1fJJU3MYrct3AELtAQ-oRw"),
            Q = PointOf(B64("BP4z9KsN6nGRTbVYI_c7VJSPQTBtkgcy27mlmlMoZIIgDll6e3vCYLocInmYWAmS6TlzAC8wEqKK6PBru3jl7A8")),
        });

        var body = WebPushCrypto.Encrypt(
            "When I grow up, I want to be a watermelon"u8.ToArray(),
            B64("BCVxsr7N_eNgVRqvHtD0zTZsEc6-VV-JvLexhqUzORcxaOzi6-AYWXvTBHm4bjyPjs7Vd8pZGH6SRpkNtoIAiw4"),
            B64("BTBZMqHH6r4Tts7J_aSIgg"),
            server,
            B64("DGv6ra1nlYgDCS1FRnbzlw"));

        Base64Url.EncodeToString(body).ShouldBe(
            "DGv6ra1nlYgDCS1FRnbzlwAAEABBBP4z9KsN6nGRTbVYI_c7VJSPQTBtkgcy27ml" +
            "mlMoZIIgDll6e3vCYLocInmYWAmS6TlzAC8wEqKK6PBru3jl7A_yl95bQpu6cVPT" +
            "pK4Mqgkf1CXztLVBSt2Ks3oZwbuwXPXLWyouBWLVWGNWQexSgSxsj_Qulcy4a-fN");
    }

    [Fact]
    public void A_key_that_isn_t_on_the_curve_is_refused()
    {
        var offCurve = PublicKeyOf(Browser);
        offCurve[^1] ^= 1;

        WebPushCrypto.IsBrowserKey(Base64Url.EncodeToString(offCurve)).ShouldBeFalse();
        WebPushCrypto.IsBrowserKey(Subscription.P256dh).ShouldBeTrue();
        WebPushCrypto.IsBrowserKey("not base64url!").ShouldBeFalse();
    }

    [Fact]
    public void The_VAPID_signature_names_the_push_service_and_the_contact_and_checks_against_the_public_key()
    {
        var header = WebPushCrypto.VapidAuthorization("https://fcm.googleapis.com", "https://app.example.com", Now, Vapid.Public, Vapid.Private);

        header.ShouldStartWith("vapid t=");
        header.ShouldEndWith($", k={Vapid.Public}");
        var jwt = header["vapid t=".Length..header.IndexOf(", k=", StringComparison.Ordinal)].Split('.');
        Encoding.UTF8.GetString(B64(jwt[0])).ShouldBe("""{"typ":"JWT","alg":"ES256"}""");
        using var claims = JsonDocument.Parse(B64(jwt[1]));
        claims.RootElement.GetProperty("aud").GetString().ShouldBe("https://fcm.googleapis.com");
        claims.RootElement.GetProperty("sub").GetString().ShouldBe("https://app.example.com");
        claims.RootElement.GetProperty("exp").GetInt64().ShouldBe(new DateTimeOffset(Now.AddHours(12)).ToUnixTimeSeconds());

        using var verifier = ECDsa.Create(new ECParameters { Curve = ECCurve.NamedCurves.nistP256, Q = PointOf(B64(Vapid.Public)) });
        verifier.VerifyData(
                Encoding.ASCII.GetBytes($"{jwt[0]}.{jwt[1]}"), B64(jwt[2]), HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation)
            .ShouldBeTrue();
    }

    [Fact]
    public void Only_two_halves_of_one_VAPID_key_pair_make_a_key_pair()
    {
        WebPushCrypto.IsKeyPair(Vapid.Public, Vapid.Private).ShouldBeTrue();
        WebPushCrypto.IsKeyPair(NewVapidKeys().Public, Vapid.Private).ShouldBeFalse();
        WebPushCrypto.IsKeyPair(Vapid.Public, "too-short").ShouldBeFalse();
    }

    [Fact]
    public async Task A_push_is_posted_encrypted_to_the_browser_s_push_service_and_the_browser_can_read_it()
    {
        var service = new FakePushService(HttpStatusCode.Created);
        var data = new PushData(7, "project_created", 12, PushTarget.Notifications);

        var tickets = await Provider(service).SendAsync([ToBrowser(data, quiet: true)]);

        tickets.ShouldBe([new PushTicket(null, null)]);
        var request = service.Requests.ShouldHaveSingleItem();
        request.Uri.ShouldBe(new Uri(Subscription.Endpoint));
        request.Headers["Content-Encoding"].ShouldBe("aes128gcm");
        request.Headers["TTL"].ShouldBe("86400");
        request.Headers["Urgency"].ShouldBe("normal");
        request.Headers["Authorization"].ShouldEndWith($", k={Vapid.Public}");

        using var payload = JsonDocument.Parse(Decrypt(request.Body));
        payload.RootElement.GetProperty("title").GetString().ShouldBe("New project");
        payload.RootElement.GetProperty("body").GetString().ShouldBe("Test admin added Villa.");
        payload.RootElement.GetProperty("badge").GetInt32().ShouldBe(3);
        payload.RootElement.GetProperty("quiet").GetBoolean().ShouldBeTrue();
        payload.RootElement.GetProperty("data").GetProperty("target").GetProperty("page").GetString().ShouldBe("notifications");
    }

    [Theory]
    [InlineData(HttpStatusCode.Gone, IPushProvider.DeviceNotRegistered)]
    [InlineData(HttpStatusCode.NotFound, IPushProvider.DeviceNotRegistered)]
    [InlineData(HttpStatusCode.TooManyRequests, "Http429")]
    public async Task What_the_push_service_refuses_comes_back_as_the_ticket_s_error(HttpStatusCode status, string error)
        => (await Provider(new FakePushService(status)).SendAsync([ToBrowser()])).ShouldBe([new PushTicket(null, error)]);

    [Fact]
    public async Task Nothing_is_posted_to_an_address_that_isn_t_a_push_service()
    {
        var service = new FakePushService(HttpStatusCode.Created);
        var elsewhere = ToBrowser() with { WebPush = Subscription with { Endpoint = "https://10.0.0.5/admin" } };

        (await Provider(service).SendAsync([elsewhere])).ShouldBe([new PushTicket(null, IPushProvider.DeviceNotRegistered)]);
        service.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task The_router_sends_browsers_through_Web_Push_and_phones_through_Expo_keeping_each_ticket_in_place()
    {
        var push = new FakePushService(HttpStatusCode.Created);
        var expo = new FakePushService(HttpStatusCode.OK, """{"data":[{"status":"ok","id":"t1"},{"status":"ok","id":"t2"}]}""");
        var router = new PushRouter(
            new ExpoPushProvider(new HttpClient(expo), Options.Create(new PushOptions()), NullLogger<ExpoPushProvider>.Instance),
            Provider(push));
        var phone = ToBrowser() with { To = "ExponentPushToken[a]", WebPush = null };

        var tickets = await router.SendAsync([phone, ToBrowser(), phone with { To = "ExponentPushToken[b]" }]);

        tickets.ShouldBe([new PushTicket("t1", null), new PushTicket(null, null), new PushTicket("t2", null)]);
        push.Requests.Count.ShouldBe(1);
        expo.Requests.Count.ShouldBe(1);
    }

    private static WebPushProvider Provider(FakePushService service)
    {
        var clock = new Mock<IClock>();
        clock.Setup(c => c.UtcNow).Returns(Now);
        return new WebPushProvider(
            new HttpClient(service),
            Options.Create(new PushOptions { Enabled = true, WebPush = new WebPushOptions { PublicKey = Vapid.Public, PrivateKey = Vapid.Private } }),
            Options.Create(new FrontendOptions { BaseUrl = "https://app.example.com" }),
            clock.Object);
    }

    private static PushMessage ToBrowser(PushData? data = null, bool quiet = false) => new(
        WebPushEndpoints.PushTokenOf(Subscription.Endpoint),
        "New project",
        "Test admin added Villa.",
        data ?? new PushData(7, "project_created", 12, PushTarget.Notifications),
        Sound: !quiet,
        Badge: 3,
        "projects",
        quiet,
        Subscription);

    /// <summary>What the browser does with a push (RFC 8291), with its own private key.</summary>
    private static byte[] Decrypt(byte[] body)
    {
        var salt = body[..16];
        var serverPublicKey = body[21..86];
        using var server = ECDiffieHellman.Create(new ECParameters { Curve = ECCurve.NamedCurves.nistP256, Q = PointOf(serverPublicKey) });
        var shared = Browser.DeriveRawSecretAgreement(server.PublicKey);
        byte[] keyInfo = [.. "WebPush: info\0"u8, .. PublicKeyOf(Browser), .. serverPublicKey];
        var ikm = HKDF.DeriveKey(HashAlgorithmName.SHA256, shared, 32, AuthSecret, keyInfo);
        var key = HKDF.DeriveKey(HashAlgorithmName.SHA256, ikm, 16, salt, "Content-Encoding: aes128gcm\0"u8.ToArray());
        var nonce = HKDF.DeriveKey(HashAlgorithmName.SHA256, ikm, 12, salt, "Content-Encoding: nonce\0"u8.ToArray());

        var ciphertext = body[86..^16];
        var plaintext = new byte[ciphertext.Length];
        using var aes = new AesGcm(key, 16);
        aes.Decrypt(nonce, ciphertext, body[^16..], plaintext);
        plaintext[^1].ShouldBe((byte)2);
        return plaintext[..^1];
    }

    private static byte[] PublicKeyOf(ECDiffieHellman key)
    {
        var q = key.ExportParameters(false).Q;
        return [4, .. q.X!, .. q.Y!];
    }

    private static ECPoint PointOf(byte[] uncompressed) => new() { X = uncompressed[1..33], Y = uncompressed[33..] };

    private static (string Public, string Private) NewVapidKeys()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var parameters = key.ExportParameters(true);
        return (Base64Url.EncodeToString([4, .. parameters.Q.X!, .. parameters.Q.Y!]), Base64Url.EncodeToString(parameters.D));
    }

    private sealed record SentRequest(Uri Uri, Dictionary<string, string> Headers, byte[] Body);

    /// <summary>A push service that answers every request with <paramref name="status"/>, and keeps what it was sent.</summary>
    private sealed class FakePushService(HttpStatusCode status, string answer = "") : HttpMessageHandler
    {
        public List<SentRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var headers = request.Headers.Concat(request.Content!.Headers).ToDictionary(h => h.Key, h => string.Join(",", h.Value));
            Requests.Add(new SentRequest(request.RequestUri!, headers, await request.Content.ReadAsByteArrayAsync(cancellationToken)));
            return new HttpResponseMessage(status) { Content = new StringContent(answer, Encoding.UTF8, "application/json") };
        }
    }
}
