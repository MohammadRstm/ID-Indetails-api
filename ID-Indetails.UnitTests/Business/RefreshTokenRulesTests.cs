using ID_Indetails.api.Business.Auth;
using ID_Indetails.api.Enums;
using Shouldly;

namespace ID_Indetails.UnitTests.Business;

public class RefreshTokenRulesTests
{
    [Theory]
    [InlineData(RefreshTokenClient.Web, false, RefreshTokenClient.Web)]
    [InlineData(RefreshTokenClient.Web, true, RefreshTokenClient.Installed)]
    [InlineData(RefreshTokenClient.Installed, true, RefreshTokenClient.Installed)]
    [InlineData(RefreshTokenClient.Installed, false, RefreshTokenClient.Installed)]
    public void A_website_session_becomes_the_installed_apps_once_the_installed_app_uses_it_and_stays_so(
        RefreshTokenClient current, bool fromInstalledApp, RefreshTokenClient next)
        => RefreshTokenRules.NextWebClient(current, fromInstalledApp).ShouldBe(next);
}
