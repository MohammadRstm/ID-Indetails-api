using ID_Indetails.api.DTO.Request.Notifications;
using ID_Indetails.api.DTO.Request.Push;
using ID_Indetails.api.Enums;
using ID_Indetails.api.Validators.Notifications;
using ID_Indetails.api.Validators.Push;
using Shouldly;

namespace ID_Indetails.UnitTests.Validators;

public class PushValidatorTests
{
    [Theory]
    [InlineData("ExponentPushToken[xxxxxxxxxxxxxxxxxxxxxx]")]
    [InlineData("ExpoPushToken[AbC-123_x]")]
    public void A_device_needs_an_Expo_push_token_and_a_platform(string token)
        => new RegisterDeviceRequestValidator()
            .Validate(new RegisterDeviceRequest { PushToken = token, Platform = DevicePlatform.Android, AppVersion = "1.0.3" })
            .IsValid.ShouldBeTrue();

    [Theory]
    [InlineData("")]
    [InlineData("fcm-token-123")]
    [InlineData("ExponentPushToken[]")]
    [InlineData("ExponentPushToken[has space]")]
    public void Anything_else_is_refused_as_a_push_token(string token)
        => new RegisterDeviceRequestValidator()
            .Validate(new RegisterDeviceRequest { PushToken = token, Platform = DevicePlatform.Ios })
            .Errors.Select(e => e.PropertyName).ShouldBe(["PushToken"]);

    [Fact]
    public void The_platform_is_required_and_the_app_version_short()
    {
        var errors = new RegisterDeviceRequestValidator()
            .Validate(new RegisterDeviceRequest { PushToken = "ExponentPushToken[abc]", Platform = null, AppVersion = new string('1', 33) })
            .Errors.Select(e => e.PropertyName);

        errors.ShouldBe(["Platform", "AppVersion"], ignoreOrder: true);
    }

    [Fact]
    public void Quiet_hours_are_both_set_or_both_empty_minutes_of_the_day_that_differ()
    {
        var validator = new SaveNotificationSettingsRequestValidator();
        bool Valid(int? from, int? to) => validator.Validate(new SaveNotificationSettingsRequest { QuietFromMinute = from, QuietToMinute = to }).IsValid;

        Valid(1320, 420).ShouldBeTrue();
        Valid(null, null).ShouldBeTrue();
        Valid(0, 1439).ShouldBeTrue();
        Valid(1320, null).ShouldBeFalse();
        Valid(null, 420).ShouldBeFalse();
        Valid(-1, 420).ShouldBeFalse();
        Valid(1320, 1440).ShouldBeFalse();
        Valid(600, 600).ShouldBeFalse();
    }

    [Fact]
    public void Each_category_is_muted_once()
        => new SaveNotificationSettingsRequestValidator()
            .Validate(new SaveNotificationSettingsRequest { MutedCategories = [NotificationCategory.Money, NotificationCategory.Money] })
            .Errors.Select(e => e.PropertyName).ShouldBe(["MutedCategories"]);

    /// <summary>A real browser's key and secret: an uncompressed P-256 point, and 16 bytes.</summary>
    private static RegisterWebPushRequest Browser(string endpoint = "https://fcm.googleapis.com/fcm/send/abc:APA91b") => new()
    {
        Endpoint = endpoint,
        P256dh = "BCVxsr7N_eNgVRqvHtD0zTZsEc6-VV-JvLexhqUzORcxaOzi6-AYWXvTBHm4bjyPjs7Vd8pZGH6SRpkNtoIAiw4",
        Auth = "BTBZMqHH6r4Tts7J_aSIgg",
    };

    [Fact]
    public void A_browser_registers_its_push_service_address_and_its_two_keys()
        => new RegisterWebPushRequestValidator().Validate(Browser()).IsValid.ShouldBeTrue();

    [Fact]
    public void An_address_that_isn_t_a_browser_s_push_service_or_keys_that_aren_t_keys_are_refused()
    {
        var validator = new RegisterWebPushRequestValidator();

        validator.Validate(Browser("https://10.0.0.5/internal")).Errors.Select(e => e.PropertyName).ShouldBe(["Endpoint"]);
        validator.Validate(Browser() with { P256dh = "BTBZMqHH6r4Tts7J_aSIgg", Auth = "c2hvcnQ" })
            .Errors.Select(e => e.PropertyName).ShouldBe(["P256dh", "Auth"], ignoreOrder: true);
        validator.Validate(new RegisterWebPushRequest()).Errors.Select(e => e.PropertyName)
            .ShouldBe(["Endpoint", "P256dh", "Auth"], ignoreOrder: true);
    }
}
