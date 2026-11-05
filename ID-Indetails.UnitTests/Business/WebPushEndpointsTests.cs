using ID_Indetails.api.Business.Push;
using Shouldly;

namespace ID_Indetails.UnitTests.Business;

public class WebPushEndpointsTests
{
    [Theory]
    [InlineData("https://fcm.googleapis.com/fcm/send/dQw4:APA91bH")]
    [InlineData("https://web.push.apple.com/QGuQyavXutnMH")]
    [InlineData("https://updates.push.services.mozilla.com/wpush/v2/gAAAAAB")]
    [InlineData("https://wns2-par02p.notify.windows.com/w/?token=BQYAAAD")]
    public void The_browsers_own_push_services_are_allowed(string endpoint)
        => WebPushEndpoints.IsAllowed(endpoint).ShouldBeTrue();

    [Theory]
    [InlineData("http://fcm.googleapis.com/fcm/send/x")]
    [InlineData("https://fcm.googleapis.com:8443/fcm/send/x")]
    [InlineData("https://user@fcm.googleapis.com/fcm/send/x")]
    [InlineData("https://fcm.googleapis.com.evil.example/x")]
    [InlineData("https://evilnotify.windows.com/w/")]
    [InlineData("https://localhost/api/auth")]
    [InlineData("https://169.254.169.254/latest/meta-data")]
    [InlineData("fcm.googleapis.com/fcm/send/x")]
    [InlineData("")]
    public void Any_other_address_is_refused(string endpoint)
        => WebPushEndpoints.IsAllowed(endpoint).ShouldBeFalse();

    [Fact]
    public void The_push_token_is_the_endpoint_s_SHA_256_so_one_subscription_is_one_device()
    {
        var token = WebPushEndpoints.PushTokenOf("https://fcm.googleapis.com/fcm/send/a");

        token.Length.ShouldBe(64);
        token.ShouldBe(WebPushEndpoints.PushTokenOf("https://fcm.googleapis.com/fcm/send/a"));
        token.ShouldNotBe(WebPushEndpoints.PushTokenOf("https://fcm.googleapis.com/fcm/send/b"));
    }
}
