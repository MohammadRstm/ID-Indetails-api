using ID_Indetails.api.Business.Auth;
using ID_Indetails.api.Enums;
using Shouldly;

namespace ID_Indetails.UnitTests.Business;

public class PasswordResetRulesTests
{
    private static readonly DateTime Now = new(2026, 9, 29, 12, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(UserStatus.Active, true)]
    [InlineData(UserStatus.Invited, false)]
    [InlineData(UserStatus.Deactivated, false)]
    public void Only_active_accounts_reset_their_password(UserStatus status, bool canReset)
        => PasswordResetRules.CanReset(status).ShouldBe(canReset);

    [Fact]
    public void A_link_works_once_until_it_expires()
    {
        PasswordResetRules.IsUsable(null, Now.AddMinutes(1), UserStatus.Active, Now).ShouldBeTrue();
        PasswordResetRules.IsUsable(Now.AddMinutes(-5), Now.AddMinutes(1), UserStatus.Active, Now).ShouldBeFalse();
        PasswordResetRules.IsUsable(null, Now, UserStatus.Active, Now).ShouldBeFalse();
        PasswordResetRules.IsUsable(null, Now.AddMinutes(-1), UserStatus.Active, Now).ShouldBeFalse();
    }

    [Fact]
    public void A_link_stops_working_once_the_account_is_deactivated()
        => PasswordResetRules.IsUsable(null, Now.AddMinutes(10), UserStatus.Deactivated, Now).ShouldBeFalse();
}
