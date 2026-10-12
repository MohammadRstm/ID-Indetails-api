using ID_Indetails.api.Business.Auth;
using ID_Indetails.api.Enums;
using Shouldly;
using Token = ID_Indetails.api.Business.Auth.MobileRefreshRules.Token;

namespace ID_Indetails.UnitTests.Business;

public class MobileRefreshRulesTests
{
    private static readonly DateTime Now = new(2026, 10, 8, 12, 0, 0, DateTimeKind.Utc);
    private static readonly TimeSpan Grace = TimeSpan.FromMinutes(5);

    private static Token Valid(int expiresInDays = 30) => new(Now.AddDays(expiresInDays), null, null);

    private static Token Revoked(RevokeReason reason, TimeSpan ago) => new(Now.AddDays(29), Now - ago, reason);

    private static MobileRefreshCase Decide(Token presented, params Token[] after) => MobileRefreshRules.Decide(presented, after, Now, Grace);

    [Fact]
    public void A_valid_token_is_rotated()
        => Decide(Valid()).ShouldBe(MobileRefreshCase.Rotate);

    [Fact]
    public void An_expired_token_is_refused()
        => Decide(new Token(Now, null, null)).ShouldBe(MobileRefreshCase.Refused);

    [Fact]
    public void A_rotated_token_presented_again_inside_the_grace_is_a_lost_answer()
        => Decide(Revoked(RevokeReason.Rotated, TimeSpan.FromMinutes(1)), Valid()).ShouldBe(MobileRefreshCase.LostAnswerRetry);

    [Fact]
    public void Repeated_retries_inside_the_grace_keep_working_past_the_superseded_tokens()
    {
        var rotated = Revoked(RevokeReason.Rotated, TimeSpan.FromMinutes(4));

        Decide(rotated, Revoked(RevokeReason.Superseded, TimeSpan.FromMinutes(3)), Revoked(RevokeReason.Superseded, TimeSpan.FromMinutes(1)), Valid())
            .ShouldBe(MobileRefreshCase.LostAnswerRetry);
    }

    [Fact]
    public void The_grace_is_counted_from_the_rotation_of_the_presented_token_and_includes_its_last_second()
    {
        Decide(Revoked(RevokeReason.Rotated, Grace), Valid()).ShouldBe(MobileRefreshCase.LostAnswerRetry);
        Decide(Revoked(RevokeReason.Rotated, Grace + TimeSpan.FromMilliseconds(1)), Valid()).ShouldBe(MobileRefreshCase.StaleReplay);
    }

    [Fact]
    public void A_rotated_token_presented_after_the_grace_while_its_successors_were_never_used_is_a_stale_replay()
        => Decide(Revoked(RevokeReason.Rotated, TimeSpan.FromHours(3)), Revoked(RevokeReason.Superseded, TimeSpan.FromHours(2)), Valid())
            .ShouldBe(MobileRefreshCase.StaleReplay);

    [Fact]
    public void A_rotated_token_whose_session_already_reached_its_end_is_only_refused()
    {
        var expired = new Token(Now.AddSeconds(-1), null, null);

        Decide(Revoked(RevokeReason.Rotated, TimeSpan.FromMinutes(1)), expired).ShouldBe(MobileRefreshCase.SessionExpired);
        Decide(Revoked(RevokeReason.Rotated, TimeSpan.FromDays(31)), expired).ShouldBe(MobileRefreshCase.SessionExpired);
    }

    [Fact]
    public void A_rotated_token_whose_successor_was_used_means_two_holders()
    {
        Decide(Revoked(RevokeReason.Rotated, TimeSpan.FromSeconds(10)), Revoked(RevokeReason.Rotated, TimeSpan.FromSeconds(5)))
            .ShouldBe(MobileRefreshCase.Theft);
        Decide(Revoked(RevokeReason.Rotated, TimeSpan.FromHours(1)), Revoked(RevokeReason.Superseded, TimeSpan.FromMinutes(59)), Revoked(RevokeReason.Rotated, TimeSpan.FromMinutes(30)))
            .ShouldBe(MobileRefreshCase.Theft);
    }

    [Fact]
    public void A_superseded_token_presented_is_theft_inside_the_grace_or_not()
    {
        Decide(Revoked(RevokeReason.Superseded, TimeSpan.FromSeconds(10)), Valid()).ShouldBe(MobileRefreshCase.Theft);
        Decide(Revoked(RevokeReason.Superseded, TimeSpan.FromDays(2)), Valid()).ShouldBe(MobileRefreshCase.Theft);
    }

    [Theory]
    [InlineData(RevokeReason.SignedOut)]
    [InlineData(RevokeReason.Revoked)]
    public void A_signed_out_or_revoked_token_is_only_refused(RevokeReason reason)
        => Decide(Revoked(reason, TimeSpan.FromSeconds(10))).ShouldBe(MobileRefreshCase.Refused);

    [Theory]
    [InlineData(RevokeReason.SignedOut)]
    [InlineData(RevokeReason.Revoked)]
    public void A_rotated_token_of_a_session_that_has_since_ended_is_only_refused(RevokeReason reason)
        => Decide(Revoked(RevokeReason.Rotated, TimeSpan.FromMinutes(1)), Revoked(reason, TimeSpan.FromSeconds(30)))
            .ShouldBe(MobileRefreshCase.Refused);

    [Fact]
    public void A_revoked_token_without_a_reason_or_a_successor_is_only_refused()
    {
        Decide(new Token(Now.AddDays(1), Now.AddMinutes(-1), null)).ShouldBe(MobileRefreshCase.Refused);
        Decide(Revoked(RevokeReason.Rotated, TimeSpan.FromMinutes(1))).ShouldBe(MobileRefreshCase.Refused);
    }
}
