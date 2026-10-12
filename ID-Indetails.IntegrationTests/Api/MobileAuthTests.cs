using ID_Indetails.api.Constants;
using ID_Indetails.api.DTO.Response.Auth;
using Microsoft.IdentityModel.JsonWebTokens;
using Shouldly;

namespace ID_Indetails.IntegrationTests.Api;

/// <summary>The phone app's sessions: 30 days, the token in the body, and the replay rules of docs/authentication.md.</summary>
public class MobileAuthTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    private readonly AuthTestSteps _steps = new(api);
    private HttpClient Client => _steps.Client;

    private async Task<MobileAuthResponse> PhoneLoginAsync(string email, string password)
    {
        var response = await Client.PostJsonAsync("/api/mobile/auth/login", new { email, password });
        response.IsSuccessStatusCode.ShouldBeTrue(await response.Content.ReadAsStringAsync());
        return await response.ReadAsync<MobileAuthResponse>();
    }

    private Task<HttpResponseMessage> RefreshAsync(string refreshToken)
        => Client.PostJsonAsync("/api/mobile/auth/refresh", new { refreshToken });

    private async Task<MobileAuthResponse> ShouldRefreshAsync(string refreshToken)
    {
        var response = await RefreshAsync(refreshToken);
        response.IsSuccessStatusCode.ShouldBeTrue(await response.Content.ReadAsStringAsync());
        return await response.ReadAsync<MobileAuthResponse>();
    }

    private async Task<string> WebCookieAsync(string email, string password)
        => (await Client.PostJsonAsync("/api/auth/login", new { email, password })).RefreshCookie()!;

    private Task BackdateRotationsAsync(long userId, int minutes)
        => _steps.ExecuteAsync(
            "UPDATE refresh_tokens SET revoked_at = DATE_SUB(revoked_at, INTERVAL @m MINUTE) WHERE user_id = @u AND revoked_at IS NOT NULL",
            ("@m", minutes), ("@u", userId));

    private static string SessionOf(string accessToken) => new JsonWebToken(accessToken).GetClaim(AuthClaimTypes.SessionId).Value;

    [Fact]
    public async Task Signing_in_on_the_phone_returns_both_tokens_in_the_body_for_30_days()
    {
        var (email, password, _) = await _steps.ActiveSuperAdminAsync();

        var response = await Client.PostJsonAsync("/api/mobile/auth/login", new { email, password });
        var auth = await response.ReadAsync<MobileAuthResponse>();

        response.SetCookieHeaders().ShouldBeEmpty();
        auth.RefreshToken.ShouldNotBeNullOrWhiteSpace();
        auth.RefreshTokenExpiresAt.ShouldBe(DateTime.UtcNow.AddDays(30), TimeSpan.FromMinutes(1));
        auth.User.Email.ShouldBe(email);
        Guid.TryParse(SessionOf(auth.AccessToken), out _).ShouldBeTrue();
        (await Client.GetAsync("/api/auth/me", auth.AccessToken)).IsSuccessStatusCode.ShouldBeTrue();
        (await _steps.ScalarAsync<string>("SELECT client FROM refresh_tokens WHERE user_id = @u ORDER BY id DESC LIMIT 1", ("@u", auth.User.Id)))
            .ShouldBe("mobile");
    }

    [Fact]
    public async Task Signing_in_on_the_phone_answers_like_the_website()
    {
        var (email, password, _) = await _steps.ActiveSuperAdminAsync();

        await (await Client.PostJsonAsync("/api/mobile/auth/login", new { email, password = "not-the-password" }))
            .ShouldFailWithAsync(401, ErrorCodes.InvalidCredentials);
        await (await Client.PostJsonAsync("/api/mobile/auth/login", new { email = AuthTestSteps.NewEmail(), password }))
            .ShouldFailWithAsync(401, ErrorCodes.InvalidCredentials);
        await (await RefreshAsync("")).ShouldFailWithAsync(401, ErrorCodes.RefreshTokenInvalid);
        await (await RefreshAsync("made-up-value")).ShouldFailWithAsync(401, ErrorCodes.RefreshTokenInvalid);
    }

    [Fact]
    public async Task Suspended_sign_in_is_refused_on_the_phone_at_login_and_at_refresh()
    {
        var (_, _, superAdmin) = await _steps.ActiveSuperAdminAsync();
        var (email, password, person) = await _steps.InviteAndAcceptAsync(superAdmin, RoleCodes.Employee, DomainCodes.Design);
        var phone = await PhoneLoginAsync(email, password);

        (await Client.PostEmptyAsync($"/api/employees/{person.User.Id}/sessions/end", superAdmin.AccessToken)).IsSuccessStatusCode.ShouldBeTrue();

        await (await RefreshAsync(phone.RefreshToken)).ShouldFailWithAsync(401, ErrorCodes.RefreshTokenInvalid);
        await (await Client.PostJsonAsync("/api/mobile/auth/login", new { email, password })).ShouldFailWithAsync(403, ErrorCodes.SignInSuspended);
    }

    [Fact]
    public async Task A_refresh_rotates_the_token_within_the_same_session_and_the_old_one_stays_retryable_for_a_while()
    {
        var (email, password, _) = await _steps.ActiveSuperAdminAsync();
        var first = await PhoneLoginAsync(email, password);

        var second = await ShouldRefreshAsync(first.RefreshToken);
        second.RefreshToken.ShouldNotBe(first.RefreshToken);
        SessionOf(second.AccessToken).ShouldBe(SessionOf(first.AccessToken));

        // the answer was lost: the phone sends the first token again, twice, and keeps getting a working one
        await ShouldRefreshAsync(first.RefreshToken);
        var retriedAgain = await ShouldRefreshAsync(first.RefreshToken);
        SessionOf(retriedAgain.AccessToken).ShouldBe(SessionOf(first.AccessToken));
        var next = await ShouldRefreshAsync(retriedAgain.RefreshToken);
        (await Client.GetAsync("/api/auth/me", next.AccessToken)).IsSuccessStatusCode.ShouldBeTrue();

        (await _steps.ScalarAsync<int>(
                "SELECT COUNT(*) FROM refresh_tokens WHERE user_id = @u AND revoke_reason = 'superseded'", ("@u", first.User.Id)))
            .ShouldBe(2);
    }

    [Fact]
    public async Task A_superseded_token_coming_back_means_theft_and_ends_every_session()
    {
        var (email, password, _) = await _steps.ActiveSuperAdminAsync();
        var phone = await PhoneLoginAsync(email, password);
        var otherPhone = await PhoneLoginAsync(email, password);
        var web = await WebCookieAsync(email, password);

        // someone else got hold of the first token and used it; the phone, whose answer never came, retries
        var attacker = await ShouldRefreshAsync(phone.RefreshToken);
        await ShouldRefreshAsync(phone.RefreshToken);

        // the attacker's token was superseded by the phone's retry; using it ends everything, everywhere
        await (await RefreshAsync(attacker.RefreshToken)).ShouldFailWithAsync(401, ErrorCodes.RefreshTokenInvalid);
        await (await RefreshAsync(otherPhone.RefreshToken)).ShouldFailWithAsync(401, ErrorCodes.RefreshTokenInvalid);
        await (await Client.PostEmptyAsync("/api/auth/refresh", refreshCookie: web)).ShouldFailWithAsync(401, ErrorCodes.RefreshTokenInvalid);
        await (await Client.GetAsync("/api/auth/me", otherPhone.AccessToken)).ShouldFailWithAsync(401, ErrorCodes.Unauthenticated);
    }

    [Fact]
    public async Task An_old_token_coming_back_after_a_newer_one_was_used_means_two_holders()
    {
        var (email, password, _) = await _steps.ActiveSuperAdminAsync();
        var first = await PhoneLoginAsync(email, password);
        var otherPhone = await PhoneLoginAsync(email, password);

        var second = await ShouldRefreshAsync(first.RefreshToken);
        await ShouldRefreshAsync(second.RefreshToken);

        // inside the grace still, but the token after it was itself used
        await (await RefreshAsync(first.RefreshToken)).ShouldFailWithAsync(401, ErrorCodes.RefreshTokenInvalid);
        await (await RefreshAsync(otherPhone.RefreshToken)).ShouldFailWithAsync(401, ErrorCodes.RefreshTokenInvalid);
    }

    [Fact]
    public async Task A_stale_replay_ends_only_that_session()
    {
        var (email, password, _) = await _steps.ActiveSuperAdminAsync();
        var phone = await PhoneLoginAsync(email, password);
        var otherPhone = await PhoneLoginAsync(email, password);
        var web = await WebCookieAsync(email, password);

        var second = await ShouldRefreshAsync(phone.RefreshToken);
        await BackdateRotationsAsync(phone.User.Id, 10);

        await (await RefreshAsync(phone.RefreshToken)).ShouldFailWithAsync(401, ErrorCodes.RefreshTokenInvalid);
        // this phone's session is over, nobody else's
        await (await RefreshAsync(second.RefreshToken)).ShouldFailWithAsync(401, ErrorCodes.RefreshTokenInvalid);
        await ShouldRefreshAsync(otherPhone.RefreshToken);
        (await Client.PostEmptyAsync("/api/auth/refresh", refreshCookie: web)).IsSuccessStatusCode.ShouldBeTrue();
    }

    [Fact]
    public async Task A_retry_after_the_session_reached_its_end_is_only_refused()
    {
        var (email, password, _) = await _steps.ActiveSuperAdminAsync();
        var phone = await PhoneLoginAsync(email, password);
        var otherPhone = await PhoneLoginAsync(email, password);

        var second = await ShouldRefreshAsync(phone.RefreshToken);
        await _steps.ExecuteAsync(
            "UPDATE refresh_tokens SET expires_at = DATE_SUB(UTC_TIMESTAMP(3), INTERVAL 1 MINUTE) WHERE session_id = @s AND revoked_at IS NULL",
            ("@s", SessionOf(second.AccessToken)));

        await (await RefreshAsync(phone.RefreshToken)).ShouldFailWithAsync(401, ErrorCodes.RefreshTokenInvalid);
        await (await RefreshAsync(second.RefreshToken)).ShouldFailWithAsync(401, ErrorCodes.RefreshTokenInvalid);
        await ShouldRefreshAsync(otherPhone.RefreshToken);
    }

    [Fact]
    public async Task Refreshes_at_the_same_moment_never_leave_two_live_tokens_in_a_session()
    {
        var (email, password, _) = await _steps.ActiveSuperAdminAsync();
        var phone = await PhoneLoginAsync(email, password);

        var answers = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => RefreshAsync(phone.RefreshToken)));

        foreach (var answer in answers.Where(a => !a.IsSuccessStatusCode))
        {
            await answer.ShouldFailWithAsync(401, ErrorCodes.RefreshTokenReused);
        }

        answers.ShouldContain(a => a.IsSuccessStatusCode);
        (await _steps.ScalarAsync<int>(
                "SELECT COUNT(*) FROM refresh_tokens WHERE session_id = @s AND revoked_at IS NULL", ("@s", SessionOf(phone.AccessToken))))
            .ShouldBe(1);
    }

    [Fact]
    public async Task Signing_out_ends_only_this_phone_s_session_and_always_answers_204()
    {
        var (email, password, _) = await _steps.ActiveSuperAdminAsync();
        var phone = await PhoneLoginAsync(email, password);
        var otherPhone = await PhoneLoginAsync(email, password);

        ((int)(await Client.PostJsonAsync("/api/mobile/auth/logout", new { refreshToken = phone.RefreshToken })).StatusCode).ShouldBe(204);
        ((int)(await Client.PostJsonAsync("/api/mobile/auth/logout", new { refreshToken = "made-up-value" })).StatusCode).ShouldBe(204);

        await (await RefreshAsync(phone.RefreshToken)).ShouldFailWithAsync(401, ErrorCodes.RefreshTokenInvalid);
        await ShouldRefreshAsync(otherPhone.RefreshToken);
        (await _steps.ScalarAsync<string>(
                "SELECT revoke_reason FROM refresh_tokens WHERE session_id = @s", ("@s", SessionOf(phone.AccessToken))))
            .ShouldBe("signed_out");
    }

    [Fact]
    public async Task Changing_the_password_on_the_phone_ends_every_other_session_and_keeps_this_one()
    {
        var (email, password, _) = await _steps.ActiveSuperAdminAsync();
        var phone = await PhoneLoginAsync(email, password);
        var otherPhone = await PhoneLoginAsync(email, password);
        var web = await WebCookieAsync(email, password);

        await (await Client.PostJsonAsync("/api/mobile/auth/change-password",
                new { currentPassword = "wrong-password", newPassword = "another-good-passphrase" }, phone.AccessToken))
            .ShouldFailWithAsync(400, ErrorCodes.CurrentPasswordIncorrect);

        var response = await Client.PostJsonAsync("/api/mobile/auth/change-password",
            new { currentPassword = password, newPassword = "another-good-passphrase" }, phone.AccessToken);
        response.IsSuccessStatusCode.ShouldBeTrue(await response.Content.ReadAsStringAsync());
        var changed = await response.ReadAsync<MobileAuthResponse>();

        SessionOf(changed.AccessToken).ShouldBe(SessionOf(phone.AccessToken));
        (await Client.GetAsync("/api/auth/me", changed.AccessToken)).IsSuccessStatusCode.ShouldBeTrue();
        await ShouldRefreshAsync(changed.RefreshToken);
        await (await RefreshAsync(phone.RefreshToken)).ShouldFailWithAsync(401, ErrorCodes.RefreshTokenInvalid);
        await (await RefreshAsync(otherPhone.RefreshToken)).ShouldFailWithAsync(401, ErrorCodes.RefreshTokenInvalid);
        await (await Client.PostEmptyAsync("/api/auth/refresh", refreshCookie: web)).ShouldFailWithAsync(401, ErrorCodes.RefreshTokenInvalid);
    }

    [Fact]
    public async Task The_website_s_sessions_are_unchanged_and_never_mix_with_the_phone_s()
    {
        var (email, password, _) = await _steps.ActiveSuperAdminAsync();
        var phone = await PhoneLoginAsync(email, password);

        var login = await Client.PostJsonAsync("/api/auth/login", new { email, password });
        var web = await login.ShouldSignInAsync();
        var cookie = login.RefreshCookie()!;
        new JsonWebToken(web.AccessToken).TryGetClaim(AuthClaimTypes.SessionId, out _).ShouldBeFalse();
        (await _steps.ScalarAsync<int>(
                "SELECT COUNT(*) FROM refresh_tokens WHERE token_hash = SHA2(@c, 256) AND client = 'web' AND session_id IS NOT NULL " +
                "AND expires_at BETWEEN DATE_ADD(UTC_TIMESTAMP(3), INTERVAL 23 HOUR) AND DATE_ADD(UTC_TIMESTAMP(3), INTERVAL 25 HOUR)",
                ("@c", cookie)))
            .ShouldBe(1);

        // each token only works where it was issued, and trying it elsewhere ends nothing
        await (await Client.PostEmptyAsync("/api/auth/refresh", refreshCookie: phone.RefreshToken)).ShouldFailWithAsync(401, ErrorCodes.RefreshTokenInvalid);
        await (await RefreshAsync(cookie)).ShouldFailWithAsync(401, ErrorCodes.RefreshTokenInvalid);
        ((int)(await Client.PostJsonAsync("/api/mobile/auth/logout", new { refreshToken = cookie })).StatusCode).ShouldBe(204);

        var rotated = await Client.PostEmptyAsync("/api/auth/refresh", refreshCookie: cookie);
        rotated.IsSuccessStatusCode.ShouldBeTrue();
        await ShouldRefreshAsync(phone.RefreshToken);

        // the website's 30-second rule is as it was: the old cookie at once is only refused
        await (await Client.PostEmptyAsync("/api/auth/refresh", refreshCookie: cookie)).ShouldFailWithAsync(401, ErrorCodes.RefreshTokenInvalid);
        (await Client.PostEmptyAsync("/api/auth/refresh", refreshCookie: rotated.RefreshCookie())).IsSuccessStatusCode.ShouldBeTrue();
        (await _steps.ScalarAsync<string>("SELECT revoke_reason FROM refresh_tokens WHERE token_hash = SHA2(@c, 256)", ("@c", cookie)))
            .ShouldBe("rotated");
    }
}
