using ID_Indetails.api.DTO.Request.Push;
using ID_Indetails.api.Enums;
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
}
